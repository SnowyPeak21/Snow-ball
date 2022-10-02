﻿using Disqord;
using Disqord.Rest;
using Emporia.Application.Features.Commands;
using Emporia.Domain.Common;
using MediatR;

namespace Agora.Addons.Disqord
{
    public partial class PersistentInteractionService
    {
        private readonly Dictionary<string, Func<IComponentInteraction, LocalInteractionModalResponse>> _modalRedirect = new()
        {
            { "extendAuction", ExtendListingModal },
            { "extendMarket", ExtendListingModal },
            { "extendTrade", ExtendListingModal },
            { "editAuction", EditAuctionListingModal },
            { "editMarket", EditMarketListingModal },
            { "editTrade", EditTradeListingModal },
            { "claim", PartialPurchaseModal }
        };

        private static IBaseRequest HandleInteraction(IComponentInteraction interaction, ulong showroomId) => interaction.CustomId switch
        {
            "buy" => new CreatePaymentCommand(new EmporiumId(interaction.GuildId.Value), new ShowroomId(showroomId), ReferenceNumber.Create(interaction.Message.Id)),
            "trade" => new CreateTradeOfferCommand(new EmporiumId(interaction.GuildId.Value), new ShowroomId(showroomId), ReferenceNumber.Create(interaction.Message.Id)),
            "undobid" => new UndoBidCommand(new EmporiumId(interaction.GuildId.Value), new ShowroomId(showroomId), ReferenceNumber.Create(interaction.Message.Id)),
            "minbid" => new CreateBidCommand(new EmporiumId(interaction.GuildId.Value), new ShowroomId(showroomId), ReferenceNumber.Create(interaction.Message.Id), 0) { UseMinimum = true },
            "maxbid" => new CreateBidCommand(new EmporiumId(interaction.GuildId.Value), new ShowroomId(showroomId), ReferenceNumber.Create(interaction.Message.Id), 0) { UseMaximum = true },
            { } when interaction.CustomId.StartsWith("accept") => new AcceptListingCommand(new EmporiumId(interaction.GuildId.Value), new ShowroomId(showroomId), ReferenceNumber.Create(interaction.Message.Id), interaction.CustomId.Replace("accept", "")),
            { } when interaction.CustomId.StartsWith("withdraw") => new WithdrawListingCommand(new EmporiumId(interaction.GuildId.Value), new ShowroomId(showroomId), ReferenceNumber.Create(interaction.Message.Id), interaction.CustomId.Replace("withdraw", "")),
            _ => null
        };

        private static Task HandleResponse(IComponentInteraction interaction) => interaction.CustomId switch
        {
            { } when interaction.CustomId.StartsWith("withdraw") => interaction.Response().SendMessageAsync(new LocalInteractionMessageResponse().WithContent("Listing successfully withdrawn!").WithIsEphemeral(true)),
            "buy" => interaction.Response().HasResponded
                   ? interaction.Followup().SendAsync(new LocalInteractionMessageResponse().WithContent("Congratulations on your purchase!").WithIsEphemeral(true))
                   : interaction.Response().SendMessageAsync(new LocalInteractionMessageResponse().WithContent("Congratulations on your purchase!").WithIsEphemeral(true)),
            _ => Task.CompletedTask
        };

        private static LocalInteractionModalResponse ExtendListingModal(IComponentInteraction interaction)
        {
            return new LocalInteractionModalResponse().WithCustomId($"{interaction.CustomId}:{interaction.Message.Id}")
                .WithTitle($"Extend Expiration")
                .WithComponents(
                    LocalComponent.Row(
                        LocalComponent.TextInput("extendTo", "Extend End To (date - yyyy-mm-dd 15:00)", TextInputComponentStyle.Short)
                            .WithMinimumInputLength(2)
                            .WithMaximumInputLength(16)
                            .WithIsRequired(false)),
                    LocalComponent.Row(
                        LocalComponent.TextInput("extendBy", "Extend End By (duration - 5d)", TextInputComponentStyle.Short)
                            .WithMinimumInputLength(2)
                            .WithMaximumInputLength(16)
                            .WithIsRequired(false)));
        }

        private static LocalInteractionModalResponse EditAuctionListingModal(IComponentInteraction interaction)
        {
            return new LocalInteractionModalResponse().WithCustomId($"{interaction.CustomId}:{interaction.Message.Id}")
                .WithTitle("Edit Auction Listing")
                .WithComponents(
                    LocalComponent.Row(LocalComponent.TextInput("image", "Update Image", TextInputComponentStyle.Short).WithPlaceholder("Insert image url").WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("description", "Update Description", TextInputComponentStyle.Paragraph).WithPlaceholder("Item description").WithMaximumInputLength(500).WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("message", "Update Buyer's Note", TextInputComponentStyle.Paragraph).WithPlaceholder("Hidden message").WithMaximumInputLength(250).WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("minIncrease", "Update Minimum Increment", TextInputComponentStyle.Short).WithPlaceholder("0").WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("maxIncrease", "Update Maximum Increment", TextInputComponentStyle.Short).WithPlaceholder("0").WithIsRequired(false)));
        }

        private static LocalInteractionModalResponse EditMarketListingModal(IComponentInteraction interaction)
        {
            return new LocalInteractionModalResponse().WithCustomId($"{interaction.CustomId}:{interaction.Message.Id}")
                .WithTitle("Edit Market Listing")
                .WithComponents(
                    LocalComponent.Row(LocalComponent.TextInput("image", "Update Image", TextInputComponentStyle.Short).WithPlaceholder("Insert image url").WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("description", "Update Description", TextInputComponentStyle.Paragraph).WithPlaceholder("Item description").WithMaximumInputLength(500).WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("message", "Update Buyer's Note", TextInputComponentStyle.Paragraph).WithPlaceholder("Hidden message").WithMaximumInputLength(250).WithIsRequired(false)));
        }

        private static LocalInteractionModalResponse EditTradeListingModal(IComponentInteraction interaction)
        {
            return new LocalInteractionModalResponse().WithCustomId($"{interaction.CustomId}:{interaction.Message.Id}")
                .WithTitle("Edit Trade Listing")
                .WithComponents(
                    LocalComponent.Row(LocalComponent.TextInput("image", "Update Image", TextInputComponentStyle.Short).WithPlaceholder("Insert image url").WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("description", "Update Description", TextInputComponentStyle.Paragraph).WithPlaceholder("Item description").WithMaximumInputLength(500).WithIsRequired(false)),
                    LocalComponent.Row(LocalComponent.TextInput("message", "Update Buyer's Note", TextInputComponentStyle.Paragraph).WithPlaceholder("Hidden message").WithMaximumInputLength(250).WithIsRequired(false)));
        }

        private static LocalInteractionModalResponse PartialPurchaseModal(IComponentInteraction interaction)
        {
            return new LocalInteractionModalResponse().WithCustomId($"{interaction.CustomId}:{interaction.Message.Id}")
                .WithTitle("Purchase Items")
                .WithComponents(LocalComponent.Row(LocalComponent.TextInput("amount", "Amount to Claim", TextInputComponentStyle.Short).WithPlaceholder("0").WithIsRequired()));
        }
    }
}
