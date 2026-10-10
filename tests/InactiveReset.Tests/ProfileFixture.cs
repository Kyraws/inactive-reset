using System.Text.Json.Nodes;

namespace InactiveReset.Tests;

internal static class ProfileFixture
{
    internal static string PathFor(string build) => Path.Combine(AppContext.BaseDirectory, "offsets", build + ".json");
    internal static JsonObject Read(string build = "0F6DCAC1") =>
        JsonNode.Parse(File.ReadAllText(PathFor(build)))!.AsObject();
}
