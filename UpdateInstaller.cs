using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using Application = System.Windows.Application;

namespace Shibori;

internal static class UpdateInstaller
{
    public static async Task InstallAsync(UpdateChecker.ReleaseInfo release)
    {
        var root = Path.Combine(Path.GetTempPath(), $"Shibori-update-{Guid.NewGuid():N}");
        var zip = Path.Combine(root, "update.zip");
        Directory.CreateDirectory(root);
        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
        using (var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(zip);
            await source.CopyToAsync(destination);
        }

        var extracted = Path.Combine(root, "extracted");
        ZipFile.ExtractToDirectory(zip, extracted);
        var script = Path.Combine(root, "apply-update.ps1");
        File.WriteAllText(script, Script, Encoding.UTF8);
        var updater = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        updater.ArgumentList.Add("-NoProfile");
        updater.ArgumentList.Add("-ExecutionPolicy");
        updater.ArgumentList.Add("Bypass");
        updater.ArgumentList.Add("-File");
        updater.ArgumentList.Add(script);
        updater.ArgumentList.Add(Environment.ProcessId.ToString());
        updater.ArgumentList.Add(extracted);
        updater.ArgumentList.Add(AppContext.BaseDirectory);
        updater.ArgumentList.Add(Environment.ProcessPath ?? "Shibori.exe");
        Process.Start(updater);
        AppLogger.Info($"Update downloaded: {release.VersionLabel}");
        Application.Current.Shutdown();
    }

    private const string Script = """
param($ProcessId, $Source, $Target, $Executable)
while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 250 }
Start-Sleep -Milliseconds 500
Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force
Start-Process -FilePath $Executable
""";
}
