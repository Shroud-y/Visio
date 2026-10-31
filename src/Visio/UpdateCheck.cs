using System.Net.Http;
using System.Text.Json;

namespace Visio;

/// <summary>Asks GitHub for the latest stable release. Nothing is downloaded or installed.</summary>
internal static class UpdateCheck
{
    public const string RepoUrl = "https://github.com/Shroud-y/Visio";
    private const string LatestApi = "https://api.github.com/repos/Shroud-y/Visio/releases/latest";

    public static readonly Version Current = typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0);

    public sealed record Release(Version Version, string Tag, string Url);

    /// <summary>The latest release (prereleases excluded by GitHub), or throws on network/parse errors.</summary>
    public static async Task<Release> GetLatestAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Visio/{Current.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi));
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
        if (!TryParseTag(tag, out var version)) throw new FormatException($"Unexpected release tag '{tag}'");
        // Only ever send the user to this repository's releases.
        if (!url.StartsWith(RepoUrl + "/releases/", StringComparison.Ordinal)) url = RepoUrl + "/releases/latest";
        return new Release(version, tag, url);
    }

    /// <summary>"v1.2.3", "1.2.3", "v1.2.3-beta" → 1.2.3</summary>
    public static bool TryParseTag(string tag, out Version version)
    {
        var s = tag.TrimStart('v', 'V');
        var cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0) s = s[..cut];
        if (Version.TryParse(s, out var v))
        {
            version = new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            return true;
        }
        version = new Version(0, 0);
        return false;
    }

    public static bool IsNewer(Version v) => v > new Version(Current.Major, Current.Minor, Math.Max(Current.Build, 0));
}
