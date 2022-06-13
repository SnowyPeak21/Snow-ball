﻿using Agora.Addons.Disqord.Extensions;
using Disqord;
using Disqord.Extensions.Interactivity.Menus;
using Emporia.Extensions.Discord;
using Emporia.Extensions.Discord.Features.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Addons.Disqord.Menus.View
{
    public class ResultChannelView : ChannelSelectionView
    {
        public ResultChannelView(GuildSettingsContext context, List<GuildSettingsOption> settingsOptions)
            : base(context, settingsOptions, message => message.AddEmbed(context.Settings.ToEmbed(settingsOptions.FirstOrDefault(s => s.IsDefault)?.Name)))
        {
            DefaultView = () => new MainSettingsView(context);
            CurrentChannelId = context.Settings.ResultLogChannelId;
        }

        public async override ValueTask SaveChannelAsync(SelectionEventArgs e)
        {
            var settings = (DefaultDiscordGuildSettings)Context.Settings;
            settings.ResultLogChannelId = SelectedChannelId;

            using var scope = Context.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IInteractionContextAccessor>().Context = new DiscordInteractionContext(e);

            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new UpdateGuildSettingsCommand(settings));

            MessageTemplate = message => message.WithEmbeds(settings.ToEmbed("Result Logs", new LocalEmoji("📃")));

            return;
        }
    }
}
