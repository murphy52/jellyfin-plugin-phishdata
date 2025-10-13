# Changelog

All notable changes to the Jellyfin Phish.net Plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Community Ratings Integration**: Fetch and aggregate user ratings from Phish.net reviews
  - Scales ratings from 1-5 (Phish.net) to 1-10 (Jellyfin)
  - Fetches up to 50 reviews per show for accurate community rating
  - Handles shows with no reviews gracefully
- **Comprehensive Image Provider**: Multi-source image fetching with intelligent fallback
  - **Venue Images**: Google Places API integration for venue photography
  - **Show-Specific Photos**: Social media integration (Instagram, Twitter, Reddit)
  - **Fan Photography**: Flickr Creative Commons and community photo search
  - **Official Artwork**: LivePhish and Phish.net image support
  - **Quality Filtering**: Intelligent ranking, deduplication, and resolution preference
- **Person Provider Enhancement**: Dedicated Phish band member profiles
  - Individual profiles for Trey Anastasio, Mike Gordon, Page McConnell, Jon Fishman
  - Comprehensive biographies with musical background and band history
  - Accurate birth dates and biographical information
- **Enhanced Metadata Fields**: Complete Jellyfin metadata population
  - Smart naming format: "Phish - YYYY-MM-DD - Venue, Location"
  - Comprehensive overview with formatted setlist display
  - Production year and premiere date mapping
  - Hard-coded genres: "Concert" and "Live Music"
  - Rich tagging system with venue, location, and Phish-specific tags
  - External provider IDs for cross-reference compatibility
- **Advanced Configuration Options**: Extended plugin configuration
  - Social media API key support (Instagram, Twitter, Flickr)
  - Google Places API integration for venue images
  - Image quality and source preferences
  - Configurable review count limits (default: 50)
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