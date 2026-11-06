using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace muten.Core;

public record ReleaseInfo(string Tag, Version Version, string PageUrl, string? AssetUrl);

// Self-update from the GitHub releases of muten: check, download, extract and swap files in place
public static class Updater
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/elsergiovera/muten/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/elsergiovera/muten/releases/latest";
    public const string AssetName = "muten.zip";
    public const string OldSuffix = ".old";

    // Files renamed to *.old by the last swap, deleted on the next start
    private static string CleanupListPath => Path.Combine(SettingsManager.DataDir, "update-cleanup.json");

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("muten-updater", "1"));
        return client;
    }

    // "v2.0.1", "2.0.1", "v2.0" or "v2.0.1-beta" -> 2.0.1 (always three parts); null when it is not a version
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        int cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        if (!s.Contains('.')) s += ".0";
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    // Major.Minor.Build, ignoring the revision that assembly versions carry
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    public static bool IsNewer(Version? candidate, Version current) =>
        candidate is not null && Normalize(candidate) > Normalize(current);

    // Reads the fields muten needs from a GitHub "release" JSON object
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var version = ParseVersion(tag);
        if (tag is null || version is null) return null;

        if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;

        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;

        string? assetUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var n)
                    && string.Equals(n.GetString(), AssetName, StringComparison.OrdinalIgnoreCase)
                    && asset.TryGetProperty("browser_download_url", out var u))
                {
                    assetUrl = u.GetString();
                    break;
                }
            }
        }

        return new ReleaseInfo(tag, version, string.IsNullOrEmpty(page) ? ReleasesPageUrl : page, assetUrl);
    }

    public static async Task<ReleaseInfo?> GetLatestAsync(HttpClient client, string url = LatestReleaseUrl, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        return ParseRelease(json);
    }

    // Downloads the release zip into workDir (emptied first) and returns its path
    public static async Task<string> DownloadAsync(HttpClient client, ReleaseInfo release, string workDir, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(release.AssetUrl))
            throw new InvalidOperationException($"Release {release.Tag} has no {AssetName}");

        if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
        Directory.CreateDirectory(workDir);

        var zipPath = Path.Combine(workDir, AssetName);
        using (var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var target = File.Create(zipPath);
            await source.CopyToAsync(target, token).ConfigureAwait(false);
        }

        return zipPath;
    }

    // Extracts the zip next to it and returns the folder that holds exeName (the zip root or its only subfolder)
    public static string Extract(string zipPath, string exeName)
    {
        var extractDir = Path.Combine(Path.GetDirectoryName(zipPath)!, "files");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);

        // ExtractToDirectory refuses entries that would land outside extractDir
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        if (File.Exists(Path.Combine(extractDir, exeName))) return extractDir;

        var subDirs = Directory.GetDirectories(extractDir);
        if (subDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0
            && File.Exists(Path.Combine(subDirs[0], exeName)))
            return subDirs[0];

        throw new InvalidDataException($"{AssetName} does not contain {exeName}");
    }

    // Moves each file that will be replaced to *.old (Windows allows renaming a running exe) and copies the new
    // files in. Any failure undoes everything done so far and rethrows.
    public static UpdateSwap Apply(string sourceDir, string installDir)
    {
        var swap = new UpdateSwap();
        try
        {
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, file);
                var target = Path.GetFullPath(Path.Combine(installDir, relative));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    var old = FreeOldPath(target);
                    File.Move(target, old);
                    swap.Renamed.Add((target, old));
                }

                File.Copy(file, target);
                swap.Copied.Add(target);
            }

            RememberForCleanup(swap.Renamed.Select(r => r.Old));
            return swap;
        }
        catch
        {
            swap.Rollback();
            throw;
        }
    }

    // target.old, or target.1.old ... when an older leftover is still locked
    private static string FreeOldPath(string target)
    {
        var old = target + OldSuffix;
        for (int i = 1; File.Exists(old); i++)
        {
            try
            {
                File.Delete(old);
            }
            catch
            {
                old = $"{target}.{i}{OldSuffix}";
            }
        }
        return old;
    }

    private static void RememberForCleanup(IEnumerable<string> paths)
    {
        try
        {
            var all = LoadCleanupList().Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Directory.CreateDirectory(SettingsManager.DataDir);
            File.WriteAllText(CleanupListPath, JsonSerializer.Serialize(all));
        }
        catch
        {
            // Only costs a leftover file; the exe's own .old is found without the list
        }
    }

    private static List<string> LoadCleanupList()
    {
        try
        {
            return File.Exists(CleanupListPath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CleanupListPath)) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }

    // Deletes the *.old files left by an update: the ones recorded by Apply plus exePath's own exe.old
    // (never other *.old files, the install folder might be shared). Returns true when none are left.
    public static bool CleanupOldFiles(string exePath)
    {
        var candidates = LoadCleanupList()
            .Where(p => p.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
            .Append(exePath + OldSuffix)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var left = new List<string>();
        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                left.Add(path);
            }
        }

        try
        {
            var remaining = left.Where(p => !p.Equals(exePath + OldSuffix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remaining.Count == 0) File.Delete(CleanupListPath);
            else File.WriteAllText(CleanupListPath, JsonSerializer.Serialize(remaining));
        }
        catch { }

        return left.Count == 0;
    }
}

public class UpdateSwap
{
    public List<(string Target, string Old)> Renamed { get; } = [];
    public List<string> Copied { get; } = [];

    // Puts the install folder back the way it was
    public void Rollback()
    {
        foreach (var copied in Copied)
        {
            try { File.Delete(copied); } catch { }
        }

        for (int i = Renamed.Count - 1; i >= 0; i--)
        {
            var (target, old) = Renamed[i];
            try { File.Move(old, target, overwrite: true); } catch { }
        }

        Copied.Clear();
        Renamed.Clear();
    }
}
