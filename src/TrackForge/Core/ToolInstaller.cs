using System.IO.Compression;
using System.Net.Http;

namespace TrackForge.Core;

/// <summary>
/// Fetches yt-dlp and ffmpeg into TrackForge's own tools folder so the app works on
/// a clean machine without the user knowing what a PATH is.
///
/// These are downloaded rather than shipped inside the installer on purpose: ffmpeg
/// builds are GPL and redistributing them carries source-offer obligations, and
/// yt-dlp goes stale within weeks. Fetching from upstream sidesteps both.
/// </summary>
public static class ToolInstaller
{
    public const string YtDlpUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    // Gyan's "essentials" build: a stable URL that always points at the current release.
    public const string FfmpegZipUrl =
        "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    /// <summary>%LOCALAPPDATA%\TrackForge\tools - writable without admin rights.</summary>
    public static string ToolsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TrackForge", "tools");

    public static string YtDlpExe => Path.Combine(ToolsDirectory, "yt-dlp.exe");
    public static string FfmpegExe => Path.Combine(ToolsDirectory, "ffmpeg.exe");
    public static string FfprobeExe => Path.Combine(ToolsDirectory, "ffprobe.exe");

    public static bool HasYtDlp => File.Exists(YtDlpExe);
    public static bool HasFfmpeg => File.Exists(FfmpegExe);

    private static HttpClient NewClient() =>
        new(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(10),
            DefaultRequestHeaders = { { "User-Agent", "TrackForge/1.0" } },
        };

    public static async Task InstallYtDlpAsync(
        IProgress<(double percent, string message)>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ToolsDirectory);
        progress?.Report((0, "Downloading yt-dlp"));

        var temp = YtDlpExe + ".part";
        using (var http = NewClient())
            await DownloadAsync(http, YtDlpUrl, temp, progress, "yt-dlp", ct).ConfigureAwait(false);

        if (File.Exists(YtDlpExe)) File.Delete(YtDlpExe);
        File.Move(temp, YtDlpExe);
        progress?.Report((100, "yt-dlp installed"));
    }

    public static async Task InstallFfmpegAsync(
        IProgress<(double percent, string message)>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ToolsDirectory);
        var zipPath = Path.Combine(Path.GetTempPath(), "trackforge-ffmpeg.zip");

        using (var http = NewClient())
            await DownloadAsync(http, FfmpegZipUrl, zipPath, progress, "ffmpeg", ct).ConfigureAwait(false);

        progress?.Report((92, "Extracting ffmpeg"));

        // The zip nests everything under ffmpeg-<version>-essentials_build/bin/.
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var wanted in new[] { "ffmpeg.exe", "ffprobe.exe" })
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (entry is null) continue;

                var target = Path.Combine(ToolsDirectory, wanted);
                if (File.Exists(target)) File.Delete(target);
                entry.ExtractToFile(target, overwrite: true);
            }
        }, ct).ConfigureAwait(false);

        try { File.Delete(zipPath); } catch { }

        if (!File.Exists(FfmpegExe))
            throw new InvalidOperationException("ffmpeg.exe was not found inside the download.");

        progress?.Report((100, "ffmpeg installed"));
    }

    private static async Task DownloadAsync(
        HttpClient http, string url, string destination,
        IProgress<(double, string)>? progress, string label, CancellationToken ct)
    {
        using var response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;

            if (total > 0)
            {
                var percent = read * 90.0 / total;
                progress?.Report((percent, $"Downloading {label}  {read / 1048576.0:0.0} / {total / 1048576.0:0.0} MB"));
            }
            else
            {
                progress?.Report((0, $"Downloading {label}  {read / 1048576.0:0.0} MB"));
            }
        }
    }

    /// <summary>Pulls the newest yt-dlp over the top of the existing one.</summary>
    public static Task UpdateYtDlpAsync(
        IProgress<(double, string)>? progress, CancellationToken ct = default)
        => InstallYtDlpAsync(progress, ct);
}
