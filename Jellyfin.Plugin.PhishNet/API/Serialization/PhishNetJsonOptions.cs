using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.PhishNet.API.Serialization;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for Phish.net API responses.
/// The v5 API is loosely typed: fields such as <c>showyear</c> arrive as numbers in some
/// endpoints and strings in others, so reading must tolerate both directions.
/// </summary>
public static class PhishNetJsonOptions
{
    /// <summary>
    /// Gets the options used for all Phish.net API deserialization.
    /// </summary>
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new LenientStringConverter() }
    };
}

/// <summary>
/// Reads JSON numbers, booleans and nulls into <see cref="string"/> properties instead of throwing.
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var l))
                {
                    return l.ToString(CultureInfo.InvariantCulture);
                }

                return reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Cannot convert token type {reader.TokenType} to string.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
