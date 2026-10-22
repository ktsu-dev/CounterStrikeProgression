// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Site.Data;

using ktsu.CounterStrikeProgression.Core.Heatmaps;
using SkiaSharp;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

/// <summary>
/// Finds the radar image drawn under a map's heatmap. A PNG put in <c>&lt;DataPath&gt;/radar</c> by hand wins.
/// Otherwise, when <see cref="SiteOptions.GamePath"/> names the game's <c>csgo</c> folder, the radar is read
/// out of the server's own <c>pak01_dir.vpk</c> and kept under <c>&lt;DataPath&gt;/radar/game</c>, so nothing
/// of Valve's is shipped with the site. A game update rewrites the pak, so the kept copy is read again after one.
/// </summary>
internal sealed class RadarImages
{
	/// <summary>The pak file in the game's <c>csgo</c> folder that holds the radar textures.</summary>
	public const string PakName = "pak01_dir.vpk";

	/// <summary>The ways the game names a map's radar texture, tried in order.</summary>
	private static readonly string[] TextureNames = ["{0}_radar_psd", "{0}_radar_tga", "{0}_radar_png", "{0}_radar"];

	private readonly string folder;
	private readonly string extracted;
	private readonly string? pak;
	private readonly Lock gate = new();
	private readonly Dictionary<string, DateTime> misses = new(StringComparer.Ordinal);

	/// <summary>
	/// Initializes a new instance of the <see cref="RadarImages"/> class.
	/// </summary>
	/// <param name="options">The site settings.</param>
	public RadarImages(SiteOptions options)
	{
		Ensure.NotNull(options);
		folder = Path.Combine(Path.GetFullPath(options.DataPath), "radar");
		extracted = Path.Combine(folder, "game");
		pak = string.IsNullOrWhiteSpace(options.GamePath) ? null : Path.Combine(Path.GetFullPath(options.GamePath), PakName);
	}

	/// <summary>
	/// Gets the path of a map's radar image, reading it out of the game files first if it isn't kept yet.
	/// </summary>
	/// <param name="map">The map, already checked to be a plain map name.</param>
	/// <returns>The PNG's path, or <see langword="null"/> when there's none for the map.</returns>
	public string? Find(string map)
	{
		string byHand = Path.Combine(folder, map + ".png");
		if (File.Exists(byHand))
		{
			return byHand;
		}

		string kept = Path.Combine(extracted, map + ".png");
		if ((pak is null) || !File.Exists(pak))
		{
			return File.Exists(kept) ? kept : null;
		}

		DateTime pakTime = File.GetLastWriteTimeUtc(pak);
		if (File.Exists(kept) && (File.GetLastWriteTimeUtc(kept) >= pakTime))
		{
			return kept;
		}

		lock (gate)
		{
			if (File.Exists(kept) && (File.GetLastWriteTimeUtc(kept) >= pakTime))
			{
				return kept;
			}

			// A map the pak has no radar for is only looked up again once the game updates.
			if (misses.TryGetValue(map, out DateTime missed) && (missed == pakTime))
			{
				return File.Exists(kept) ? kept : null;
			}

			byte[]? png = TryExtract(pak, map);
			if (png is null)
			{
				misses[map] = pakTime;
				return File.Exists(kept) ? kept : null;
			}

			Directory.CreateDirectory(extracted);
			string temporary = kept + ".tmp";
			File.WriteAllBytes(temporary, png);
			File.Move(temporary, kept, overwrite: true);
			_ = misses.Remove(map);
			return kept;
		}
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pak or texture the decoder can't read means no radar, never a failed page.")]
	private static byte[]? TryExtract(string pakPath, string map)
	{
		try
		{
			return Extract(pakPath, map);
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// Reads a map's radar texture out of a pak and encodes it as a PNG the size of <see cref="MapOverview.Size"/>,
	/// the size the overview file's scale is measured against.
	/// </summary>
	/// <param name="pakPath">The pak's path.</param>
	/// <param name="map">The map.</param>
	/// <returns>The PNG, or <see langword="null"/> when the pak has no radar for the map.</returns>
	public static byte[]? Extract(string pakPath, string map)
	{
		using Package package = new();
		package.Read(pakPath);
		foreach (string name in TextureNames)
		{
			PackageEntry? entry = package.FindEntry($"panorama/images/overheadmaps/{string.Format(System.Globalization.CultureInfo.InvariantCulture, name, map)}.vtex_c");
			if (entry is not null)
			{
				package.ReadEntry(entry, out byte[] data);
				return Encode(data);
			}
		}

		return null;
	}

	/// <summary>
	/// Decodes a compiled texture and encodes it as a square PNG the size of <see cref="MapOverview.Size"/>.
	/// </summary>
	/// <param name="texture">The <c>.vtex_c</c> file's bytes.</param>
	/// <returns>The PNG, or <see langword="null"/> when the file isn't a texture.</returns>
	public static byte[]? Encode(byte[] texture)
	{
		using MemoryStream stream = new(texture);
		using Resource resource = new();
		resource.Read(stream);
		if (resource.DataBlock is not Texture decoded)
		{
			return null;
		}

		using SKBitmap bitmap = decoded.GenerateBitmap();
		using SKBitmap sized = (bitmap.Width == MapOverview.Size) && (bitmap.Height == MapOverview.Size)
			? bitmap.Copy()
			: bitmap.Resize(new SKImageInfo(MapOverview.Size, MapOverview.Size, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell));
		using SKData png = sized.Encode(SKEncodedImageFormat.Png, 100);
		return png.ToArray();
	}
}
