namespace InactiveReset.Ui;

internal static class LabPage
{
    // Reuse the proven practice actions without changing the preserved classic page.
    public static string PracticeScript()
    {
        var start = Page.Html.IndexOf("<script>", StringComparison.Ordinal) + 8;
        var end = Page.Html.IndexOf("</script>", start, StringComparison.Ordinal);
        return Page.Html[start..end];
    }
}
