# Jellyfin Phish.net Metadata Plugin

## About

This plugin adds a metadata provider for Phish concert videos using the [Phish.net API v5](https://docs.phish.net/). It automatically fetches show information, setlists, venue details, and concert metadata for your Phish video collection in Jellyfin.

## Features

- **Show Metadata**: Automatically fetch show date, venue, location, and attendance information
- **Setlist Integration**: Display complete setlists with song titles, set breaks, and encore information
- **Venue Information**: Get venue names, locations, and capacity details
- **Jam Chart Data**: Access notable jams and performance ratings from the community
- **Review Integration**: Pull user reviews and show ratings from Phish.net
- **Smart Matching**: Intelligent file name parsing to match video files with shows
- **Flexible Naming**: Support for various file naming conventions commonly used by Phish collectors

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

### Automatic (Recommended - Coming Soon)

1. Navigate to **Settings > Admin Dashboard > Plugins > Repositories**
2. Add a new repository with URL: `https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishnet/main/manifest.json`
3. Save and navigate to **Catalogue**
4. Find "Phish.net" in the plugin list and install the latest version
5. Restart Jellyfin Server

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
   - **Max Reviews**: Maximum number of reviews to display (default: 5)
   - **Cache Duration**: How long to cache API responses (default: 24 hours)

## Usage

1. Add your Phish video files to a Jellyfin library
2. Set the library content type to "Music Videos" or "Movies"
3. Enable the "Phish.net" metadata provider for the library
4. Run a library scan
5. The plugin will automatically match files with Phish shows and populate metadata

## Development

This plugin is built with:
- **.NET 8.0**: Target framework for Jellyfin compatibility
- **Phish.net API v5**: Official API for accessing show data
- **Jellyfin Plugin SDK**: For seamless integration

### Building from Source

1. Clone this repository
2. Install .NET 8.0 SDK
3. Build the project: `dotnet build`
4. Copy output to Jellyfin plugins directory
5. Restart Jellyfin

## Contributing

Contributions are welcome! Please read our contributing guidelines and submit pull requests for any improvements.

## License

This project is licensed under the GPL-2.0 License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Phish.net](https://phish.net) for providing the comprehensive API
- The Jellyfin team for the excellent media server platform
- The Phish community for maintaining detailed show information

## Disclaimer

This plugin is not officially affiliated with Phish or Phish.net. It's a community-created tool for organizing and enriching Phish video collections.