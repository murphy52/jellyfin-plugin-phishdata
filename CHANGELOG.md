# Changelog

All notable changes to the Jellyfin Phish.net Plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Planned
- Enhanced real API integration for image sources
- Advanced social media photo integration
- AI-generated fallback images for venues
- Community photo contribution system

## [1.1.5] - 2024-10-13

### Fixed
- Enhanced plugin icon support with direct `GetPluginImage()` method implementation
- Improved embedded resource icon loading for consistent display across Jellyfin states
- Added additional icon formats (`logo.png`) for better compatibility

## [1.1.4] - 2024-10-13

### Added
- Plugin icon embedded as resource (`thumb.png`) for consistent branding
- Enhanced icon display in Jellyfin plugin catalog and "My Plugins"

### Fixed
- Plugin icon now properly embedded and accessible across all plugin states
- Updated project file to include embedded icon resources

## [1.1.3] - 2024-10-13

### Fixed
- **Critical JSON Deserialization Fix**: Resolved review ID parsing errors
  - Fixed `ReviewDto.ReviewId` property to handle integer IDs instead of strings
  - Plugin now successfully loads metadata for shows with reviews
  - Eliminates "Cannot deserialize JSON" errors during metadata fetching

### Improved
- Enhanced error handling for API response parsing
- Better logging for JSON deserialization troubleshooting

## [1.1.2] - 2024-10-13

### Fixed
- **Configuration Page Loading**: Resolved "no settings to set up" issue
- Fixed embedded resource path for configuration HTML page
- Changed from dynamic to hardcoded embedded resource path for stability
- Configuration page now loads properly with all API key and preference fields

## [1.1.1] - 2024-10-13

### Fixed
- Updated plugin manifest with correct release information
- Improved build and release automation
- Enhanced GitHub Actions workflow for consistent releases

## [1.1.0] - 2024-10-13

### Added
- **Community Ratings Integration**: Fetch and aggregate user ratings from Phish.net reviews
  - Scales ratings from 1-5 (Phish.net) to 1-10 (Jellyfin) 
  - Fetches up to 50 reviews per show for accurate community rating
  - Handles shows with no reviews gracefully
- **Comprehensive Image Provider**: Multi-source image fetching with intelligent fallback
  - **PhishImageProvider**: Complete framework for Primary, Backdrop, Thumb images
  - **Multi-Source Strategy**: Supports venue photos, show-specific images, generic concert images
  - **External Services**: `ExternalImageService` and `ShowPhotoService` architecture
  - **Quality Framework**: Image ranking, deduplication, and resolution handling
- **Person Provider Enhancement**: Dedicated Phish band member profiles
  - Individual profiles for Trey Anastasio, Mike Gordon, Page McConnell, Jon Fishman
  - Comprehensive biographies with musical background and band history
  - Accurate birth dates and biographical information
- **Enhanced Metadata Fields**: Complete Jellyfin metadata population
  - Smart naming format with multi-night run detection: "N2 Phish Hampton 11-22-1997"
  - Comprehensive overview with formatted setlist display (setlist-first format)
  - Production year and premiere date mapping
  - Hard-coded genres: "Concert" and "Live Music"
  - Rich tagging system with venue, location, and Phish-specific tags
  - External provider IDs for cross-reference compatibility
- **Plugin Catalog Integration**: Professional plugin distribution
  - Complete `manifest.json` for Jellyfin plugin catalog
  - Automated build and release system with GitHub Actions
  - Plugin icon and metadata for catalog display
  - Versioning and changelog integration
- **Advanced Configuration Options**: Extended plugin configuration
  - Configurable API key management with validation
  - Review and setlist preference controls
  - Image provider settings framework
  - Cache duration and performance settings
- **Comprehensive Testing Suite**: Extensive test coverage
  - Unit tests for filename parsing with edge cases
  - Movie and person provider unit tests
  - API client tests with mocked HTTP responses
  - Interactive MetadataTest project for development
  - Full test coverage for parsing logic and provider functionality

### Enhanced
- **Filename Parsing Improvements**: More robust show date extraction
  - Support for various date formats (YYYY-MM-DD, YYYY.MM.DD, etc.)
  - Enhanced venue and location parsing
  - Better handling of multi-night shows and special events
- **API Client Robustness**: Improved error handling and response parsing
  - Better timeout handling and retry logic
  - Enhanced JSON deserialization with null safety
  - Improved logging for debugging and troubleshooting
- **Service Architecture**: Clean separation of concerns
  - Dedicated services for external images and show photos
  - Modular design for easy extension and maintenance
  - Proper dependency injection and lifecycle management

### Technical Improvements
- **Build System**: Updated project structure and dependencies
  - .NET 8.0 target framework alignment
  - Resolved package version conflicts between projects
  - Clean project references and dependency management
- **Code Quality**: Enhanced code organization and documentation
  - Comprehensive XML documentation for public APIs
  - Proper async/await patterns throughout
  - Consistent error handling and logging
- **Performance**: Optimized API calls and caching
  - Efficient HTTP client usage with connection pooling
  - Intelligent caching strategies for metadata and images
  - Reduced API call frequency with smart fallbacks

### Documentation
- **Installation Guide**: Comprehensive setup instructions
- **API Integration Guide**: Detailed documentation for image provider implementation
- **Image Provider Strategy**: Complete strategy document for multi-source image fetching
- **Implementation Guide**: Technical implementation details and architecture
- **Jellyfin Fields Status**: Complete mapping of supported metadata fields

## [1.0.0] - Initial Release

### Added
- Basic Phish.net API integration
- Show metadata fetching (date, venue, location)
- Setlist integration with song listings
- Smart filename parsing for show matching
- Plugin configuration with API key support
- Basic error handling and logging

### Core Features
- Movie metadata provider for Phish concerts
- Integration with Phish.net API v5
- Support for common Phish video file naming conventions
- Jellyfin plugin framework integration