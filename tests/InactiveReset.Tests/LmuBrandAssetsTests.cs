using System.IO.Compression;
using InactiveReset.Ui;
using Xunit;

namespace InactiveReset.Tests;

public sealed class LmuBrandAssetsTests
{
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
