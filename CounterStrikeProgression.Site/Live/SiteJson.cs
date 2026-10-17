// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Live;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ktsu.CounterStrikeProgression.Core.Live;

/// <summary>
/// The JSON settings for everything the site sends to browsers: the live wire format's settings, with
/// 64-bit numbers such as Steam IDs written as strings, because JavaScript numbers lose precision above 2^53.
/// </summary>
internal static class SiteJson
{
	/// <summary>Gets the serializer options.</summary>
	public static JsonSerializerOptions Options { get; } = Create();

	/// <summary>
	/// Copies the site's settings onto other options, such as the ones minimal APIs use.
	/// </summary>
	/// <param name="options">The options to change.</param>
	public static void Apply(JsonSerializerOptions options)
	{
		Ensure.NotNull(options);
		options.PropertyNamingPolicy = LiveJson.Options.PropertyNamingPolicy;
		options.PropertyNameCaseInsensitive = LiveJson.Options.PropertyNameCaseInsensitive;
		options.NumberHandling = LiveJson.Options.NumberHandling;
		options.Converters.Add(new JsonStringEnumConverter());
		options.Converters.Add(new UInt64AsStringConverter());
	}

	private static JsonSerializerOptions Create()
	{
		JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
		Apply(options);
		return options;
	}

	/// <summary>Writes a <see cref="ulong"/> as a decimal string, and reads it from a string or a number.</summary>
	private sealed class UInt64AsStringConverter : JsonConverter<ulong>
	{
		public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			reader.TokenType == JsonTokenType.String
				? ulong.Parse(reader.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture)
				: reader.GetUInt64();

		public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
		{
			Ensure.NotNull(writer);
			writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
		}
	}
}
