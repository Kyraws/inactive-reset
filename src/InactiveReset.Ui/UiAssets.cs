namespace InactiveReset.Ui;

internal sealed class UiAssets(string? sourceDirectory)
{
    private readonly string? _sourceDirectory = ValidateDirectory(sourceDirectory);

    public bool LiveReload => _sourceDirectory is not null;

    public static Stream Open(string name) =>
        typeof(UiAssets).Assembly.GetManifestResourceStream("InactiveReset.Ui." + name)
            ?? throw new InvalidOperationException("The application asset is missing from this build.");

    public string Read(string name)
    {
        if (_sourceDirectory is not null && IsFrontendSource(name))
        {
            return File.ReadAllText(Path.Combine(_sourceDirectory, name));
        }
        using var stream = Open(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string ReadPage()
    {
        if (!LiveReload) return Read("Lab.html");

        var revision = Revision();
        var reload = $$"""
            <script>
            (() => {
              const revision = '{{revision}}';
              const pageKey = 'inactive-reset-dev-page';
              const previousPage = sessionStorage.getItem(pageKey);
              sessionStorage.removeItem(pageKey);
              if (previousPage) {
                const root = document.documentElement;
                const observer = new MutationObserver(() => {
                  if (root.hasAttribute('data-loading')) return;
                  observer.disconnect();
                  if (document.body.dataset.page === previousPage) return;
                  const page = [...document.querySelectorAll('.workspace-nav [data-page]')]
                    .find(button => button.dataset.page === previousPage);
                  page?.click();
                });
                observer.observe(root, { attributeFilter: ['data-loading'] });
              }
              async function check() {
                try {
                  const response = await fetch('/__dev/ui-version', { cache: 'no-store' });
                  if (response.ok && await response.text() !== revision) {
                    if (document.body.dataset.page) sessionStorage.setItem(pageKey, document.body.dataset.page);
                    location.reload();
                    return;
                  }
                } catch { /* Retry if a save or server restart interrupts the request. */ }
                setTimeout(check, 750);
              }
              setTimeout(check, 750);
            })();
            </script>
            """;
        var html = Read("Lab.html");
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd < 0 ? html + reload : html.Insert(bodyEnd, reload);
    }

    public string Revision()
    {
        var version = new HashCode();
        foreach (var path in Directory.EnumerateFiles(_sourceDirectory!).Where(IsFrontendSource).Order(StringComparer.Ordinal))
        {
            version.Add(path, StringComparer.Ordinal);
            version.Add(File.GetLastWriteTimeUtc(path));
        }
        return version.ToHashCode().ToString("x8");
    }

    private static bool IsFrontendSource(string path) => Path.GetExtension(path) is ".html" or ".css" or ".js";

    private static string? ValidateDirectory(string? path)
    {
        if (path is null) return null;
        var directory = Path.GetFullPath(path);
        if (!File.Exists(Path.Combine(directory, "Lab.html")))
            throw new ArgumentException("Live reload needs the frontend source directory containing Lab.html.", nameof(path));
        return directory;
    }
}
