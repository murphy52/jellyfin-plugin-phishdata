If you find this extension useful, consider supporting its development:

[![Buy me a coffee](https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&emoji=&slug=murphy52&button_colour=FFDD00&font_colour=000000&font_family=Cookie&outline_colour=000000&coffee_colour=ffffff)](https://www.buymeacoffee.com/murphy52)

Your support helps maintain and improve the extension with new features and site integrations!

# Phish Data Plugin for Jellyfin

A comprehensive metadata provider for Phish concert videos using the Phish.net API.

**Requires Jellyfin 12.0 or newer** (plugin v1.6.0.0+). Running Jellyfin 10.8–10.10? Install plugin v1.5.x instead.

![Phish Data Jellyfin Plugin](https://github.com/murphy52/jellyfin-plugin-phishdata/blob/master/thumb.png)

## Features

- **Smart Filename Parsing**: Automatically identifies Phish shows from various filename formats
- **Complete Setlists**: Displays full song lists with set breaks and transitions
- **Rich Metadata**: Show dates, venues, locations, and production years
- **Phish.net Links**: Every show links to its Phish.net setlist page under External Links
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

## Mixed Libraries

The plugin only acts on videos that look Phish-related: the title, filename, or any folder in the path contains "phish", or the filename uses the `ph` + date convention (for example `ph1997-11-22.mkv`). Anything else is left untouched for your other metadata providers, so Phish shows can share a library with other concerts or movies.

## Chapter Markers (optional)

The plugin can add a chapter for every song, so you can skip straight to the Tweezer. It finds each song by matching the video's own audio against the opening of that track on [phish.in](https://phish.in). It's off by default.

- **Turn it on:** Dashboard > Plugins > Phish Data > **Add chapter markers to Phish shows**, then Save.
- **When it runs:** soon after a new or changed show is scanned (once the file has stopped changing for 10 minutes), and every day at 4:00 in the **Phish: add chapter markers** scheduled task, which also picks up the backlog. To start right away, use **Run now** on that task.
- **What it costs:** each show reads its video file once and downloads about 90 seconds of every track from phish.in, roughly 40–70 MB. The settings page caps how many shows and how many GB one run handles, so a big library is spread over several nights.
- **Where chapters go:** Jellyfin's database only. Video files are never changed. If a "Replace all metadata" refresh wipes the chapters, the next run puts them back.
- **What it leaves alone:** chapters that are already in the file, or that you added yourself. Jellyfin's generated "Chapter 1, Chapter 2…" markers are replaced unless you turn that off.
- **Thumbnails:** if the library has chapter image extraction turned on (Manage Library > **Extract chapter images**, off by default), the images appear after Jellyfin's next **Extract Chapter Images** run, nightly by default. That run also removes the images of any "Chapter N" markers that were replaced.

Shows that phish.in doesn't have yet (usually the last few days of a tour) are retried automatically for up to 60 days. Shows the plugin can't place with confidence are not guessed. They're listed under **Needs your attention** on the settings page with **Retry**, **Apply anyway** and **Ignore** buttons.

To set a show's chapters by hand, put a text file next to the video, named like it with `.chapters.txt` in place of the extension (`ph2026-07-07.chapters.txt` next to `ph2026-07-07.mkv`), with one `H:MM:SS Title` per line:

```
0:00:58 Chalk Dust Torture
0:11:03 Ghost
1:32:40 Tweezer
```

It replaces alignment for that show, and editing it later updates the chapters.

## Supported Jellyfin Versions

| Plugin version | Jellyfin version | .NET |
|----------------|------------------|------|
| 1.6.x          | 12.0 and newer   | 10   |
| 1.5.x          | 10.8 – 10.10     | 8    |

Compatible with all major platforms (Windows, Linux, macOS, Docker).

### Upgrading from 1.5.x to Jellyfin 12

Jellyfin 12 moved to .NET 10 and changed its plugin API, so 1.5.x builds do not load on it. After upgrading Jellyfin, install v1.6.0.0 from the catalog (it appears once the server reports version 12) and delete any old `phishnet_*` or `PhishData_1.5.*` folder from your `plugins` directory before restarting.

## Support this project

If you find this plugin useful, consider supporting its development:

[![Buy me a coffee](https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&emoji=&slug=murphy52&button_colour=FFDD00&font_colour=000000&font_family=Cookie&outline_colour=000000&coffee_colour=ffffff)](https://www.buymeacoffee.com/murphy52)

Your support helps maintain and improve the plugin with new features and enhancements!

## About Phish.net

This plugin is not affiliated with Phish.net, but we highly encourage supporting their incredible work! Phish.net is run by The Mockingbird Foundation, a non-profit organization that has awarded hundreds of grants to music education programs. Consider supporting them at:

**[Support Phish.net & The Mockingbird Foundation](https://donate.mbird.org/forms/mbird-donations-via-the-phishnet-frontpage)**

## About phish.in

The optional chapter markers (off by default) are aligned against track audio from [phish.in](https://phish.in), the Phish audio archive. Thanks to phish.in for the audio. This plugin is not affiliated with phish.in.

## Support

For issues, questions, or feature requests, please visit the [GitHub repository](https://github.com/murphy52/jellyfin-plugin-phishdata).

## License

This plugin is licensed under the GPL-3.0 License.
