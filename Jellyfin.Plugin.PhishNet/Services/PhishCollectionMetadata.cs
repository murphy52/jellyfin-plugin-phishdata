using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.PhishNet.Parsers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PhishNet.Services
{
    /// <summary>
    /// The multi-night-run facts a movie carries so the library handler can build collections
    /// after the item has been saved. Persisted as custom provider IDs on the movie.
    /// </summary>
    public sealed class PhishCollectionMetadata
    {
        /// <summary>Provider ID key for the run's city.</summary>
        public const string CityKey = "PhishCollectionCity";

        /// <summary>Provider ID key for the run's year.</summary>
        public const string YearKey = "PhishCollectionYear";

        /// <summary>Provider ID key for this show's night number within the run.</summary>
        public const string DayNumberKey = "PhishCollectionDayNumber";

        /// <summary>Provider ID key for this show's date.</summary>
        public const string DateKey = "PhishCollectionDate";

        /// <summary>Provider ID key for every date in the run, comma separated.</summary>
        public const string RunDatesKey = "PhishCollectionRunDates";

        private const string DateFormat = "yyyy-MM-dd";

        private PhishCollectionMetadata(string city, int year, int dayNumber, DateTime showDate, List<DateTime> runDates)
        {
            City = city;
            Year = year;
            DayNumber = dayNumber;
            ShowDate = showDate;
            RunDates = runDates;
        }

        /// <summary>Gets the city used to name the collection.</summary>
        public string City { get; }

        /// <summary>Gets the year used to name the collection.</summary>
        public int Year { get; }

        /// <summary>Gets this show's night number within the run (1-based).</summary>
        public int DayNumber { get; }

        /// <summary>Gets this show's date.</summary>
        public DateTime ShowDate { get; }

        /// <summary>Gets every show date in the run.</summary>
        public IReadOnlyList<DateTime> RunDates { get; }

        /// <summary>
        /// Builds collection metadata for a show, preferring run information detected from the Phish.net API
        /// and falling back to a night number parsed from the filename or title.
        /// </summary>
        /// <param name="parse">The filename/title parse result.</param>
        /// <param name="apiNightNumber">Night number from API run detection, or null if the API found no run.</param>
        /// <param name="apiRunDates">All dates of the run from API run detection, or null.</param>
        /// <param name="apiCity">City reported by the API, used when the filename had none.</param>
        /// <returns>The metadata, or null when nothing indicates this show is part of a run.</returns>
        public static PhishCollectionMetadata? FromShow(
            PhishShowParseResult parse,
            int? apiNightNumber,
            IReadOnlyList<DateTime>? apiRunDates,
            string? apiCity)
        {
            if (!parse.ShowDate.HasValue)
            {
                return null;
            }

            var showDate = parse.ShowDate.Value.Date;
            var dayNumber = apiNightNumber is > 0 ? apiNightNumber.Value : parse.DayNumber ?? 0;
            if (dayNumber <= 0)
            {
                return null;
            }

            var runDates = apiRunDates is { Count: > 1 }
                ? apiRunDates.Select(d => d.Date).OrderBy(d => d).ToList()
                : FallbackRunDates(showDate, dayNumber);

            var city = string.IsNullOrWhiteSpace(parse.City)
                ? (string.IsNullOrWhiteSpace(apiCity) ? "Unknown" : apiCity!)
                : parse.City!;

            return new PhishCollectionMetadata(city, showDate.Year, dayNumber, showDate, runDates);
        }

        /// <summary>
        /// Reads collection metadata previously stored with <see cref="ApplyTo"/>.
        /// </summary>
        /// <param name="providerIds">The item's provider IDs.</param>
        /// <returns>The metadata, or null when the item carries none.</returns>
        public static PhishCollectionMetadata? FromProviderIds(IReadOnlyDictionary<string, string>? providerIds)
        {
            if (providerIds == null
                || !providerIds.TryGetValue(CityKey, out var city) || string.IsNullOrWhiteSpace(city)
                || !providerIds.TryGetValue(YearKey, out var yearText) || !int.TryParse(yearText, out var year)
                || !providerIds.TryGetValue(DayNumberKey, out var dayText) || !int.TryParse(dayText, out var dayNumber)
                || !providerIds.TryGetValue(DateKey, out var dateText) || !TryParseDate(dateText, out var showDate))
            {
                return null;
            }

            List<DateTime> runDates;
            if (providerIds.TryGetValue(RunDatesKey, out var runDatesText) && !string.IsNullOrWhiteSpace(runDatesText))
            {
                runDates = runDatesText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => TryParseDate(t, out var d) ? d : (DateTime?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .OrderBy(d => d)
                    .ToList();
            }
            else
            {
                runDates = FallbackRunDates(showDate, dayNumber);
            }

            return new PhishCollectionMetadata(city, year, dayNumber, showDate, runDates);
        }

        /// <summary>
        /// Stores this metadata on an item as provider IDs.
        /// </summary>
        /// <param name="item">The item to tag.</param>
        public void ApplyTo(IHasProviderIds item)
        {
            item.SetProviderId(CityKey, City);
            item.SetProviderId(YearKey, Year.ToString(CultureInfo.InvariantCulture));
            item.SetProviderId(DayNumberKey, DayNumber.ToString(CultureInfo.InvariantCulture));
            item.SetProviderId(DateKey, ShowDate.ToString(DateFormat, CultureInfo.InvariantCulture));
            item.SetProviderId(RunDatesKey, string.Join(",", RunDates.Select(d => d.ToString(DateFormat, CultureInfo.InvariantCulture))));
        }

        /// <summary>
        /// Without API run dates, assume the run started (dayNumber - 1) days before this show
        /// and runs through this show. Two nights minimum so night 1 still looks for night 2.
        /// </summary>
        private static List<DateTime> FallbackRunDates(DateTime showDate, int dayNumber)
        {
            var start = showDate.AddDays(-(dayNumber - 1));
            var nights = Math.Max(dayNumber, 2);
            return Enumerable.Range(0, nights).Select(i => start.AddDays(i)).ToList();
        }

        private static bool TryParseDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
    }
}
