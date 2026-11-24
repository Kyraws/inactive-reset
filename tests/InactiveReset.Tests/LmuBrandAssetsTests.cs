using System.IO.Compression;
using System.Net;
using InactiveReset.TestSupport;
using InactiveReset.Ui;
using Xunit;

namespace InactiveReset.Tests;

public sealed class LmuBrandAssetsTests
{
    [Fact]
    public async Task MissingArtworkCompletesTheHttpResponse()
    {
        await using var host = new UiTestHost();
        using var response = await host.Client.GetAsync("api/artwork?kind=track&key=invalid.scene");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ResolvesExactLiveryAndFallsBackToTheMatchingCircuitVenue()
    {
        var path = Path.Combine(Path.GetTempPath(), "inactive-reset-artwork-" + Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                void Add(string name, byte value)
                {
                    using var stream = zip.CreateEntry("start/images/" + name).Open();
                    stream.WriteByte(value);
                }
                Add("cars/FrontThumbnail/Car_01_frontAngle.webp", 1);
                Add("tracks/backgrounds/BahrainELMS.webp", 2);
                Add("tracks/backgrounds/BahrainWEC.webp", 3);
                Add("tracks/cards/LeMansWEC.webp", 4);
                using var large = zip.CreateEntry("start/images/tracks/backgrounds/LeMansWEC.webp").Open();
                large.Write(new byte[5 * 1024 * 1024]);
            }
            var assets = new LmuBrandAssets(() => path);
            Assert.Equal(new byte[] { 1 }, assets.ReadImage("car", "car_01"));
            Assert.Equal(new byte[] { 3 }, assets.ReadImage("track", "BahrainWEC_Outer"));
            Assert.Equal(5 * 1024 * 1024, assets.ReadImage("track", "LeMansWEC_Alternate")?.Length);
            Assert.Equal(new byte[] { 4 }, assets.ReadImage("track-card", "LeMansWEC_Alternate"));
            Assert.Null(assets.ReadImage("car", "car_02"));
            Assert.Null(assets.ReadImage("track", "../BahrainWEC"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("\\")]
    public void ReadsExactBrandAndThemeVariantWithoutExtractingFiles(string separator)
    {
        var path = Path.Combine(Path.GetTempPath(), "inactive-reset-brands-" + Guid.NewGuid() + ".zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var name in new[] { "BMW", "BMW Dark", "ADESS" })
                {
                    using var writer = new StreamWriter(zip.CreateEntry(("start/images/manufacturer/Brand=" + name + ".svg").Replace("/", separator)).Open());
                    writer.Write("<svg>" + name + "</svg>");
                }
            var assets = new LmuBrandAssets(() => path);
            Assert.Equal("<svg>BMW</svg>", assets.Read("BMW", false));
            Assert.Equal("<svg>BMW Dark</svg>", assets.Read("BMW", true));
            Assert.Equal("<svg>ADESS</svg>", assets.Read("ADESS", true));
            Assert.Null(assets.Read("../BMW", false));
            Assert.Null(assets.Read("Unknown", false));
        }
        finally { File.Delete(path); }
    }
}
