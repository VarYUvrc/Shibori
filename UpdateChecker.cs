using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;

namespace Shibori;

internal static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/VarYUvrc/Shibori/releases/latest";
    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task<ReleaseInfo?> FindAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Shibori/{CurrentVersion}");
        var release = await client.GetFromJsonAsync<GitHubRelease>(ReleasesUrl);
        if (release is null || release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName)) return null;
        var tag = release.TagName.TrimStart('v', 'V');
        return Version.TryParse(tag, out var version) && version > CurrentVersion
            ? new ReleaseInfo(release.TagName, release.HtmlUrl) : null;
    }

    private sealed record GitHubRelease(string TagName, string HtmlUrl, bool Draft, bool Prerelease);
    internal sealed record ReleaseInfo(string TagName, string Url);
}
