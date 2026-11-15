using System.Text.RegularExpressions;

namespace EzCooking.Api.Recipes;

public static partial class ReelSource
{
    public static string? Canonicalize(string? url)
    {
        if (url is null || !ReelUrl().IsMatch(url))
        {
            return null;
        }

        var uri = new Uri(url);
        var shortcode = uri.AbsolutePath.TrimEnd('/').Split('/')[^1];
        return $"https://www.instagram.com/reel/{shortcode}/";
    }

    [GeneratedRegex(@"^https://(?:www\.)?instagram\.com/reel/[A-Za-z0-9_-]+/?(?:\?[A-Za-z0-9_=&%-]*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReelUrl();
}
