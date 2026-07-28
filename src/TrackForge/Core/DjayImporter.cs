using System.Text;
using System.Text.RegularExpressions;

namespace TrackForge.Core;

/// <summary>
/// Pulls the BPM that Algoriddim djay already analysed out of its MediaLibrary.db,
/// so tracks you've DJ'd with don't get re-analysed from scratch.
///
/// djay stores records as its own "TSAF" binary blobs, which we don't parse
/// properly - we just scrape the printable strings for the file:// URL and pair
/// it with the BPM from the secondary index table. Deliberately best-effort:
/// any failure here just means no djay data, which is fine.
/// </summary>
public static class DjayImporter
{
    private static readonly Regex PrintableRun = new(@"[\x20-\x7e]{4,}", RegexOptions.Compiled);

    /// <summary>Filename (lowercased) -> BPM.</summary>
    public static Dictionary<string, double> Load(string libraryFolder)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var db in CandidateDatabases(libraryFolder))
        {
            if (!File.Exists(db)) continue;
            try { Scrape(db, result); }
            catch { /* locked, moved, or a format we don't know: skip it */ }
        }
        return result;
    }

    private static IEnumerable<string> CandidateDatabases(string libraryFolder)
    {
        yield return Path.Combine(libraryFolder, "djay", "djay Media Library", "MediaLibrary.db");
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "djay", "djay Media Library", "MediaLibrary.db");
        yield return Path.Combine(local, "Packages", "AlgoriddimGmbH.djay_pmcgwrrjmv2n0",
                                  "LocalState", "djay Media Library", "MediaLibrary.db");
    }

    private static void Scrape(string dbPath, Dictionary<string, double> into)
    {
        // Copy first: djay keeps the live DB locked with a WAL open.
        var temp = Path.Combine(Path.GetTempPath(), "trackforge_djay.db");
        File.Copy(dbPath, temp, overwrite: true);

        var uuidByRow = new Dictionary<long, string>();
        var pathByUuid = new Dictionary<string, string>();
        var bpmByRow = new Dictionary<long, double>();

        using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={temp};Mode=ReadOnly"))
        {
            con.Open();

            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT rowid, key, data FROM database2 WHERE collection IN " +
                    "('mediaItemAnalyzedData','localMediaItemLocations')";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    long rowid = r.GetInt64(0);
                    string key = r.GetString(1);
                    uuidByRow[rowid] = key;

                    if (r.IsDBNull(2)) continue;
                    var blob = (byte[])r["data"];
                    foreach (Match m in PrintableRun.Matches(Encoding.ASCII.GetString(blob)))
                    {
                        var s = m.Value;
                        if (!s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) continue;
                        var decoded = Uri.UnescapeDataString(s[8..]).Replace('/', '\\');
                        pathByUuid[key] = decoded;
                        break;
                    }
                }
            }

            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT rowid, bpm, manualBPM FROM secondaryIndex_mediaItemAnalyzedDataIndex";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    double? manual = r.IsDBNull(2) ? null : r.GetDouble(2);
                    double? auto = r.IsDBNull(1) ? null : r.GetDouble(1);
                    var bpm = manual ?? auto;
                    if (bpm is > 0) bpmByRow[r.GetInt64(0)] = bpm.Value;
                }
            }
        }

        foreach (var (rowid, bpm) in bpmByRow)
        {
            if (!uuidByRow.TryGetValue(rowid, out var uuid)) continue;
            if (!pathByUuid.TryGetValue(uuid, out var path)) continue;
            into[Path.GetFileName(path)] = Math.Round(bpm, 1);
        }

        try { File.Delete(temp); } catch { }
    }
}
