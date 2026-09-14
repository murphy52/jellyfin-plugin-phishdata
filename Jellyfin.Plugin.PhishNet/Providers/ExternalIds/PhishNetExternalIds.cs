using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.PhishNet.Providers.ExternalIds;

/// <summary>
/// External ID provider for Phish.net show pages.
/// The link itself is produced by <see cref="PhishNetExternalUrlProvider"/>.
/// </summary>
public class PhishNetExternalId : IExternalId
{
    /// <summary>
    /// The provider ID key under which the Phish.net show URL is stored.
    /// </summary>
    public const string ProviderKey = "PhishNet";

    /// <inheritdoc />
    public string ProviderName => "Phish.net";

    /// <inheritdoc />
    public string Key => ProviderKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}
