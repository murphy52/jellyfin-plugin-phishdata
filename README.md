# Phish Data Plugin for Jellyfin

A comprehensive metadata provider for Phish concert videos using the Phish.net API.

## Features

- **Smart Filename Parsing**: Automatically identifies Phish shows from various filename formats
- **Complete Setlists**: Displays full song lists with set breaks and transitions
- **Rich Metadata**: Show dates, venues, locations, and production years
- **Community Ratings**: Aggregated ratings from Phish.net user reviews
- **Band Member Profiles**: Individual profiles for Trey, Mike, Page, and Fish
- **Plugin Images**: Works correctly in all Jellyfin UI locations
- **Clean Configuration**: Simple setup with just your Phish.net API key

## Installation

### Via Plugin Catalog (Recommended)

1. **Add Repository**
   - Navigate to **Dashboard > Catalog > then click the ⚙️ icon**
   - Click **+** to add a new repository
   - **Repository Name**: `Phish Data Plugin Repository`
   - **Repository URL**: `https://raw.githubusercontent.com/murphy52/jellyfin-plugin-phishdata/master/manifest.json`
   - Click **Save**

2. **Install Plugin**
   - Go to **Dashboard > Plugins **
   - Find "Phish Data" and click **Install**
   - **Restart Jellyfin Server**

3. **Get API Key**
   - Visit [Phish.net API Keys](https://phish.net/api/keys) for a free API key
   - Complete registration and note your API key

4. **Configure Plugin**
   - Go to **Dashboard > Plugins > My Plugins**
   - Find "Phish Data" and click **Settings**
   - Enter your Phish.net API key
   - Click **Save**

5. **Configure Library**
   - Go to **Dashboard > Libraries/Libraries > For each library that will contain Phish videos open the menu and click Manage Library**
   - Under **Metadata downloaders**, enable the Phish Data providers:
   - ✅ **Phish Data** (movie metadata)
   - Click **OK** and **Scan Library**

## File Naming

The plugin works with various Phish show filename formats:

```
Phish Videos/
├── 1997-11-22 - Hampton Coliseum/
│   └── phish1997-11-22.mkv
├── ph1995-12-31d1.mkv
└── Phish - 1999-07-04 - Oswego, NY.mp4
```

## What You'll Get

- **Smart Titles**: "Phish - 1997-11-22 - Hampton Coliseum, Hampton, VA"
- **Complete Setlists**: Full song lists with SET 1, SET 2, ENCORE formatting
- **Community Ratings**: Ratings based on Phish.net user reviews
- **Rich Tags**: Venue, location, year, and Phish-specific tags
- **Band Member Info**: Individual profiles accessible via People section

## Supported Jellyfin Versions

- Jellyfin 10.8.0 and newer
- Compatible with all major platforms (Windows, Linux, macOS, Docker)

## Support

For issues, questions, or feature requests, please visit the [GitHub repository](https://github.com/murphy52/jellyfin-plugin-phishdata).

## License

This plugin is licensed under the GPL-3.0 License.
