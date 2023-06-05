﻿using Agora.Addons.Disqord.Extensions;
using Agora.Shared.Attributes;
using Agora.Shared.Extensions;
using Agora.Shared.Services;
using Disqord;
using Disqord.Bot;
using Disqord.Bot.Commands;
using Disqord.Gateway;
using Disqord.Http;
using Disqord.Rest;
using Emporia.Domain.Common;
using Emporia.Domain.Entities;
using Emporia.Extensions.Discord;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qommon;

namespace Agora.Addons.Disqord
{
    [AgoraService(AgoraServiceAttribute.ServiceLifetime.Transient)]
    public sealed class ProductListingService : AgoraService, IProductListingService
    {
        private readonly DiscordBotBase _agora;
        private readonly IGuildSettingsService _settingsService;
        private readonly ICommandContextAccessor _commandAccessor;
        private readonly IInteractionContextAccessor _interactionAccessor;

        public EmporiumId EmporiumId { get; set; }
        public ShowroomId ShowroomId { get; set; }

        public ProductListingService(DiscordBotBase bot,
                                        IGuildSettingsService settingsService,
                                        ICommandContextAccessor commandAccessor,
                                        IInteractionContextAccessor interactionAccessor,
                                        ILogger<MessageProcessingService> logger) : base(logger)
        {
            _agora = bot;
            _settingsService = settingsService;
            _commandAccessor = commandAccessor;
            _interactionAccessor = interactionAccessor;
        }

        public async ValueTask<ReferenceNumber> PostProductListingAsync(Listing productListing)
        {
            await CheckPermissionsAsync(EmporiumId.Value,
                                        ShowroomId.Value,
                                        Permissions.ViewChannels | Permissions.SendMessages | Permissions.SendEmbeds);

            var channelId = ShowroomId.Value;
            var channel = _agora.GetChannel(EmporiumId.Value, channelId);
            var categorization = await GetCategoryAsync(productListing);
            var settings = await _settingsService.GetGuildSettingsAsync(EmporiumId.Value);
            var message = new LocalMessage().AddEmbed(productListing.ToEmbed().WithCategory(categorization))
                                            .WithComponents(productListing.Buttons(settings.AllowAcceptingOffer));

            if (channel is CachedForumChannel forum)
                return ReferenceNumber.Create(await CreateForumPostAsync(forum, message, productListing, categorization));

            if (channel is CachedCategoryChannel)
                channelId = await CreateCategoryChannelAsync(productListing);

            var response = await _agora.SendMessageAsync(channelId, message);

            try
            {
                if (channel is ITextChannel textChannel && textChannel.Type == ChannelType.News)
                {
                    await CheckPermissionsAsync(EmporiumId.Value, ShowroomId.Value, Permissions.ManageMessages);
                    await textChannel.CrosspostMessageAsync(response.Id);
                }
            }
            catch (Exception) { }

            if (channelId != ShowroomId.Value) await response.PinAsync();

            return ReferenceNumber.Create(response.Id);
        }

        public async ValueTask<ReferenceNumber> UpdateProductListingAsync(Listing productListing, bool refreshMessage = true)
        {
            var categorization = await GetCategoryAsync(productListing);
            var productEmbeds = new List<LocalEmbed>() { productListing.ToEmbed().WithCategory(categorization) };

            if (productListing.Product.Carousel.Count > 1) productEmbeds.AddRange(productListing.WithImages());

            var channelId = ShowroomId.Value;
            var channel = _agora.GetChannel(EmporiumId.Value, channelId);
            var settings = await _settingsService.GetGuildSettingsAsync(EmporiumId.Value);

            if (channel is CachedCategoryChannel or CachedForumChannel)
                channelId = productListing.ReferenceCode.Reference();

            try
            {
                if (refreshMessage) await RefreshProductListingAsync(productListing, productEmbeds, channelId, settings);

                if (channel is IForumChannel forumChannel
                    && (productListing.Status == ListingStatus.Active || productListing.Status == ListingStatus.Locked || productListing.Status == ListingStatus.Sold))
                    forumChannel = await UpdateForumTagAsync(productListing, forumChannel);

                return productListing.Product.ReferenceNumber;
            }
            catch (RestApiException api) when (api.StatusCode == HttpResponseStatusCode.NotFound)
            {
                //ignore these, the message doesn't exist anymore
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to update product listing: {exception}", ex);
            }

            return null;
        }

        private async Task<IForumChannel> UpdateForumTagAsync(Listing productListing, IForumChannel forumChannel)
        {
            forumChannel = await EnsureForumTagsExistAsync(forumChannel, AgoraTag.Active, AgoraTag.Expired, AgoraTag.Locked, AgoraTag.Sold, AgoraTag.Soon);

            var pending = forumChannel.Tags.FirstOrDefault(x => x.Name.Equals("Pending", StringComparison.OrdinalIgnoreCase))?.Id;
            var active = forumChannel.Tags.FirstOrDefault(x => x.Name.Equals("Active", StringComparison.OrdinalIgnoreCase))?.Id;
            var locked = forumChannel.Tags.FirstOrDefault(x => x.Name.Equals("Locked", StringComparison.OrdinalIgnoreCase))?.Id;
            var soon = forumChannel.Tags.FirstOrDefault(x => x.Name.Equals("Ending Soon", StringComparison.OrdinalIgnoreCase))?.Id;
            var thread = (IThreadChannel)_agora.GetChannel(EmporiumId.Value, productListing.Product.ReferenceNumber.Value);

            try
            {
                if (productListing.Status != ListingStatus.Sold)
                {
                    var isActive = productListing.ScheduledPeriod.ScheduledStart <= SystemClock.Now && active != null;
                    var isSoon = productListing.ExpiresIn <= TimeSpan.FromHours(1) && soon != null;

                    if (isActive)
                    {
                        var tagIds = thread.TagIds.Where(tag => tag != pending.GetValueOrDefault() && tag != locked.GetValueOrDefault()).ToList();

                        var count = tagIds.Count;

                        if (!tagIds.Contains(active.Value)) tagIds.Add(active.Value);
                        
                        if (isSoon && !tagIds.Contains(soon.Value)) tagIds.Add(soon.Value);

                        if (count != tagIds.Count)
                            await ModifyThreadTagsAsync(thread, tagIds);
                    }
                    
                    if (soon != null && !isSoon && thread.TagIds.Contains(soon.Value))
                        await ModifyThreadTagsAsync(thread, thread.TagIds.Where(tag => tag != pending.GetValueOrDefault() && tag != soon.GetValueOrDefault() && tag != locked.GetValueOrDefault()).ToList());
                }
                else if (locked != null)
                {
                    await ModifyThreadTagsAsync(thread, thread.TagIds.Where(tag => tag != pending.GetValueOrDefault() && tag != active.GetValueOrDefault() && tag != soon.GetValueOrDefault()).Append(locked.Value).ToList());
                }
            }
            catch (Exception)
            {
                // Failed to update tags on forum post
            }

            return forumChannel;
        }

        private async Task ModifyThreadTagsAsync(IThreadChannel thread, List<Snowflake> tagIds) => await _agora.ModifyThreadChannelAsync(thread.Id, x =>
        {
            x.TagIds = tagIds.Distinct().ToArray();
        });

        private async Task RefreshProductListingAsync(Listing productListing, List<LocalEmbed> productEmbeds, ulong channelId, IDiscordGuildSettings settings)
        {
            if (_interactionAccessor.Context == null
                || (_interactionAccessor.Context.Interaction is IComponentInteraction component
                && component.Message.Id != productListing.Product.ReferenceNumber.Value))
            {
                await _agora.ModifyMessageAsync(channelId, productListing.Product.ReferenceNumber.Value, x =>
                {
                    x.Embeds = productEmbeds;
                    x.Components = productListing.Buttons(settings.AllowAcceptingOffer);
                });
            }
            else
            {
                var interaction = _interactionAccessor.Context.Interaction;

                if (interaction.Response().HasResponded)
                    await interaction.Followup().ModifyResponseAsync(x =>
                    {
                        x.Embeds = productEmbeds;
                        x.Components = productListing.Buttons(settings.AllowAcceptingOffer);
                    });
                else
                    await interaction.Response().ModifyMessageAsync(new LocalInteractionMessageResponse()
                    {
                        Embeds = productEmbeds,
                        Components = productListing.Buttons(settings.AllowAcceptingOffer)
                    });
            }
        }

        public async ValueTask<ReferenceNumber> OpenBarteringChannelAsync(Listing listing)
        {
            var channel = _agora.GetChannel(EmporiumId.Value, ShowroomId.Value);

            if (channel is CachedCategoryChannel or CachedForumChannel) return default;

            await CheckPermissionsAsync(EmporiumId.Value,
                                        ShowroomId.Value,
                                        Permissions.ViewChannels | Permissions.SendMessages | Permissions.SendEmbeds | Permissions.ReadMessageHistory |
                                        Permissions.ManageThreads | Permissions.CreatePublicThreads | Permissions.SendMessagesInThreads);

            var duration = listing.ScheduledPeriod.Duration switch
            {
                var minutes when minutes < TimeSpan.FromMinutes(60) => TimeSpan.FromHours(1),
                var hours when hours < TimeSpan.FromHours(24) => TimeSpan.FromDays(1),
                var days when days < TimeSpan.FromDays(3) => TimeSpan.FromDays(3),
                _ => TimeSpan.FromDays(7),
            };

            try
            {
                var product = listing.Product;
                var thread = await _agora.CreatePublicThreadAsync(ShowroomId.Value,
                                                                  $"[{listing.ReferenceCode.Code()}] {product.Title}",
                                                                  product.ReferenceNumber.Value,
                                                                  x => x.AutomaticArchiveDuration = duration);

                await thread.SendMessageAsync(new LocalMessage().WithContent("Execute commands for this item HERE!"));

                return ReferenceNumber.Create(thread.Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to create bartering channel");
            }

            return default;
        }

        public async ValueTask CloseBarteringChannelAsync(Listing productListing)
        {
            try
            {
                var channelId = productListing.Product.ReferenceNumber.Value;
                var showroom = _agora.GetChannel(EmporiumId.Value, ShowroomId.Value);

                if (showroom == null) return;

                if (showroom is CachedCategoryChannel or CachedForumChannel)
                    channelId = productListing.ReferenceCode.Reference();

                var settings = await _settingsService.GetGuildSettingsAsync(EmporiumId.Value);

                if (productListing.Status != ListingStatus.Withdrawn && showroom is IForumChannel forum)
                {
                    if (_interactionAccessor != null && _interactionAccessor.Context != null)
                    {
                        var interaction = _interactionAccessor.Context.Interaction;

                        if (interaction.Response().HasResponded)
                            await interaction.Followup().SendAsync(new LocalInteractionFollowup().WithContent("Transaction Closed!").WithIsEphemeral());
                        else
                            await interaction.Response().SendMessageAsync(new LocalInteractionMessageResponse().WithContent("Transaction Closed!").WithIsEphemeral(true));
                    }

                    if (_agora.GetChannel(EmporiumId.Value, channelId) is not CachedThreadChannel post) return;

                    forum = await TagClosedPostAsync(productListing, forum, post);

                    if (settings.InlineResults) return;

                    await post.ModifyAsync(x =>
                    {
                        x.IsArchived = true;
                        x.IsLocked = true;
                    });
                }
                else
                {
                    var channel = _agora.GetChannel(EmporiumId.Value, channelId);

                    if (channel == null) return;

                    if (settings.InlineResults && productListing.Status > ListingStatus.Withdrawn)
                        await _agora.DeleteMessageAsync(channel.Id, productListing.Product.ReferenceNumber.Value);
                    else
                        await _agora.DeleteChannelAsync(channelId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to close bartering channel.");
            }

            return;
        }

        private async Task<IForumChannel> TagClosedPostAsync(Listing productListing, IForumChannel forum, CachedThreadChannel post)
        {
            forum = await EnsureForumTagsExistAsync(forum, AgoraTag.Expired, AgoraTag.Locked, AgoraTag.Sold);

            var active = forum.Tags.FirstOrDefault(x => x.Name.Equals("Active", StringComparison.OrdinalIgnoreCase))?.Id;
            var locked = forum.Tags.FirstOrDefault(x => x.Name.Equals("Locked", StringComparison.OrdinalIgnoreCase))?.Id;

            if (productListing.Status == ListingStatus.Sold)
            {
                var sold = forum.Tags.FirstOrDefault(x => x.Name.Equals("Sold", StringComparison.OrdinalIgnoreCase))?.Id;

                if (sold.HasValue && !post.TagIds.Contains(sold.Value))
                    await _agora.ModifyThreadChannelAsync(productListing.Product.ReferenceNumber.Value, x =>
                    {
                        x.TagIds = post.TagIds.Where(tag => tag != active.GetValueOrDefault() && tag != locked.GetValueOrDefault()).Append(sold.Value).ToArray();
                    });
            }
            else if (productListing.Status == ListingStatus.Expired)
            {
                var expired = forum.Tags.FirstOrDefault(x => x.Name.Equals("Expired", StringComparison.OrdinalIgnoreCase))?.Id;

                if (expired.HasValue && !post.TagIds.Contains(expired.Value))
                    await _agora.ModifyThreadChannelAsync(productListing.Product.ReferenceNumber.Value, x =>
                    {
                        x.TagIds = post.TagIds.Where(tag => tag != active.GetValueOrDefault() && tag != locked.GetValueOrDefault()).Append(expired.Value).ToArray();
                    });
            }

            return forum;
        }

        public async ValueTask RemoveProductListingAsync(Listing productListing)
        {
            var channel = _agora.GetChannel(EmporiumId.Value, ShowroomId.Value);

            if (channel is CachedCategoryChannel or CachedForumChannel) return;

            try
            {
                await _agora.DeleteMessageAsync(ShowroomId.Value, productListing.Product.ReferenceNumber.Value);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to remove product listing {id} in {channel}.", productListing.Product.ReferenceNumber.Value, ShowroomId.Value);
            }

            return;
        }

        private async ValueTask CheckPermissionsAsync(ulong guildId, ulong channelId, Permissions permissions)
        {
            var currentMember = _agora.GetCurrentMember(guildId);
            var channel = _agora.GetChannel(guildId, channelId) ?? throw new NoMatchFoundException($"Unable to verify channel permissions for {Mention.Channel(channelId)}");
            var channelPerms = currentMember.CalculateChannelPermissions(channel);

            if (!channelPerms.HasFlag(permissions))
            {
                var message = $"The bot lacks the necessary permissions ({permissions & ~channelPerms}) to post to {Mention.Channel(ShowroomId.Value)}";
                var feedbackId = _interactionAccessor?.Context?.ChannelId ?? _commandAccessor?.Context?.ChannelId;

                if (feedbackId.HasValue && feedbackId != channelId)
                {
                    await _agora.SendMessageAsync(feedbackId.Value, new LocalMessage().AddEmbed(new LocalEmbed().WithDescription(message).WithColor(Color.Red)));
                }
                else
                {
                    var settings = await _agora.Services.GetRequiredService<IGuildSettingsService>().GetGuildSettingsAsync(guildId);

                    if (settings.AuditLogChannelId != 0)
                        await _agora.SendMessageAsync(settings.AuditLogChannelId, new LocalMessage().AddEmbed(new LocalEmbed().WithDescription(message).WithColor(Color.Red)));
                    else if (settings.ResultLogChannelId > 1)
                        await _agora.SendMessageAsync(settings.ResultLogChannelId, new LocalMessage().AddEmbed(new LocalEmbed().WithDescription(message).WithColor(Color.Red)));
                }

                throw new InvalidOperationException(message);
            }

            return;
        }

        private async ValueTask<ulong> CreateForumPostAsync(IForumChannel forum, LocalMessage message, Listing productListing, string category)
        {
            await CheckPermissionsAsync(EmporiumId.Value, ShowroomId.Value, Permissions.ManageThreads | Permissions.SendMessagesInThreads | Permissions.ManageChannels);

            forum = await EnsureForumTagsExistAsync(forum, AgoraTag.Pending, AgoraTag.Active, AgoraTag.Soon, AgoraTag.Expired, AgoraTag.Sold);

            var type = productListing.Type.ToString().Replace("Market", "Sale");
            var price = productListing.Type == ListingType.Market ? $"({productListing.ValueTag})" : string.Empty;
            var tags = new List<Snowflake>();

            var pendingTag = forum.Tags.FirstOrDefault(x => x.Name.Equals("Pending", StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
            var activeTag = forum.Tags.First(x => x.Name.Equals("Active", StringComparison.OrdinalIgnoreCase))?.Id ?? 0;
            var endingSoonTag = forum.Tags.First(x => x.Name.Equals("Ending Soon", StringComparison.OrdinalIgnoreCase))?.Id ?? 0;

            if (productListing.ScheduledPeriod.ScheduledStart >= SystemClock.Now.AddSeconds(5))
            {
                tags.Add(pendingTag);
            }
            else
            {
                tags.Add(activeTag);

                if (productListing.ExpiresIn <= TimeSpan.FromHours(1)) tags.Add(endingSoonTag);
            }

            if (category != string.Empty)
                tags.AddRange(await GetTagIdsAsync(forum, category.Split(':')));

            var expiration = Markdown.Timestamp(productListing.ExpiresAt(), Markdown.TimestampFormat.RelativeTime);

            message.WithContent($"Expiration: {expiration}\n");

            if (type.Equals("Auction"))
            {
                var item = (AuctionItem)productListing.Product;
                var bids = productListing is VickreyAuction 
                         ? $"Bids: {item.Offers.Count}"
                         : $"Current Bid: {(item.Offers.Count == 0 ? "None" : productListing.ValueTag)}";

                message.Content += bids;
            }

            var showroom = await forum.CreateThreadAsync($"[{type}] {productListing.Product.Title} {price}", message, x =>
            {
                x.AutomaticArchiveDuration = TimeSpan.FromDays(7);
                x.TagIds = tags.Take(20).ToArray();
            });

            await showroom.AddMemberAsync(productListing.Owner.ReferenceNumber.Value);
            productListing.SetReference(ReferenceCode.Create($"{productListing.ReferenceCode}:{showroom.Id}"));

            return showroom.Id.RawValue;
        }

        private static async ValueTask<IForumChannel> EnsureForumTagsExistAsync(IForumChannel forum, params LocalForumTag[] tagsToAdd)
        {
            var tagAdded = false;
            var tags = forum.Tags.Select(x => LocalForumTag.CreateFrom(x));

            foreach (var tag in tagsToAdd)
            {
                if (tags.Any(x => x.Name.Value.Equals(tag.Name.Value, StringComparison.OrdinalIgnoreCase))) continue;

                tags = tags.Append(tag);
                tagAdded = true;
            }

            if (tagAdded)
                return await forum.ModifyAsync(x => x.Tags = tags.Take(20).ToArray());

            return forum;
        }

        private static async ValueTask<IEnumerable<Snowflake>> GetTagIdsAsync(IForumChannel forum, string[] tagNames)
        {
            var tagAdded = false;
            var tags = forum.Tags.Select(x => LocalForumTag.CreateFrom(x));

            foreach (var name in tagNames)
            {
                if (tags.Any(x => x.Name.Value.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) continue;

                tags = tags.Append(new LocalForumTag() { Emoji = new LocalEmoji("📁"), Name = name.Trim() });
                tagAdded = true;
            }

            if (tagAdded)
                forum = await forum.ModifyAsync(x => x.Tags = tags.Take(20).ToArray());

            return forum.Tags.Where(x => tagNames.Any(name => name.Trim().Equals(x.Name))).Select(x => x.Id);
        }

        private async ValueTask<ulong> CreateCategoryChannelAsync(Listing productListing)
        {
            await CheckPermissionsAsync(EmporiumId.Value, ShowroomId.Value, Permissions.ManageChannels | Permissions.ManageMessages);

            var showroom = await _agora.CreateTextChannelAsync(EmporiumId.Value,
                                                               productListing.Product.Title.Value,
                                                               x => x.CategoryId = Optional.Create(new Snowflake(ShowroomId.Value)));

            productListing.SetReference(ReferenceCode.Create($"{productListing.ReferenceCode}:{showroom.Id}"));

            return showroom.Id.RawValue;
        }

        private async Task<string> GetCategoryAsync(Listing productListing)
        {
            string categorization = string.Empty;
            var subcategoryId = productListing.Product?.SubCategoryId;

            if (subcategoryId != null)
            {
                var emporium = await _agora.Services.GetRequiredService<IEmporiaCacheService>().GetEmporiumAsync(productListing.Owner.EmporiumId.Value);
                var category = emporium.Categories.FirstOrDefault(c => c.SubCategories.Any(s => s.Id.Equals(subcategoryId)));
                var subcategory = category.SubCategories.FirstOrDefault(s => s.Id.Equals(subcategoryId));

                categorization = $"{category.Title}{(subcategory.Title.Equals(category.Title.Value) ? "" : $": {subcategory.Title}")}";
            }

            return categorization;
        }

        public async ValueTask NotifyPendingListingAsync(Listing productListing)
        {
            var channelReference = productListing.ReferenceCode.Reference();
            var channelId = channelReference == 0 ? productListing.Product.ReferenceNumber.Value : channelReference;
            var messageId = productListing.Product.ReferenceNumber.Value;

            var offer = productListing.CurrentOffer;
            var showroom = (IMessageChannel)_agora.GetChannel(EmporiumId.Value, channelId);
            var prompt = $"Action required for pending [transaction]({Discord.MessageJumpLink(EmporiumId.Value, channelId, messageId)})";
            var submission = $"Review offer submitted by {Mention.User(offer.UserReference.Value)} -> {Markdown.Bold(offer.Submission)}.";

            await showroom.SendMessageAsync(
                new LocalMessage()
                    .WithContent(Mention.User(productListing.Owner.ReferenceNumber.Value))
                    .AddEmbed(
                        new LocalEmbed()
                            .WithDefaultColor()
                            .WithDescription(prompt + Environment.NewLine + submission)));

            return;
        }
    }
}
