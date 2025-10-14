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

/// <summary>
/// External ID provider for Phish.net setlist pages.
/// </summary>
public class PhishNetSetlistExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Phish.net Setlist";

    /// <inheritdoc />
    public string Key => "PhishNetSetlist";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public string UrlFormatString => "https://phish.net/setlists/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}

/// <summary>
/// External ID provider for Phish.net venue pages.
/// </summary>
public class PhishNetVenueExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Phish.net Venue";

    /// <inheritdoc />
    public string Key => "PhishNetVenue";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public string UrlFormatString => "https://phish.net/venue/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}

/// <summary>
/// External ID provider for Phish.net show reviews.
/// </summary>
public class PhishNetReviewsExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => "Phish.net Reviews";

    /// <inheritdoc />
    public string Key => "PhishNetReviews";

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    /// <inheritdoc />
    public string UrlFormatString => "https://phish.net/show/{0}/reviews";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item)
    {
        return item is Movie;
    }
}