# Jellyfin Phish.net Metadata Plugin

## About

This plugin adds a metadata provider for Phish concert videos using the [Phish.net API v5](https://docs.phish.net/). It automatically fetches show information, setlists, venue details, and concert metadata for your Phish video collection in Jellyfin.

## Features

### Core Metadata
- **Show Metadata**: Automatically fetch show date, venue, location, and attendance information
- **Setlist Integration**: Display complete setlists with song titles, set breaks, and encore information in overview
- **Venue Information**: Get venue names, locations, and capacity details
- **Smart Naming Format**: Intelligent title generation (e.g., "Phish - 1997-11-22 - Hampton Coliseum, Hampton, VA")
- **Production Details**: Premiere date, production year, and external provider IDs
- **Genre Classification**: Automatic categorization as "Concert" and "Live Music"
- **Comprehensive Tags**: Including "Phish", venue, location, and show-specific tags

### Community Features
- **Community Ratings**: Aggregate user ratings from Phish.net reviews (scaled 1-10)
- **Review Integration**: Pull user reviews and show ratings from Phish.net (up to 50 per show)
- **Jam Chart Data**: Access notable jams and performance ratings from the community

### Image Provider
- **Multi-Source Image Fetching**: Comprehensive image provider with fallback hierarchy
- **Venue Images**: Curated venue photography from multiple sources
- **Show-Specific Photos**: Social media and fan photography integration
- **Official Artwork**: LivePhish and Phish.net image support
- **Quality Filtering**: Intelligent ranking and deduplication of images

### Person Provider
- **Band Member Profiles**: Dedicated profiles for Trey, Mike, Page, and Fish
- **Detailed Biographies**: Comprehensive band member information
- **Birth Dates**: Accurate biographical data for each member

### Smart Matching
- **Intelligent File Parsing**: Advanced filename parsing to match video files with shows
- **Flexible Naming**: Support for various file naming conventions commonly used by Phish collectors
- **Multi-Format Support**: Works with various video file formats and naming patterns

## Supported File Naming Conventions

The plugin supports various naming patterns commonly used in the Phish community:

```
Phish Videos/
├── 1997-11-22 - Hampton Coliseum/
│   ├── phish1997-11-22.mkv
│   ├── Phish - 1997-11-22 - Hampton, VA.mp4
│   └── 1997-11-22 Hampton Complete Show.avi
├── 1999-07-04 - Oswego County Airport/
│   ├── Set 1/
│   │   ├── 01 - Wilson.mkv
│   │   └── 02 - Runaway Jim.mkv
│   └── Set 2/
│       ├── 01 - Down with Disease.mkv
│       └── 02 - Tweezer.mkv
└── Shows by Year/
    └── 1995/
        ├── phish1995-12-31d1.sbd.flac16
        └── phish1995-12-31d2.sbd.flac16
```

## Prerequisites

- Jellyfin Server 10.8.0 or higher
- .NET 8.0 runtime
- Phish.net API key (free registration required)

## Installation

### Plugin Catalog (Recommended)

1. Navigate to **Admin Dashboard > Plugins > Repositories**
2. Click **+** to add a new repository
3. **Repository Name**: `Phish.net Plugin Repository`
4. **Repository URL**: `https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishnet/master/manifest.json`
5. Click **Save** and navigate to **Catalog**
6. Find "Phish.net" in the plugin list and click **Install**
7. Select the latest version (1.1.0) and confirm installation
8. Restart Jellyfin Server

### Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/murphy52/jellyfin-plugin-phishnet/releases)
2. Extract the zip file
3. Copy the `.dll` files to your Jellyfin plugins directory under `plugins/phishnet/`
4. Restart Jellyfin Server
5. Configure the plugin with your Phish.net API key

## Configuration

1. Get a free API key from [Phish.net](https://phish.net/api/keys)
2. In Jellyfin, go to **Settings > Admin Dashboard > Plugins**
3. Find "Phish.net" and click **Settings**
4. Enter your API key and configure preferences:
   - **API Key**: Your Phish.net API key (required)
   - **Prefer Official Releases**: Prioritize official releases over audience recordings
   - **Include Jam Charts**: Fetch jam chart data for notable performances
   - **Include Reviews**: Pull community reviews and ratings
   - **Max Reviews**: Maximum number of reviews to display (default: 50)
   - **Cache Duration**: How long to cache API responses (default: 24 hours)
   - **Image Provider Settings**: Configure image sources and quality preferences
   - **Social Media API Keys**: Optional API keys for Instagram, Twitter, Flickr for enhanced image search
   - **Google Places API Key**: Optional key for venue image integration

## Usage

1. Add your Phish video files to a Jellyfin library
2. Set the library content type to "Music Videos" or "Movies"
3. Enable the "Phish.net" metadata provider for the library
4. **Important**: Also enable the "Phish.net" image provider and person provider for complete metadata
5. Run a library scan
6. The plugin will automatically match files with Phish shows and populate metadata

### What You'll Get

Once configured, your Phish videos will display:
- **Smart Titles**: "Phish - 1997-11-22 - Hampton Coliseum, Hampton, VA"
- **Complete Setlists**: Full song lists with set breaks and transitions in the overview
- **Community Ratings**: Average ratings from Phish.net reviews (1-10 scale)
- **Rich Metadata**: Venue details, show dates, production years, and comprehensive tagging
- **Band Member Info**: Individual profiles for Trey, Mike, Page, and Fish
- **Quality Images**: Venue photos, show-specific images from multiple sources

## Example Output

### Typical Show Metadata

**Title**: `Phish - 1997-11-22 - Hampton Coliseum, Hampton, VA`

**Overview**:
```
Setlist (29 songs):
Set 1: Makisupa Policeman > Llama, Divided Sky, Guelah Papyrus, Maze, Sparkle, Sample in a Jar, Limb By Limb, Bouncing Around the Room, Run Like an Antelope
Set 2: Ghost > Isabella, Piper > Black-Eyed Katy, Harry Hood > Cavern
Encore: Rocky Top

Phish concert performed on November 22, 1997 at Hampton Coliseum, Hampton, VA
```

**Community Rating**: `8.4/10` (based on Phish.net reviews)

**Tags**: `Phish, Concert, Live Music, Jam Band, Hampton, VA, Hampton Coliseum, 1997`

**Genres**: `Concert, Live Music`

### Multi-Night Run Example

**Title**: `N2 Phish - 1995-12-31 - Madison Square Garden, New York, NY`

For multi-night runs, the plugin automatically detects consecutive shows at the same venue and adds night indicators (N1, N2, N3, etc.)

## Development

This plugin is built with:
- **.NET 8.0**: Target framework for Jellyfin compatibility
- **Phish.net API v5**: Official API for accessing show data
- **Jellyfin Plugin SDK**: For seamless integration
- **Multiple Provider Architecture**: Movie, Person, and Image providers
- **Comprehensive Service Layer**: API client, parsing, and image services
- **Extensive Testing**: Unit tests, integration tests, and mocked API responses

### Architecture

- **Providers**: `PhishMovieProvider`, `PhishPersonProvider`, `PhishImageProvider`
- **Services**: `PhishNetApiClient`, `ExternalImageService`, `ShowPhotoService`
- **Parsers**: `FilenameParsing` utilities for intelligent show matching
- **Configuration**: Flexible plugin configuration with multiple API key support
- **Testing**: Comprehensive test suite with `MetadataTest` and unit test projects

### Building from Source

1. Clone this repository
2. Install .NET 8.0 SDK
3. Build the main project: `cd Jellyfin.Plugin.PhishNet && dotnet build`
4. Run tests: `cd ../MetadataTest && dotnet run` or `cd ../Jellyfin.Plugin.PhishNet.Tests && dotnet test`
5. Copy output to Jellyfin plugins directory
6. Restart Jellyfin

### Testing

- **MetadataTest**: Interactive testing environment for metadata providers
- **Unit Tests**: Comprehensive unit tests for parsing, API clients, and providers
- **Mocked Responses**: Full API response simulation for development without API keys

## Troubleshooting

### Common Issues

**Plugin Not Finding Shows**
- Ensure your API key is correctly configured in plugin settings
- Check that filenames contain recognizable date patterns (YYYY-MM-DD format works best)
- Verify the show exists in the Phish.net database
- Check Jellyfin logs for parsing details and API responses

**No Images Appearing**
- Enable the "Phish.net" image provider in your library settings
- Configure optional API keys (Google Places, social media) for enhanced image sources
- Some venues may not have images available from current sources

**Community Ratings Not Showing**
- Ratings only appear for shows that have reviews on Phish.net
- Check that "Include Reviews" is enabled in plugin configuration
- Some older shows may have fewer or no community reviews

**Performance Issues**
- Adjust cache duration settings to reduce API calls
- Disable image providers temporarily if experiencing slowdowns
- Consider reducing max review count for faster metadata loading

### Debug Logging

Enable debug logging in Jellyfin for detailed troubleshooting:
1. Go to **Settings > Admin Dashboard > Logs**
2. Set log level to "Debug" for the PhishNet plugin
3. Run a library scan and check logs for detailed information

## Version History

- **v1.1.0** (Current): Community ratings, comprehensive image provider, person provider, enhanced metadata
- **v1.0.0**: Basic metadata provider with Phish.net API integration

## Roadmap

### Planned Features
- **Series Grouping**: Group multi-night runs as series
- **Show Duration**: Estimate show duration from setlist data  
- **Enhanced Images**: AI-generated fallback images, community contributions
- **Advanced Filtering**: Filter by venue, year, song appearance
- **Jam Chart Integration**: Deep integration with Phish.net jam charts
- **LivePhish Integration**: Official LivePhish artwork and metadata

### Image Provider Enhancements
- Real-time social media photo integration
- Community image contribution system
- AI-generated venue/show artwork
- Enhanced image quality scoring

## Support

### Getting Help
- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/murphy52/jellyfin-plugin-phishnet/issues)
- **Discussions**: Join the conversation on [GitHub Discussions](https://github.com/murphy52/jellyfin-plugin-phishnet/discussions)
- **Documentation**: Check out the comprehensive guides in the repository

### Performance Tips
- Use descriptive filenames with dates in YYYY-MM-DD format
- Enable all three providers (movie, person, image) for complete metadata
- Configure optional API keys for enhanced image sources
- Adjust cache duration based on library size and update frequency

## Contributing

Contributions are welcome! Here's how you can help:

1. **Bug Reports**: Use GitHub Issues with detailed reproduction steps
2. **Feature Requests**: Discuss new ideas in GitHub Discussions first
3. **Code Contributions**: Fork the repository and submit pull requests
4. **Documentation**: Help improve guides, examples, and troubleshooting
5. **Testing**: Test with different file naming conventions and report issues

Before contributing code:
- Review the architecture documentation in `/docs`
- Run the test suite: `dotnet test`
- Follow existing code style and patterns
- Add tests for new functionality

## License

This project is licensed under the GPL-2.0 License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Phish.net](https://phish.net) for providing the comprehensive API
- The Jellyfin team for the excellent media server platform
- The Phish community for maintaining detailed show information

## Disclaimer

This plugin is not officially affiliated with Phish or Phish.net. It's a community-created tool for organizing and enriching Phish video collections.