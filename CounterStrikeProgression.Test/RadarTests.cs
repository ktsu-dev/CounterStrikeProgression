// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using SkiaSharp;
using ValvePak;

[TestClass]
public sealed class RadarTests
{
	private string directory = string.Empty;

	public TestContext TestContext { get; set; } = null!;

	private string Radar => Path.Combine(directory, "data", "radar");

	private string Game => Path.Combine(directory, "game");

	[TestInitialize]
	public void Setup()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-radar-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Radar);
		Directory.CreateDirectory(Game);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private RadarImages Images(bool withGame = true) => new(new SiteOptions { DataPath = Path.Combine(directory, "data"), GamePath = withGame ? Game : string.Empty });

	private string WritePak(params (string Name, byte[] Data)[] files)
	{
		using Package package = new();
		foreach ((string name, byte[] data) in files)
		{
			package.AddFile(name, data);
		}

		string path = Path.Combine(Game, RadarImages.PakName);
		package.Write(path);
		return path;
	}

	[TestMethod]
	public void ImagePutInByHandWins()
	{
		File.WriteAllBytes(Path.Combine(Radar, "de_dust2.png"), [1]);
		_ = WritePak(("panorama/images/overheadmaps/other_radar_psd.vtex_c", [1, 2]));
		Assert.AreEqual(Path.Combine(Radar, "de_dust2.png"), Images().Find("de_dust2"));
		Assert.AreEqual(Path.Combine(Radar, "de_dust2.png"), Images(withGame: false).Find("de_dust2"));
	}

	[TestMethod]
	public void NothingWithoutAnImageOrTheGame()
	{
		Assert.IsNull(Images(withGame: false).Find("de_dust2"));
		Assert.IsNull(Images().Find("de_dust2"));
	}

	[TestMethod]
	public void MapWithNoRadarInThePakIsMissing()
	{
		_ = WritePak(("panorama/images/overheadmaps/de_mirage_radar_psd.vtex_c", [1, 2]));
		RadarImages images = Images();
		Assert.IsNull(images.Find("de_dust2"));
		Assert.IsNull(images.Find("de_dust2"));
		Assert.IsNull(RadarImages.Extract(Path.Combine(Game, RadarImages.PakName), "de_dust2"));
	}

	[TestMethod]
	public void TextureThatCantBeDecodedIsMissing()
	{
		_ = WritePak(("panorama/images/overheadmaps/de_dust2_radar_psd.vtex_c", [1, 2, 3, 4]));
		Assert.IsNull(Images().Find("de_dust2"));
	}

	[TestMethod]
	public void KeptCopyIsUsedUntilTheGameUpdates()
	{
		string pak = WritePak(("panorama/images/overheadmaps/de_mirage_radar_psd.vtex_c", [1, 2]));
		File.SetLastWriteTimeUtc(pak, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
		string kept = Path.Combine(Radar, "game", "de_dust2.png");
		Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
		File.WriteAllBytes(kept, [9]);

		Assert.AreEqual(kept, Images().Find("de_dust2"));

		// After an update that dropped the map, the old copy is still better than nothing.
		File.SetLastWriteTimeUtc(pak, DateTime.UtcNow.AddMinutes(1));
		Assert.AreEqual(kept, Images().Find("de_dust2"));
		Assert.AreEqual(kept, Images(withGame: false).Find("de_dust2"));
	}

	[TestMethod]
	public void ReadsTheRadarOutOfThePak()
	{
		// Valve's textures aren't committed, so this runs only when CSP_TEST_VTEX names a .vtex_c texture to pack.
		string? texture = Environment.GetEnvironmentVariable("CSP_TEST_VTEX");
		if (string.IsNullOrEmpty(texture))
		{
			Assert.Inconclusive("Set CSP_TEST_VTEX to a .vtex_c texture to run.");
			return;
		}

		_ = WritePak(("panorama/images/overheadmaps/de_dust2_radar_psd.vtex_c", File.ReadAllBytes(texture)));
		string? path = Images().Find("de_dust2");
		Assert.AreEqual(Path.Combine(Radar, "game", "de_dust2.png"), path);
		using SKBitmap bitmap = SKBitmap.Decode(path);
		Assert.AreEqual(MapOverview.Size, bitmap.Width);
		Assert.AreEqual(MapOverview.Size, bitmap.Height);
	}
}
