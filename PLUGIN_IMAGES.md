# Jellyfin Plugin Images - Complete Implementation Guide

## Overview
This document explains exactly how to implement plugin images in Jellyfin plugins based on the official Jellyfin source code analysis.

## How Jellyfin Loads Plugin Images

### The Correct Approach (What We Fixed)
Jellyfin loads plugin images through the **manifest file**, NOT through methods in Plugin.cs. Here's how it works:

1. **Manifest Configuration**: The plugin manifest file (`manifest.json`) must contain an `imagePath` property
2. **Local File**: The image file must be included in the plugin's installation directory
3. **API Endpoint**: Jellyfin serves images via `/Plugins/{pluginId}/{version}/Image` endpoint
4. **File Loading**: Jellyfin looks at `plugin.Manifest.ImagePath` and serves that file from the plugin directory

### Implementation Details

#### 1. Manifest File Structure
```json
{
  "guid": "8a4d0e85-6f3c-4e5a-9b2c-3d7f8e9a1b4c",
  "name": "Phish Data",
  "overview": "Plugin description...",
  "imagePath": "thumb.png",    // ✅ CORRECT: Points to local file
  // "imageUrl": "https://..."  // ❌ WRONG: External URLs don't work
}
```

#### 2. Plugin Directory Structure
```
plugin-directory/
├── Jellyfin.Plugin.PhishNet.dll
├── thumb.png                    // ✅ Image file in plugin root
└── other-plugin-files...
```

#### 3. Jellyfin Source Code Reference
From `Jellyfin.Api/Controllers/PluginsController.cs`:

```csharp
[HttpGet("{pluginId}/{version}/Image")]
public ActionResult GetPluginImage([FromRoute, Required] Guid pluginId, [FromRoute, Required] Version version)
{
    var plugin = _pluginManager.GetPlugin(pluginId, version);
    if (plugin is null)
    {
        return NotFound();
    }

    // KEY: Jellyfin uses the ImagePath from the manifest
    var imagePath = Path.Combine(plugin.Path, plugin.Manifest.ImagePath ?? string.Empty);
    if (plugin.Manifest.ImagePath is null || !System.IO.File.Exists(imagePath))
    {
        return NotFound();
    }

    return PhysicalFile(imagePath, MimeTypes.GetMimeType(imagePath));
}
```

## Where Plugin Images Appear

Plugin images are displayed in **3 locations** in Jellyfin:

1. **Plugin Catalog** (`/web/#/dashboard/plugins/catalog`)
   - Shows when browsing available plugins
   
2. **Plugin Details/Update Screen** (`/web/#/dashboard/plugins/?name=PluginName`)  
   - Shows when clicking on a plugin from the catalog
   
3. **My Plugins Screen** (`/web/#/dashboard/plugins`)
   - Shows installed plugins in Admin Dashboard

## What Doesn't Work (Common Mistakes)

### ❌ Plugin.cs Image Methods
These methods in Plugin.cs **DO NOT WORK**:
```csharp
// These methods are NOT used by Jellyfin
public Stream GetPluginImage() { ... }
public Stream GetThumbImage() { ... }  
public Stream GetLogoImage() { ... }
```

### ❌ Embedded Resources Only
Just embedding images in the DLL **IS NOT ENOUGH**:
```xml
<!-- This alone doesn't work -->
<EmbeddedResource Include="thumb.png" />
```

### ❌ External Image URLs
Using `imageUrl` in manifest **DOES NOT WORK**:
```json
{
  "imageUrl": "https://example.com/image.png"  // ❌ Jellyfin ignores this
}
```

## The Fix We Applied

### 1. Removed Non-Working Code
- Deleted all image methods from Plugin.cs
- Removed embedded resource references from .csproj
- Removed unused using statements

### 2. Updated Manifest Files
- Changed from `imageUrl` to `imagePath` 
- Set `imagePath: "thumb.png"`

### 3. Included Image File
- Copied `images/plugin-icon.png` to root as `thumb.png`
- Ensured image is included in distribution packages

### 4. Verified Implementation
The image must be included in the plugin release ZIP file structure:
```
jellyfin-plugin-phishdata-v1.4.3.zip
├── Jellyfin.Plugin.PhishNet.dll
├── thumb.png                     // ✅ Critical: Image at root level
└── meta.json                     // Plugin metadata
```

## Testing Plugin Images

To verify plugin images work correctly:

1. **Install Plugin**: Install via catalog or manual installation
2. **Check Locations**: Verify image appears in all 3 UI locations
3. **API Test**: Test direct API endpoint: `GET /Plugins/{pluginId}/{version}/Image`
4. **File Verification**: Confirm image file exists in plugin installation directory

## Image Requirements

- **Format**: PNG, JPG, JPEG supported
- **Naming**: Use `thumb.png` for consistency  
- **Size**: Recommended 256x256px or similar square aspect ratio
- **Location**: Must be in plugin root directory (same level as DLL)
- **Manifest**: Must specify correct `imagePath` in manifest.json

## Conclusion

Plugin images in Jellyfin work through the **manifest file system**, not through Plugin.cs methods. The key is:
1. Set `imagePath` in manifest.json
2. Include image file in plugin directory
3. Ensure image is in release packages

This approach ensures plugin images display correctly across all Jellyfin UI locations.