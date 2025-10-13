# Jellyfin Movie Metadata Fields - Implementation Status

## ✅ **IMPLEMENTED FIELDS**

### **Core Movie Information**
- ✅ **Name** - Smart title with N1/N2/N3 format: `"N1 Phish Denver 8-30-2024"`
- ✅ **Overview** - Setlist-first description with transition marks
- ✅ **PremiereDate** - Show date from API/filename parsing  
- ✅ **ProductionYear** - Year extracted from show date
- ✅ **DateCreated (Release Date)** - Set to concert date for proper release dating

### **Identification & Linking**
- ✅ **ProviderIds** - Links back to Phish.net:
  - `PhishNet: "2024-08-30"`
  - `PhishNetVenue: "123"` (venue ID)

### **Categorization & Discovery**
- ✅ **Genres** - Hard-coded proper genres: `["Concert", "Live Music"]`
- ✅ **Tags** - Comprehensive tagging system:
  - Base tags: `["Phish", "Concert", "Live Music", "Jam Band"]` (includes "Phish")
  - Location tags: `["Denver", "CO", "Dick's Sporting Goods Park"]`
  - Event tags: `["Special Event", "Secret Set", "NPR Tiny Desk Concert"]`

### **Cast & Crew (Via Separate PersonProvider)**
- ✅ **PersonProvider** - Dedicated provider for Phish band members:
  - `Trey Anastasio` - Guitar, Vocals, Composer (born 1964-09-30)
  - `Mike Gordon` - Bass, Vocals (born 1965-06-03)
  - `Jon Fishman` - Drums, Percussion (born 1965-02-19)
  - `Page McConnell` - Keyboards, Piano, Vocals (born 1963-05-17)
  - Each member includes biography, birth date, and role information

### **Image Support Structure**
- ✅ **GetSupportedImages()** - Declares support for:
  - `Primary` (poster/cover art)
  - `Backdrop` (background images)
  - `Thumb` (thumbnail images)
- ✅ **GetImageInfos()** - Framework ready (currently returns empty)

## ❌ **MISSING / POTENTIAL FIELDS**

### **Cast & Crew**
- ✅ **People** - Band members implemented via PersonProvider (see above)
- ❌ **Director** - Could use venue/production info
- ❌ **Producer** - Could use recording source info

### **Content Rating & Classification**  
- ❌ **OfficialRating** - Could be "Not Rated" or "G" for concerts
- ✅ **CommunityRating** - ✅ **IMPLEMENTED**: Uses Phish.net review ratings (1-10 scale)
- ❌ **CriticRating** - Could use Phish.net review scores (separate from community)
- ✅ **Genres** - Hard-coded: `["Concert", "Live Music"]`

### **Audio/Video Technical Info**
- ❌ **RunTimeTicks** - Show duration from setlist/venue data
- ❌ **AspectRatio** - Could extract from filename (1080p, 4K, etc.)
- ❌ **Video3DFormat** - Not typically applicable

### **Series/Collection Organization**  
- ❌ **SeriesName** - Could group by tour/year: "Phish 2024", "Baker's Dozen"
- ❌ **SeasonNumber** - Could use year: 2024, 2023, etc.
- ❌ **EpisodeNumber** - Could use show number within year
- ❌ **ParentIndexNumber** - For multi-set organization

### **Additional Metadata**
- ❌ **Tagline** - Could use show highlights: "The Donut Show", "Epic Hood"
- ❌ **Studios** - Could use recording source: "Phish.net", "LivePhish"
- ❌ **TrailerUrls** - Could link to Phish.net clips
- ❌ **HomePageUrl** - Link to Phish.net show page
- ❌ **Budget/Revenue** - Not applicable to concerts

### **Alternate Titles & Versions**
- ❌ **OriginalTitle** - Could be original taper filename
- ❌ **AlternateVersions** - Different sources (SBD, AUD, etc.)

## 🎯 **PRIORITY ADDITIONS WE SHOULD IMPLEMENT**

### **High Priority** 
1. ✅ **CommunityRating** - ~~Use Phish.net show ratings~~ **COMPLETED** (1-10 scale from review aggregation)
2. ✅ **Genres** - ~~Move from Tags to proper Genres~~ **COMPLETED**
3. ✅ **People** - ~~Add band members as actors/performers~~ **COMPLETED via PersonProvider**
4. **RunTimeTicks** - Calculate show duration from setlist data

### **Medium Priority**  
5. **SeriesName/Season** - Group by year: "Phish 2024", "Phish 2023"
6. **Tagline** - Add show highlights from reviews/notes
7. **HomePageUrl** - Direct link to Phish.net show page

### **Low Priority**
8. **Studios** - Recording source information
9. **OfficialRating** - Standard "Not Rated" for concerts

## 📊 **CURRENT IMPLEMENTATION EXAMPLES**

### **Complete Metadata Result:**
```json
{
  "Name": "N2 Phish Denver 8-31-2024",
  "Overview": "Setlist (23 songs):\nSet I: Wilson > Simple...\n\nPhish concert performed on August 31, 2024 at Dick's Sporting Goods Park, Denver, CO",
  "PremiereDate": "2024-08-31T00:00:00Z",
  "ProductionYear": 2024,
  "DateCreated": "2024-08-31T00:00:00Z",
  "Genres": ["Concert", "Live Music"],
  "Tags": ["Phish", "Concert", "Live Music", "Jam Band", "Denver", "CO", "Dick's Sporting Goods Park"],
  "ProviderIds": {
    "PhishNet": "2024-08-31",
    "PhishNetVenue": "123"
  },
  "People": "Handled by separate PhishPersonProvider"
}
```

### **What's Still Missing:**
```json
{
  // CommunityRating: 4.2 - ✅ NOW IMPLEMENTED
  "RunTimeTicks": 108000000000, // ~3 hours
  "SeriesName": "Phish 2024",
  "SeasonNumber": 2024,
  "Tagline": "The legendary Dick's run continues",
  "HomePageUrl": "https://phish.net/setlists/2024-08-31"
}
```

## 🎸 **CURRENT STATUS**

✅ **COMPLETED ALL REQUESTED FIELDS:**
- ✅ **Release Date** (DateCreated) - Set to concert date
- ✅ **Year** (ProductionYear) - Extracted from show date  
- ✅ **Genres** - Hard-coded: `["Concert", "Live Music"]`
- ✅ **People** - All 4 band members via PersonProvider with biographies
- ✅ **"Phish" Tag** - Added to Tags array
- ✅ **CommunityRating** - ✅ **COMPLETED**: Aggregate ratings from Phish.net reviews (1-10 scale)

🎯 **NEXT LOGICAL ADDITIONS:**
1. **RunTimeTicks** for show duration
2. **SeriesName/Season** for year-based grouping
3. **HomePageUrl** linking to Phish.net
4. **Tagline** with show highlights

The plugin now provides **comprehensive metadata** that enhances the Jellyfin experience for Phish fans!
