using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.PhishNet.Providers.ExternalIds;

/// <summary>
/// External ID provider for Phish.net show pages.
/// </summary>
public class PhishNetExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Phish.net";

    /// <inheritdoc />
    public string Key => "PhishNet";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public string UrlFormatString => "https://phish.net/show/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}
