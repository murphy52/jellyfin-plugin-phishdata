# Jellyfin Phish.net Plugin Architecture

## Overview

This document outlines the architecture of the Jellyfin Phish.net metadata provider plugin. The plugin is designed to automatically fetch show information, setlists, venue details, and other metadata for Phish concert videos using the Phish.net API v5.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                      Jellyfin Server                           │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐    ┌─────────────────┐    ┌──────────────┐ │
│  │   Video Files   │    │  Metadata DB    │    │   Web UI     │ │
│  └─────────────────┘    └─────────────────┘    └──────────────┘ │
├─────────────────────────────────────────────────────────────────┤
│                    Plugin Framework                             │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │              Phish.net Plugin                               │ │
│  │                                                             │ │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │ │
│  │  │   Providers     │  │   API Layer     │  │ Configuration│ │ │
│  │  │                 │  │                 │  │              │ │ │
│  │  │ • Movie Provider│  │ • PhishNet API  │  │ • Settings   │ │ │
│  │  │ • Person Provider│ │ • HTTP Client   │  │ • Cache Mgmt │ │ │
│  │  │ • Image Provider│  │ • Response Cache│  │ • Validation │ │ │
│  │  │ • Service Layer │  │ • Rate Limiting │  │ • DI System  │ │ │
│  │  │ • External APIs │  │ • Error Handling│  │              │ │ │
│  │  └─────────────────┘  └─────────────────┘  └──────────────┘ │ │
│  │                                                             │ │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │ │
│  │  │   Utilities     │  │   Data Models   │  │   Mappers    │ │ │
│  │  │                 │  │                 │  │              │ │ │
│  │  │ • File Parser   │  │ • Show DTO      │  │ • API to     │ │ │
│  │  │ • Date Parser   │  │ • Setlist DTO   │  │   Jellyfin   │ │ │
│  │  │ • Name Matcher  │  │ • Venue DTO     │  │ • Jellyfin   │ │ │
│  │  │ • Fuzzy Search  │  │ • Review DTO    │  │   to Display │ │ │
│  │  │ • Logging       │  │ • Cache DTO     │  │              │ │ │
│  │  └─────────────────┘  └─────────────────┘  └──────────────┘ │ │
│  └─────────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────┤
│                     External APIs                              │
├─────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                  Phish.net API v5                           │ │
│  │                                                             │ │
│  │  • Shows Endpoint      • Venues Endpoint                   │ │
│  │  • Setlists Endpoint   • Songs Endpoint                    │ │
│  │  • Reviews Endpoint    • JamCharts Endpoint                │ │
│  │  • Artists Endpoint    • Users Endpoint                    │ │
│  └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

## Core Components

### 1. Plugin Core (`Plugin.cs`)
- **Responsibility**: Main entry point, dependency injection, HTTP client factory
- **Features**: Configuration management, plugin lifecycle, web pages

### 2. Configuration System
- **`PluginConfiguration.cs`**: Configuration data model
- **`configPage.html`**: Web-based configuration UI
- **Features**: API key management, preferences, caching settings

### 3. API Layer
- **`PhishNetApiClient.cs`**: Primary API interface
- **`PhishNetApiModels.cs`**: API response data models
- **Features**: HTTP requests, authentication, rate limiting, error handling

### 4. Metadata Providers
Based on Jellyfin's metadata provider system:

#### Core Providers (Implemented)
- **`PhishMovieProvider.cs`**: Complete movie metadata for Phish shows
  - Smart title generation with multi-night run detection
  - Setlist integration and community ratings
  - Venue information and production metadata
- **`PhishPersonProvider.cs`**: Band member profiles and biographies
  - Individual profiles for all four band members
  - Birth dates, roles, and comprehensive biographies
- **`PhishImageProvider.cs`**: Multi-source image provider
  - Venue photos, show-specific images, concert artwork
  - Support for Primary, Backdrop, and Thumb image types
  - Integration with ExternalImageService and ShowPhotoService

#### Supporting Services
- **`ExternalImageService.cs`**: Venue and generic image fetching
- **`ShowPhotoService.cs`**: Show-specific photo search and aggregation
- **`PluginServiceRegistrator.cs`**: Dependency injection registration

### 5. Utilities and Helpers
- **`FileNameParser.cs`**: Parse various Phish file naming conventions
- **`DateParser.cs`**: Handle date formats (YYYY-MM-DD, etc.)
- **`ShowMatcher.cs`**: Match files to shows using fuzzy logic
- **`CacheManager.cs`**: Local caching for API responses

## Data Flow

### 1. File Discovery
```
Video File → File Name Parser → Show Date/Venue Extraction → Show Matcher
```

### 2. Metadata Retrieval
```
Show Match → API Client → Phish.net API → Response Cache → Data Mappers → Jellyfin Metadata
```

### 3. Caching Strategy
```
API Request → Cache Check → [Cache Hit: Return] → [Cache Miss: API Call] → Cache Store → Return Data
```

## File Naming Convention Support

The plugin will support these common Phish file naming patterns:

1. **Standard Date Format**: `phish1997-11-22.mkv`, `1997-11-22.mp4`
2. **Date + Venue**: `1997-11-22 Hampton Coliseum.avi`
3. **Artist + Date + Venue**: `Phish - 1997-11-22 - Hampton, VA.mp4`  
4. **Multi-Part Shows**: `phish1995-12-31d1.mkv`, `phish1995-12-31d2.mkv`
5. **Set-Based**: `1999-07-04 Set 1.mkv`, `1999-07-04 Set 2.mkv`
6. **Song-Based**: `01 - Wilson.mkv`, `02 - Runaway Jim.mkv`

## API Integration

### Phish.net API v5 Endpoints

1. **Shows**: `/v5/shows/artist/phish.json?showdate=YYYY-MM-DD`
2. **Setlists**: `/v5/setlists/showdate/YYYY-MM-DD.json`
3. **Venues**: `/v5/venues/venueid/{id}.json`
4. **Reviews**: `/v5/reviews/showdate/YYYY-MM-DD.json`
5. **JamCharts**: `/v5/jamcharts/showdate/YYYY-MM-DD.json`

### Authentication
- API Key required for all requests
- Passed as `apikey` parameter in query string
- Rate limiting: ~1 request per second (to be confirmed)

### Response Caching
- Default: 24 hours for show data
- Configurable duration (1-168 hours)
- Cache invalidation on plugin settings change
- Separate cache for images vs. metadata

## Error Handling Strategy

### API Errors
1. **Invalid API Key**: Clear error message, link to key registration
2. **Rate Limiting**: Exponential backoff, queue management  
3. **Network Errors**: Retry logic with timeout
4. **Data Not Found**: Graceful fallback, logging for manual review

### File Parsing Errors
1. **Invalid Date Format**: Log warning, attempt alternative parsing
2. **Ambiguous Matches**: Present options to user, log for improvement
3. **No Show Found**: Log for community contribution to Phish.net

## Performance Considerations

### Caching Strategy
- **Memory Cache**: Recently accessed shows (LRU, max 100 items)
- **Disk Cache**: Long-term storage for all fetched data
- **Image Cache**: Separate cache for artwork and venue photos

### Rate Limiting
- **Request Queue**: FIFO with 1-second intervals
- **Batch Operations**: Group related API calls where possible
- **Background Processing**: Non-blocking metadata retrieval

### Optimization
- **Lazy Loading**: Fetch additional data (reviews, jam charts) on demand
- **Parallel Processing**: Multiple file parsing operations
- **Smart Matching**: Cache show index for faster lookups

## Security Considerations

### API Key Protection
- Store encrypted in plugin configuration
- Never log API keys in plain text
- Validate key format before API calls

### Input Validation
- Sanitize all file names and paths
- Validate date ranges and formats
- Prevent API injection attacks

## Testing Strategy

### Unit Tests
- API client functionality
- File name parsing edge cases
- Date parsing and validation
- Show matching algorithms

### Integration Tests
- End-to-end metadata retrieval
- Cache functionality
- Error handling scenarios
- Configuration management

### Performance Tests  
- Large library scanning
- Concurrent API requests
- Cache performance under load
- Memory usage patterns

## Deployment Architecture

### Plugin Package
```
Jellyfin.Plugin.PhishNet.dll
├── Configuration/
│   └── configPage.html
├── Providers/
│   ├── Movies/
│   ├── Series/  
│   ├── Episodes/
│   └── Images/
├── API/
│   ├── Client/
│   ├── Models/
│   └── Cache/
└── Utilities/
    ├── Parsers/
    ├── Matchers/
    └── Helpers/
```

### Dependencies
- **Jellyfin.Controller** (10.10.7+): Core Jellyfin APIs
- **Microsoft.Extensions.Http** (8.0.1): HTTP client factory
- **System.Text.Json** (8.0.0): JSON serialization
- **.NET 8.0**: Runtime requirement

## Future Enhancements

### Phase 1 (MVP)
- Basic show metadata retrieval
- Standard file name parsing
- Simple caching

### Phase 2
- Image provider for venue photos
- Review integration
- Advanced file name patterns

### Phase 3
- Jam chart integration
- Community rating display
- Smart show recommendations

### Phase 4
- Multi-artist support (Trey, Mike, etc.)
- Tour and venue browsing
- Advanced search features

This architecture provides a solid foundation for the Phish.net plugin while allowing for incremental development and future enhancements.