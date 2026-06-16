using System.Net.Http;
using System.Text.Json;

namespace CP2077SaveKit.Core;

public sealed record UpdateInfo(string LatestTag, string ReleaseUrl, string? DmgUrl)
{
    /// <summary>Best URL to send the user to: the .dmg asset if found, else the release page.</summary>
    public string DownloadUrl => DmgUrl ?? ReleaseUrl;
}

/// <summary>
/// Best-effort check against the GitHub Releases API. Returns the latest release if it is newer
/// than the running version, otherwise null. Never throws (network/offline failures are silent).
/// </summary>
public static class UpdateChecker
{
    private const string Repo = "ysrdevs/cyberpunk-savekit-mac";

    public static async Task<UpdateInfo?> CheckAsync(Version current)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("cp2077-savekit-update-check");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var latest = ParseVersion(tag);
            if (latest is null || latest <= current) return null;

            var releaseUrl = root.TryGetProperty("html_url", out var h)
                ? h.GetString() ?? $"https://github.com/{Repo}/releases/latest"
                : $"https://github.com/{Repo}/releases/latest";

            string? dmg = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u))
                    { dmg = u.GetString(); break; }
                }

            return new UpdateInfo(tag, releaseUrl, dmg);
        }
        catch
        {
            return null; // offline or API hiccup: stay quiet, never bother the player
        }
    }

    private static Version? ParseVersion(string tag) =>
        Version.TryParse(tag.TrimStart('v', 'V').Trim(), out var v) ? v : null;
}
