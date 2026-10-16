namespace InactiveReset.Ui;

internal static class UiAssets
{
    public static string Read(string name)
    {
        using var stream = typeof(UiAssets).Assembly.GetManifestResourceStream("InactiveReset.Ui." + name)
            ?? throw new InvalidOperationException("The application page is missing from this build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
