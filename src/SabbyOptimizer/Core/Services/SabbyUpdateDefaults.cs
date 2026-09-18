namespace PCTweaker.Core.Services;

public static class SabbyUpdateDefaults
{
    public static string GetBuiltInStableFeedUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "UpdateFeed.txt");
            if (!File.Exists(path)) return string.Empty;
            foreach (var line in File.ReadLines(path))
            {
                var value = line.Trim();
                if (value.Length == 0 || value.StartsWith('#')) continue;
                if (value.Contains("YOUR-UPDATE-MANIFEST-URL", StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
                return value;
            }
        }
        catch { }
        return string.Empty;
    }
}
