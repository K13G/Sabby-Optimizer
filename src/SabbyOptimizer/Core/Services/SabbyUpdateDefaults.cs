namespace PCTweaker.Core.Services;

public static class SabbyUpdateDefaults
{
    public const string OfficialStableFeedUrl =
        "https://raw.githubusercontent.com/mrcoem/mrcoem/main/update/SabbyOptimizer-Stable-manifest.json";

    public static string GetBuiltInStableFeedUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "UpdateFeed.txt");
            if (File.Exists(path))
            {
                foreach (var line in File.ReadLines(path))
                {
                    var value = line.Trim();
                    if (value.Length == 0 || value.StartsWith('#')) continue;
                    if (!value.Contains("YOUR-UPDATE-MANIFEST-URL", StringComparison.OrdinalIgnoreCase))
                        return value;
                }
            }
        }
        catch { }

        return OfficialStableFeedUrl;
    }

    public static bool IsLegacyLocalFeed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            return false;

        return uri.IsLoopback ||
               uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeStableFeed(string? value) =>
        IsLegacyLocalFeed(value) ? GetBuiltInStableFeedUrl() : value!.Trim();
}
