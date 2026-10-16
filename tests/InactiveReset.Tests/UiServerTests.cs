using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using InactiveReset.Ui;
using Xunit;

namespace InactiveReset.Tests;

public sealed class UiServerTests
{
    [Fact]
    public async Task RootServesCurrentUiAndItsAssetsWithoutClassicPages()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        var server = new Server("unused", Path.Combine(Path.GetTempPath(), "ir-ui-" + Guid.NewGuid()), number);
        using var cancellation = new CancellationTokenSource();
        var serving = server.RunAsync(cancellation.Token);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(server.Url), Timeout = TimeSpan.FromSeconds(5) };
            var html = await client.GetStringAsync("");
            Assert.Contains("id=\"practicePanel\"", html);
            Assert.DoesNotContain("compareButton", html);
            Assert.DoesNotContain("classicFrame", html);
            Assert.Equal(html, await client.GetStringAsync("lab"));
            foreach (var asset in Regex.Matches(html, "(?:src|href)=\"(/[^\"]+)\"").Select(m => m.Groups[1].Value).Distinct())
                Assert.False(string.IsNullOrWhiteSpace(await client.GetStringAsync(asset)));
            using var classic = await client.GetAsync("session");
            Assert.False(classic.IsSuccessStatusCode);
            Assert.DoesNotContain("InactiveReset.Ui.Session.html", typeof(Server).Assembly.GetManifestResourceNames());
        }
        finally
        {
            cancellation.Cancel();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
