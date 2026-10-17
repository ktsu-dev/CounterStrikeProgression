// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Core.Live;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The JSON settings the plugin and the site share for the live wire format.
/// </summary>
internal static class LiveJson
{
	/// <summary>Gets the serializer options: web defaults, with enums written as strings.</summary>
	public static JsonSerializerOptions Options { get; } = Create();

	private static JsonSerializerOptions Create()
	{
		JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
		options.Converters.Add(new JsonStringEnumConverter());
		return options;
	}
}
