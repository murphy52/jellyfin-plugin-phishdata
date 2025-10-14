# Jellyfin Phish Data Metadata Plugin

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
- **User Feedback**: Helpful error messages with specific guidance when filename parsing fails

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

#### Step 1: Add Plugin Repository
1. Navigate to **Admin Dashboard > Plugins > Repositories**
2. Click **+** to add a new repository
3. **Repository Name**: `Phish.net Plugin Repository`
4. **Repository URL**: `https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishdata/master/manifest.json`
5. Click **Save**

#### Step 2: Install Plugin
1. Navigate to **Admin Dashboard > Plugins > Catalog**
2. Find "Phish.net" in the plugin list and click **Install**
3. Select the latest version and confirm installation
4. **Restart Jellyfin Server** - This is required for the plugin to load

#### Step 3: Get API Key
1. Visit [Phish.net API Keys](https://phish.net/api/keys) to register for a free API key
2. Complete the registration form and note your API key

#### Step 4: Configure Plugin
1. After restarting, go to **Admin Dashboard > Plugins > My Plugins**
2. Find "Phish.net" and click **Settings** (gear icon)
3. Enter your Phish.net API key in the **API Key** field
4. Configure other preferences as desired:
   - **Prefer Official Releases**: Prioritize official releases over audience recordings
   - **Include Jam Charts**: Fetch jam chart data for notable performances
   - **Cache Duration**: How long to cache API responses (default: 24 hours)
   - **Image Provider Settings**: Configure image sources and quality preferences
   - **Social Media API Keys**: Optional API keys for Instagram, Twitter, Flickr for enhanced image search
   - **Google Places API Key**: Optional key for venue image integration
5. Click **Save**

#### Step 5: Configure Library Settings
1. Navigate to your Phish video library (or create a new one)
2. Click the **three dots** menu and select **Manage Library**
3. Go to the **Metadata** tab
4. **Content Type**: Set to "Movies" or "Music Videos"
5. **Metadata downloaders**: Check **all three** Phish.net providers:
   - ✅ **Phish.net** (movie metadata)
   - ✅ **Phish.net** (image provider)
   - ✅ **Phish.net** (person provider)
6. **Image fetchers**: Ensure **Phish.net** is checked and prioritized
7. Click **OK** to save library settings

#### Step 6: Scan Library
1. Click **Scan Library** to refresh metadata
2. Monitor the scan progress in **Admin Dashboard > Activity**
3. Your Phish videos will now have rich metadata, setlists, ratings, and images!

### Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/murphy52/jellyfin-plugin-phishdata/releases)
2. Extract the zip file
3. Copy the `.dll` files to your Jellyfin plugins directory under `plugins/phishnet/`
4. Restart Jellyfin Server
5. Follow **Steps 3-6** above for configuration

## Library Setup & Usage

### Recommended Library Configuration

**Library Type**: Movies or Music Videos
- **Movies**: Best for complete show recordings
- **Music Videos**: Good for individual song or set recordings

**Folder Structure**: The plugin works with various naming conventions:
```
Phish Videos/
├── 1997-11-22 - Hampton Coliseum/
│   └── phish1997-11-22.mkv
├── ph1995-12-31d1.mkv
└── Phish - 1999-07-04 - Oswego, NY.mp4
```

### Essential Provider Settings

For complete metadata experience, **enable all three providers**:

1. **Phish.net (Movie)**: Core metadata, setlists, ratings
2. **Phish.net (Image)**: Show photos, venue images, artwork
3. **Phish.net (Person)**: Band member profiles and biographies

### Post-Installation Verification

After scanning, verify your shows display:
- ✅ **Smart Titles**: "N2 Phish Hampton 11-22-1997"
- ✅ **Complete Setlists**: Full song lists in overview
- ✅ **Rich Images**: Venue photos and show-specific artwork
- ✅ **Band Members**: Individual profiles for Trey, Mike, Page, Fish
- ✅ **Comprehensive Tags**: Venue, location, year-specific tags

### What You'll Get

Once configured, your Phish videos will display:
- **Smart Titles**: "Phish - 1997-11-22 - Hampton Coliseum, Hampton, VA"
- **Complete Setlists**: Full song lists with set breaks and transitions in the overview
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

**Plugin Repository Not Loading**
- Double-check the repository URL is exactly: `https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishdata/master/manifest.json`
- Try refreshing the Catalog page after adding the repository
- Ensure your Jellyfin server has internet access
- Check Admin Dashboard > Logs for repository loading errors

**Plugin Not Appearing in Catalog**
- Wait a few minutes after adding the repository, then refresh the Catalog page
- Clear browser cache and reload the Jellyfin web interface
- Verify the repository was added successfully in Admin Dashboard > Plugins > Repositories

**No Configuration Page After Installation**
- Ensure you've restarted Jellyfin Server after installing the plugin
- Look for "Phish.net" in Admin Dashboard > Plugins > My Plugins
- If missing, check that the plugin files are in the correct directory
- Check Jellyfin logs for plugin loading errors

**Phish.net Provider Not Available in Library Settings**
- Confirm the plugin is installed and Jellyfin has been restarted
- Verify your API key is configured in the plugin settings
- Check that all three providers are visible: movie metadata, image provider, and person provider
- If providers are missing, reinstall the plugin or check logs for registration errors

**Plugin Not Finding Shows**
- Ensure your API key is correctly configured in plugin settings
- Check that filenames contain recognizable date patterns (YYYY-MM-DD format works best)
- Verify the show exists in the Phish.net database
- Check Jellyfin logs for parsing details and API responses
- Try rescanning individual files to see detailed error messages

**"Metadata Parsing Failed" Messages**
- If you see a video titled "⚠️ Metadata Parsing Failed" with guidance in the overview, the filename couldn't be matched to a show
- Follow the detailed instructions in the overview to rename your file with a proper date format
- After renaming, right-click the video and select "Refresh Metadata" → "Replace all metadata"
- The plugin will automatically retry parsing with the new filename and fetch complete show data

**No Images Appearing**
- Enable the "Phish.net" image provider in your library settings under **Metadata** tab
- Ensure "Phish.net" is checked in the **Image fetchers** section
- Configure optional API keys (Google Places, social media) for enhanced image sources
- Some venues may not have images available from current sources
- Check if images appear after a fresh library scan


**Performance Issues**
- Adjust cache duration settings to reduce API calls
- Disable image providers temporarily if experiencing slowdowns
- Consider reducing max review count for faster metadata loading
- Monitor API rate limits if scanning large libraries

### Debug Logging

Enable debug logging in Jellyfin for detailed troubleshooting:
1. Go to **Settings > Admin Dashboard > Logs**
2. Set log level to "Debug" for the PhishNet plugin
3. Run a library scan and check logs for detailed information

## Quick Setup Summary

For experienced users, here's the essential setup checklist:

1. ➕ **Add Repository**: `https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishdata/master/manifest.json`
2. 📦 **Install Plugin** from Catalog → **Restart Jellyfin**
3. 🔑 **Get API Key** from [Phish.net](https://phish.net/api/keys)
4. ⚙️ **Configure Plugin** in Admin Dashboard > Plugins > My Plugins
5. 📚 **Enable All Providers** in Library Settings > Metadata tab:
   - ✅ Phish.net (movie metadata)
   - ✅ Phish.net (image provider)  
   - ✅ Phish.net (person provider)
6. 🔍 **Scan Library** and enjoy rich Phish metadata!

## Version History

- **v1.3.0** (Current): User feedback for parsing failures, comprehensive error guidance, enhanced UX
- **v1.1.5**: Enhanced plugin icons, JSON deserialization fixes, full metadata functionality
- **v1.1.4**: Embedded plugin icons, provider registration improvements
- **v1.1.3**: JSON parsing fixes for review data
- **v1.1.2**: Configuration page loading fixes
- **v1.1.1**: Manifest updates and release improvements
- **v1.1.0**: Community ratings, comprehensive image provider, person provider, enhanced metadata
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
- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/murphy52/jellyfin-plugin-phishdata/issues)
- **Discussions**: Join the conversation on [GitHub Discussions](https://github.com/murphy52/jellyfin-plugin-phishdata/discussions)
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
