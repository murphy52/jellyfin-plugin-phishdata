using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PhishNet.Providers.ExternalIds;

/// <summary>
/// Supplies the Phish.net show page link for movies tagged with a Phish.net provider ID.
/// Jellyfin 10.11+ removed <c>IExternalId.UrlFormatString</c>; external links now come from this interface.
/// </summary>
public class PhishNetExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => "Phish.net";

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        if (item is not Movie)
        {
            yield break;
        }

        // The provider ID is stored as the full show URL (permalink or date-based fallback).
        if (item.TryGetProviderId(PhishNetExternalId.ProviderKey, out var url) && !string.IsNullOrWhiteSpace(url))
        {
            yield return url;
        }
    }
}
