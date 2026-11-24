namespace InactiveReset.Ui;

internal static class UiAssets
{
    public static Stream Open(string name) =>
        typeof(UiAssets).Assembly.GetManifestResourceStream("InactiveReset.Ui." + name)
            ?? throw new InvalidOperationException("The application asset is missing from this build.");

    public static string Read(string name)
    {
        using var stream = Open(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
