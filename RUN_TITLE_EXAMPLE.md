# Multi-Night Run Title Format

## Title Format: [night] [band] [city] [date]

### Example: Dick's Sporting Goods Park 3-Night Run

**Single Show (not part of run):**
- `Phish Commerce City 8-30-2024`

**Multi-Night Run:**
- `N1 Phish Commerce City 8-30-2024` (Night 1 of 3)
- `N2 Phish Commerce City 8-31-2024` (Night 2 of 3)  
- `N3 Phish Commerce City 9-1-2024`  (Night 3 of 3)

### Example: Madison Square Garden 4-Night NYE Run

**New Year's Eve Run:**
- `N1 Phish New York City 12-28-2024`
- `N2 Phish New York City 12-29-2024`
- `N3 Phish New York City 12-30-2024`
- `N4 Phish New York City 12-31-2024`

### Example: Festival Shows

**Single Festival Show:**
- `Phish Dover 8-16-2024 (Secret Set)`

**Multi-Night Festival:**
- `N1 Phish Watkins Glen 8-13-2025`
- `N2 Phish Watkins Glen 8-14-2025`
- `N3 Phish Watkins Glen 8-15-2025`

## Run Detection Algorithm

### How it Works:
1. **Date Range Query**: Search API for shows ±7 days around current show
2. **Venue Filtering**: Find shows at same venue (using VenueId)
3. **Consecutive Detection**: Identify groups of consecutive nights
4. **Night Assignment**: Determine which night (1, 2, 3, etc.) current show is

### Example Run Detection:

**API Returns Shows at Dick's:**
- 2024-08-30 (Dick's - VenueId: 123)
- 2024-08-31 (Dick's - VenueId: 123)  
- 2024-09-01 (Dick's - VenueId: 123)
- 2024-09-05 (Dick's - VenueId: 123) [separate run]

**Consecutive Groups Found:**
- Run 1: [2024-08-30, 2024-08-31, 2024-09-01] ← 3-night run
- Run 2: [2024-09-05] ← Single show

**Title Results:**
- `N1 Phish Commerce City 8-30-2024`
- `N2 Phish Commerce City 8-31-2024`  
- `N3 Phish Commerce City 9-1-2024`
- `Phish Commerce City 9-5-2024` (no night indicator for single show)

## Benefits for Phish Fans

- ✅ **Instant Recognition** - Fans immediately know which night of a run
- ✅ **Proper Organization** - Multi-night runs group together logically
- ✅ **Familiar Format** - Uses the N1/N2/N3 format fans already know
- ✅ **Clean Sorting** - Jellyfin will sort N1 → N2 → N3 automatically
- ✅ **Run Context** - Easy to see the full story of a venue run
- ✅ **Accurate Titles** - Matches how fans actually refer to shows

## Real-World Examples

### Famous Runs (Based on Working Plugin Results):
- **Baker's Dozen MSG 2017**: `N1 Phish New York City 7-21-2017` through `N13 Phish New York City 8-6-2017`
- **Big Cypress 1999**: `Phish Big Cypress 12-31-1999` (single millennium show)
- **Dick's 2023**: `N1 Phish Commerce City 8-31-2023` through `N4 Phish Commerce City 9-3-2023`
- **Sphere 2024**: `N1 Phish Las Vegas 4-18-2024` through `N4 Phish Las Vegas 4-21-2024`
- **Hampton 1997**: `N2 Phish Hampton 11-22-1997` (actual working example from testing)

This format makes it instantly clear which show is which in your Jellyfin library! 🎸