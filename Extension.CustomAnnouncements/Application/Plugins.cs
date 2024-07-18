﻿using Agora.Addons.Disqord.Common;
using Agora.Addons.Disqord.Interfaces;
using Emporia.Domain.Entities;
using Emporia.Domain.Services;

namespace Extension.CustomAnnouncements.Application;

public class CustomAnnouncement(AnnouncementProcessingService announcementService) : IPluginExtension
{
    public async ValueTask<IResult> Execute(PluginParameters parameters)
    {
        var listing = parameters.GetValue<Listing>("Listing");

        var announcement = await announcementService.GetAnnouncementMessageAsync(listing);

        if (announcement is null) return Result<string>.Failure("No custom announcement configured");

        return Result.Success(announcement);
    }
}
