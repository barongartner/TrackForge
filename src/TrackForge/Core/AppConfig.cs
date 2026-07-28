using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackForge.Core;

/// <summary>User settings, persisted to %APPDATA%\TrackForge\config.json.</summary>
public sealed class AppConfig
{
    public string LibraryFolder { get; set; } = @"F:\Music";
    public string OutputFolder { get; set; } = @"F:\Music";
    public string Format { get; set; } = "mp3";
    public string Bitrate { get; set; } = "320";
    public string FilenamePattern { get; set; } = "{track} {title}";
    public bool AnalyzeBpmAndKey { get; set; } = true;
    public bool AutoArt { get; set; } = true;
    public bool ForceTitleCase { get; set; } = true;
    public bool WriteSourceUrl { get; set; } = true;
    public bool ImportDjayData { get; set; } = true;
    public bool SkipDuplicates { get; set; } = true;
    public string CookiesFromBrowser { get; set; } = "";
    public string YtDlpPath { get; set; } = "";
    public string FfmpegPath { get; set; } = "";
    public int MaxConcurrentJobs { get; set; } = 2;
    public string ItunesCountry { get; set; } = "CA";

    [JsonIgnore]
    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrackForge");

    [JsonIgnore]
    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Opts);
                if (cfg != null) return cfg;
            }
        }
        catch { /* fall through to defaults */ }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, Opts));
        }
        catch { /* a settings write failure should never kill the app */ }
    }
}
