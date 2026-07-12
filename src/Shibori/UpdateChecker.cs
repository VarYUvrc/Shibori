using System.Net.Http.Json;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Shibori;

internal static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/VarYUvrc/Shibori/releases?per_page=20";
    public const string IssuesUrl = "https://github.com/VarYUvrc/Shibori/issues";
    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public static string CurrentVersionLabel => Format(CurrentVersion);

    public static async Task<ReleaseInfo?> FindAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Shibori/{CurrentVersionLabel}");
        var releases = await client.GetFromJsonAsync<GitHubRelease[]>(ReleasesUrl) ?? [];
        return releases
            .Where(release => !release.Draft && !string.IsNullOrWhiteSpace(release.TagName))
            .Select(release => (Release: release,
                Version: Version.TryParse(release.TagName.TrimStart('v', 'V'), out var version) ? version : null))
            .Where(item => item.Version is not null && item.Version > CurrentVersion)
            .OrderByDescending(item => item.Version)
            .Select(item =>
            {
                var asset = item.Release.Assets?.FirstOrDefault(candidate =>
                    candidate.Name.Equals("Shibori-win-x64.zip", StringComparison.OrdinalIgnoreCase));
                return asset is null ? null : new ReleaseInfo(item.Release.TagName, Format(item.Version!),
                    item.Release.HtmlUrl, asset.BrowserDownloadUrl);
            })
            .FirstOrDefault(item => item is not null);
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
