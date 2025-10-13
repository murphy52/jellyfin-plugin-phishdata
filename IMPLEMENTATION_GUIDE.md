# 🛠️ **Step-by-Step Image Implementation Guide**

## **Phase 1: Replace Placeholder URLs (30 minutes)**

### **Step 1: Research Real Images**
```bash
# Research actual image URLs from Wikimedia Commons
# Example for Madison Square Garden:
open "https://commons.wikimedia.org/wiki/Category:Madison_Square_Garden"

# Copy direct image URLs like:
# https://upload.wikimedia.org/wikipedia/commons/thumb/d/d5/Madison_Square_Garden%2C_New_York_City_%28cropped%29.jpg/1200px-Madison_Square_Garden%2C_New_York_City_%28cropped%29.jpg
```

### **Step 2: Update Known Venues Dictionary**
Edit `PhishImageProvider.cs` line ~200:

```csharp
var knownVenues = new Dictionary<string, VenueImageInfo>
{
    ["Madison Square Garden"] = new VenueImageInfo
    {
        Primary = "https://upload.wikimedia.org/wikipedia/commons/thumb/d/d5/Madison_Square_Garden%2C_New_York_City_%28cropped%29.jpg/1200px-Madison_Square_Garden%2C_New_York_City_%28cropped%29.jpg",
        Backdrop = "https://upload.wikimedia.org/wikipedia/commons/thumb/f/f1/Madison_Square_Garden_interior.jpg/1920px-Madison_Square_Garden_interior.jpg",
        Description = "Madison Square Garden - New York, NY"
    },
    ["Red Rocks Amphitheatre"] = new VenueImageInfo
    {
        Primary = "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a1/Red_Rocks_Amphitheatre_Colorado.jpg/1200px-Red_Rocks_Amphitheatre_Colorado.jpg",
        Backdrop = "https://upload.wikimedia.org/wikipedia/commons/thumb/b/b1/Red_Rocks_stage_view.jpg/1920px-Red_Rocks_stage_view.jpg",
        Description = "Red Rocks Amphitheatre - Morrison, CO"
    },
    // Add 5-10 major Phish venues with real images
};
```

### **Step 3: Test Results**
```bash
# Rebuild plugin
cd /tmp/jellyfin-plugin-phishnet/Jellyfin.Plugin.PhishNet && dotnet build

# Deploy to Jellyfin
# cp bin/Debug/net8.0/Jellyfin.Plugin.PhishNet.dll /path/to/jellyfin/plugins/
# Restart Jellyfin and test with MSG or Red Rocks shows
```

## **Phase 2: Enable Wikipedia API Integration (15 minutes)**

### **Already Implemented!** 
The `ExternalImageService` is ready to use. Wikipedia API requires no API key and works immediately.

### **Test Wikipedia Integration:**
1. Create a show with a venue name like "Madison Square Garden"
2. The plugin will automatically search Wikipedia for images
3. Images will appear in Jellyfin's "Images" section for that show

### **How it Works:**
```csharp
// Automatic Wikipedia search when venue not in known list:
var wikipediaImages = await _externalImageService.GetWikipediaImagesAsync(venueName, cancellationToken);
```

## **Phase 3: Add External API Keys (Optional)**

### **Option A: Unsplash API (Free)**
1. Go to https://unsplash.com/developers
2. Create account and get API key
3. Add to Jellyfin plugin configuration:
   ```
   Dashboard → Plugins → Phish.net → Settings
   Unsplash API Key: [your-key-here]
   ```

### **Option B: Google Places API (Paid)**
1. Go to https://console.cloud.google.com
2. Enable Places API
3. Get API key
4. Add to plugin configuration:
   ```
   Google Places API Key: [your-key-here]
   ```

## **🎯 Results You'll Get:**

### **With Just Phase 1 (Static Images):**
- **Major venues** like MSG, Red Rocks will have beautiful images
- **Instant improvement** for your most popular show locations
- **No API keys required**

### **With Phase 2 (Wikipedia):**
- **Automatic venue lookup** for any venue with a Wikipedia page
- **Free forever** - no API limits
- **High-quality venue photos** from Wikipedia Commons

### **With Phase 3 (External APIs):**
- **Professional photos** from Unsplash
- **Real venue photos** from Google Street View
- **Dynamic image sourcing** for any venue worldwide

## **🚀 Practical Example:**

Let's implement Red Rocks Amphitheatre images:

```bash
# 1. Find real image URL
curl -s "https://commons.wikimedia.org/w/api.php?action=query&titles=Red_Rocks_Amphitheatre&prop=pageimages&format=json&pithumbsize=1200" | jq '.query.pages[].thumbnail.source'

# 2. Test the URL works
open "https://upload.wikimedia.org/wikipedia/commons/thumb/a/a1/Red_Rocks_Amphitheatre_Colorado.jpg/1200px-Red_Rocks_Amphitheatre_Colorado.jpg"

# 3. Update code with real URL
# 4. Build and test
```

## **📊 Expected Timeline:**

- **Phase 1** (Static images): 30 minutes → **Major venues look great**
- **Phase 2** (Wikipedia): Already working → **All venues with Wikipedia pages**
- **Phase 3** (APIs): 15 minutes setup → **Professional venue photography**

**Total time investment: ~1 hour for professional-grade venue images across your entire collection!**