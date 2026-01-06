public static class ScenePlanJsonCleaner
{
    public static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        raw = raw.Trim();

        // Strip ``` fences
        if (raw.StartsWith("```"))
        {
            int a = raw.IndexOf('{');
            int b = raw.LastIndexOf('}');
            if (a >= 0 && b > a)
                raw = raw.Substring(a, b - a + 1);
        }

        // Fallback: slice first JSON block
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
            raw = raw.Substring(start, end - start + 1);

        return raw.Trim();
    }
}
