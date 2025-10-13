# 🎸 Jellyfin Phish.net Plugin Installation Guide

## 📦 What This Plugin Does

✅ **Automatically detects** Phish concert videos from filenames  
✅ **Rich metadata** including setlists, venue info, and multi-night run support  
✅ **Smart titles** with proper N1/N2/N3 formatting for multi-night runs  
✅ **Complete band member info** for all four members with biographies  
✅ **Proper genres** ("Concert", "Live Music") and comprehensive tagging  

## 🛠️ Installation

### Method 1: Manual Installation (Recommended)

1. **Download the plugin:**
   ```bash
   # Copy the plugin DLL to your Jellyfin plugins directory
   cp Jellyfin.Plugin.PhishNet.dll /path/to/jellyfin/plugins/
   ```

2. **Find your Jellyfin plugins directory:**
   - **Linux:** `/var/lib/jellyfin/plugins/`
   - **Windows:** `%PROGRAMDATA%\Jellyfin\Server\plugins\`
   - **Docker:** `/config/plugins/` (mounted volume)
   - **macOS:** `/Users/[username]/.local/share/jellyfin/plugins/`

3. **Restart Jellyfin server**

4. **Configure the plugin:**
   - Go to **Dashboard** → **Plugins** → **Phish.net**
   - Enter your Phish.net API key (get one from https://phish.net/api/register)
   - Enable setlist descriptions and other options as desired

### Method 2: Plugin Repository (Future)
*This plugin will be submitted to the official Jellyfin plugin repository.*

## 🔧 Configuration

### Required: Phish.net API Key
1. Visit https://phish.net/api/register
2. Request an API key (free)
3. Enter the key in **Dashboard** → **Plugins** → **Phish.net**

### Plugin Settings:
- ✅ **Include setlist in description** - Shows full setlist with transitions
- ✅ **Include reviews** - Adds show reviews to metadata  
- 🔢 **Max reviews** - Limit number of reviews (default: 3)

## 📁 Supported File Formats

The plugin recognizes these filename patterns:

### Standard Formats:
- `ph2024-08-30.mkv` → **"Phish 8-30-2024"**
- `Phish.2024-04-18.Las.Vegas.NV.mkv` → **"Phish Las Vegas 4-18-2024"**
- `phish2023-12-31.madison.square.garden.mkv` → **"Phish New York City 12-31-2023"**

### Multi-Night Runs (Auto-detected):
- `ph2024-08-30.dicks.mkv` → **"N1 Phish Commerce City 8-30-2024"**
- `ph2024-08-31.dicks.mkv` → **"N2 Phish Commerce City 8-31-2024"**

### Special Events:
- `Phish - 8-16-2024 - Mondegreen Secret Set.mp4` → **"Phish 8-16-2024 (Secret Set)"**

### Supported Extensions:
`.mkv`, `.mp4`, `.avi`, `.flac`, `.mp3`, `.m4v`

## 📊 Generated Metadata

### Movie Fields:
- **Title:** Smart formatting with N1/N2/N3 for runs
- **Release Date:** Concert date  
- **Year:** Concert year
- **Genres:** "Concert", "Live Music"
- **Overview:** Setlist + show details
- **Tags:** "Phish", location, venue, event type

### People (Band Members):
- **Trey Anastasio** - Guitar, Vocals, Composer
- **Mike Gordon** - Bass, Vocals  
- **Jon Fishman** - Drums, Percussion
- **Page McConnell** - Keyboards, Piano, Vocals

*Each member includes biography and birth date.*

## 🎯 Example Results

### Basic Show:
```
Title: Phish 8-6-2024
Date: 2024-08-06
Genres: Concert, Live Music
Tags: Phish, Concert, Live Music
Overview: Phish concert performed on August 6, 2024
```

### Multi-Night Run with API:
```
Title: N1 Phish Commerce City 8-30-2024
Overview: Setlist (23 songs):
Set I: Wilson > Simple, Harry Hood
Set II: Ghost > Twenty Years Later...

Phish concert performed on August 30, 2024 at Dick's Sporting Goods Park, Commerce City, CO
```

## 🔍 Troubleshooting

### Plugin Not Appearing:
1. Verify DLL is in correct plugins directory
2. Check Jellyfin logs for errors
3. Ensure .NET 8.0 runtime is available
4. Restart Jellyfin completely

### No Metadata Generated:
1. Check filename matches supported patterns
2. Verify API key is configured correctly
3. Check plugin logs for parsing confidence
4. Ensure file is in a Movie library (not Music)

### Low Confidence Parsing:
Files with confidence < 30% are ignored. Common causes:
- Missing date in filename
- Non-Phish content
- Unusual filename format

## 📝 Logs & Debugging

Enable debug logging:
```json
"Jellyfin.Plugin.PhishNet": "Debug"
```

Look for these log messages:
- `Processing movie info for: [filename]`
- `Parsed [filename] with confidence [X]`
- `API client not available, using basic metadata only`

## 🆘 Support

- **GitHub Issues:** [Create an issue](https://github.com/your-repo/jellyfin-plugin-phishnet/issues)
- **Jellyfin Community:** [Plugin Support Forum](https://forum.jellyfin.org/)
- **Phish.net API:** [API Documentation](https://phish.net/api/)

## 🎸 Happy Phishing!

Your Phish collection will now have rich, accurate metadata with proper setlists, venue information, and multi-night run detection. Enjoy browsing your shows with all the details a true fan deserves!