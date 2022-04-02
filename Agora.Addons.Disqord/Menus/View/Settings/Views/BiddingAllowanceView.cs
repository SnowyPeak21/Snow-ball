﻿using Agora.Addons.Disqord.Extensions;
using Disqord;
using Disqord.Extensions.Interactivity.Menus;
using Emporia.Extensions.Discord;
using Emporia.Extensions.Discord.Features.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Agora.Addons.Disqord.Menus.View
{
    internal class BiddingAllowanceView : BaseGuildSettingsView
    {
        private readonly GuildSettingsContext _context;
        private readonly IDiscordGuildSettings _settings;
        private readonly ButtonViewComponent ShillBiddingButton;
        private readonly ButtonViewComponent AbsenteeBiddingButton;

        public BiddingAllowanceView(GuildSettingsContext context, List<GuildSettingsOption> settingsOptions = null) : base(context, settingsOptions) 
        {
            _context = context;
            _settings = context.Settings.DeepClone();
            
            ShillBiddingButton = new ButtonViewComponent(ShillBidding)
            {
                Label = $"{(_settings.AllowShillBidding ? "Disable" : "Enable")} Shill Bidding",
                Style = LocalButtonComponentStyle.Primary,
                Position = 1,
                Row = 1
            };

            AbsenteeBiddingButton = new ButtonViewComponent(AbsenteeBidding)
            {
                Label = $"{(_settings.AllowAbsenteeBidding ? "Disable" : "Enable")} Absentee Bidding",
                Style = LocalButtonComponentStyle.Primary,
                Position = 2,
                Row = 1
            };

            var saveButton = new ButtonViewComponent(SaveBidingOptions)
            {
                Label = "Save",
                Style = LocalButtonComponentStyle.Success,
                Emoji = new LocalEmoji("💾"),
                Position = 3,
                Row = 1
            };

            AddComponent(ShillBiddingButton);
            AddComponent(AbsenteeBiddingButton);
            AddComponent(saveButton);
        }

        private ValueTask ShillBidding(ButtonEventArgs e)
        {
            _settings.AllowShillBidding = !_settings.AllowShillBidding;
            ShillBiddingButton.Label = $"{(_settings.AllowShillBidding ? "Disable" : "Enable")} Shill Bidding";

            Selection.Options.FirstOrDefault(x => x.Label == "Shill Bidding").IsDefault = true;
            Selection.Options.FirstOrDefault(x => x.Label == "Absentee Bidding").IsDefault = false;

            TemplateMessage.WithEmbeds(_settings.AsEmbed("Shill Bidding"));

            ReportChanges();

            return default; ;
        }
        
        private ValueTask AbsenteeBidding(ButtonEventArgs e)
        {
            _settings.AllowAbsenteeBidding = !_settings.AllowAbsenteeBidding;
            AbsenteeBiddingButton.Label = $"{(_settings.AllowAbsenteeBidding ? "Disable" : "Enable")} Absentee Bidding";

            Selection.Options.FirstOrDefault(x => x.Label == "Shill Bidding").IsDefault = false;
            Selection.Options.FirstOrDefault(x => x.Label == "Absentee Bidding").IsDefault = true;
            
            TemplateMessage.WithEmbeds(_settings.AsEmbed("Absentee Bidding"));

            ReportChanges();

            return default;
        }

        public async ValueTask SaveBidingOptions(ButtonEventArgs e)
        {
            if (_settings.AllowShillBidding == _context.Settings.AllowShillBidding
                && _settings.AllowAbsenteeBidding == _context.Settings.AllowAbsenteeBidding) return;

            using (var scope = _context.Services.CreateScope())
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var settings = (DefaultDiscordGuildSettings)_context.Settings;

                settings.AllowShillBidding = _settings.AllowShillBidding;
                settings.AllowAbsenteeBidding = _settings.AllowAbsenteeBidding;

                await mediator.Send(new UpdateGuildSettingsCommand(settings));

                TemplateMessage.WithEmbeds(settings.AsEmbed());
            }

            foreach (ButtonViewComponent button in EnumerateComponents().OfType<ButtonViewComponent>())
                button.IsDisabled = true;

            ReportChanges();

            return;
        }
    }
}
