using System.Net.Http.Json;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Shibori;

internal static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/VarYUvrc/Shibori/releases/latest";
    public const string IssuesUrl = "https://github.com/VarYUvrc/Shibori/issues";
    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentVersionLabel => Format(CurrentVersion);

    public static async Task<ReleaseInfo?> FindAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Shibori/{CurrentVersionLabel}");
        var release = await client.GetFromJsonAsync<GitHubRelease>(ReleasesUrl);
        if (release is null || release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName)) return null;
        var tag = release.TagName.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var version) || version <= CurrentVersion) return null;
        var asset = release.Assets?.FirstOrDefault(item => item.Name.Equals("Shibori-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        return asset is null ? null : new ReleaseInfo(release.TagName, Format(version), release.HtmlUrl, asset.BrowserDownloadUrl);
    }

    public static string Format(Version version) => $"{version.Major:0000}.{version.Minor:00}.{version.Build:00}.{Math.Max(version.Revision, 0):00}";

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] GitHubAsset[]? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

    internal sealed record ReleaseInfo(string TagName, string VersionLabel, string Url, string AssetUrl);
}
