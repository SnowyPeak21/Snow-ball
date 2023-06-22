﻿using Agora.Addons.Disqord.Extensions;
using Disqord;
using Disqord.Extensions.Interactivity.Menus;
using Emporia.Extensions.Discord;
using Emporia.Extensions.Discord.Features.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Addons.Disqord.Menus.View
{
    public class AuctionAcceptView : ServerSettingsView
    {
        private readonly GuildSettingsContext _context;
        private readonly IDiscordGuildSettings _settings;

        public AuctionAcceptView(GuildSettingsContext context, List<GuildSettingsOption> settingsOptions = null) : base(context, settingsOptions)
        {
            _context = context;
            _settings = context.Settings.DeepClone();

            foreach (var button in EnumerateComponents().OfType<ButtonViewComponent>())
            {
                if (button.Position == 1)
                    button.Label = $"{(_settings.Features.AcceptOffers ? "Disable" : "Enable")} Early Acceptance";
            }
        }

        [Button(Label = "Early Acceptance", Style = LocalButtonComponentStyle.Primary, Position = 1, Row = 4)]
        public ValueTask RecallListings(ButtonEventArgs e)
        {
            _settings.Flags = _settings.Features.ToggleFlag(SettingsFlags.AcceptOffers);

            e.Button.Label = $"{(_settings.Features.AcceptOffers ? "Disable" : "Enable")} Early Acceptance";

            MessageTemplate = message => message.WithEmbeds(_settings.ToEmbed("Allow Early Acceptance"));

            ReportChanges();

            return default; ;
        }

        [Button(Label = "Save", Style = LocalButtonComponentStyle.Success, Position = 3, Row = 4, Emoji = "💾")]
        public async ValueTask SaveBidingOptions(ButtonEventArgs e)
        {
            if (_settings.Flags == _context.Settings.Flags) return;

            var settings = (DefaultDiscordGuildSettings)_context.Settings;

            settings.Flags = _settings.Flags;

            using var scope = _context.Services.CreateScope();
            {
                scope.ServiceProvider.GetRequiredService<IInteractionContextAccessor>().Context = new DiscordInteractionContext(e);

                await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new UpdateGuildSettingsCommand(settings));

                MessageTemplate = message => message.WithEmbeds(settings.ToEmbed());
            }

            foreach (ButtonViewComponent button in EnumerateComponents().OfType<ButtonViewComponent>())
                if (button.Label != "Close") button.IsDisabled = true;

            ReportChanges();

            return;
        }

        protected override string GetCustomId(InteractableViewComponent component)
        {
            if (component is ButtonViewComponent buttonComponent) return $"#{buttonComponent.Label}";

            return base.GetCustomId(component);
        }
    }
}
