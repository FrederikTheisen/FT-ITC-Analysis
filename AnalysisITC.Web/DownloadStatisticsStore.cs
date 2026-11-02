using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

/// <summary>Independent of scientific projects, registration and interpretation accounting.</summary>
internal sealed class DownloadStatisticsStore(string path)
{
    const string History = """
        WITH history AS (
          SELECT a.*, s.observed_utc,
            lag(s.observed_utc) OVER (PARTITION BY asset_id ORDER BY a.day) AS previous_utc,
            lag(download_count) OVER (PARTITION BY asset_id ORDER BY a.day) AS previous_count
          FROM installer_downloads a JOIN download_snapshots s ON s.day=a.day
        )
        SELECT release_id, asset_id, version, prerelease, file_name, distribution, architecture, package,
          download_count, observed_utc, previous_utc, previous_count FROM history
        """;

    public DownloadSnapshot? ReadLatest()
    {
        if (!File.Exists(path)) return null;
        using var db = Open(readOnly: true);
        // SQLite may leave an empty database after the first transaction is rolled back.
        using var tables = db.CreateCommand();
        tables.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table'";
        if (Convert.ToInt64(tables.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) return null;
        return ReadLatest(db);
    }

    public DownloadSnapshot? ReadPrevious(DateOnly day)
    {
        if (!File.Exists(path)) return null;
        using var db = Open(readOnly: true);
        return ReadLatest(db, before: day);
    }

    public DownloadSnapshot Save(DateOnly day, DateTimeOffset observedUtc, IReadOnlyList<InstallerDownload> assets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var db = Open(readOnly: false);
        using (var transaction = db.BeginTransaction())
        {
            using var schema = db.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS download_snapshots(day TEXT PRIMARY KEY, observed_utc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS installer_downloads(
                  day TEXT NOT NULL REFERENCES download_snapshots(day), release_id INTEGER NOT NULL,
                  asset_id INTEGER NOT NULL, version TEXT NOT NULL, prerelease INTEGER NOT NULL,
                  file_name TEXT NOT NULL, distribution TEXT NOT NULL, architecture TEXT NOT NULL,
                  package TEXT NOT NULL, download_count INTEGER NOT NULL CHECK(download_count >= 0),
                  PRIMARY KEY(day, asset_id));
                CREATE INDEX IF NOT EXISTS installer_download_history ON installer_downloads(asset_id, day);
                """;
            schema.ExecuteNonQuery();
            using var snapshot = db.CreateCommand();
            snapshot.Transaction = transaction;
            snapshot.CommandText = "INSERT OR IGNORE INTO download_snapshots(day,observed_utc) VALUES($day,$utc)";
            snapshot.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            snapshot.Parameters.AddWithValue("$utc", Stamp(observedUtc));
            if (snapshot.ExecuteNonQuery() > 0)
                foreach (var asset in assets)
                {
                    using var insert = db.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO installer_downloads(day,release_id,asset_id,version,prerelease,file_name,distribution,architecture,package,download_count)
                        VALUES($day,$release,$asset,$version,$prerelease,$name,$distribution,$architecture,$package,$count)
                        """;
                    insert.Parameters.AddWithValue("$day", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    insert.Parameters.AddWithValue("$release", asset.ReleaseId);
                    insert.Parameters.AddWithValue("$asset", asset.AssetId);
                    insert.Parameters.AddWithValue("$version", asset.Version);
                    insert.Parameters.AddWithValue("$prerelease", asset.Prerelease ? 1 : 0);
                    insert.Parameters.AddWithValue("$name", asset.FileName);
                    insert.Parameters.AddWithValue("$distribution", asset.Distribution);
                    insert.Parameters.AddWithValue("$architecture", asset.Architecture);
                    insert.Parameters.AddWithValue("$package", asset.Package);
                    insert.Parameters.AddWithValue("$count", asset.Count);
                    insert.ExecuteNonQuery();
                }
            transaction.Commit();
        }
        return ReadLatest(db)!;
    }

    public void ExportCsv(TextWriter output)
    {
        output.WriteLine("observed_utc,previous_observed_utc,release_id,asset_id,version,prerelease,distribution,architecture,package,file_name,cumulative_downloads,increase,interval_status");
        if (!File.Exists(path)) return;
        using var db = Open(readOnly: true);
        using var query = db.CreateCommand();
        query.CommandText = History + " ORDER BY observed_utc,version,distribution,asset_id";
        using var row = query.ExecuteReader();
        while (row.Read())
        {
            var observation = ReadObservation(row);
            var asset = observation.Installer;
            string[] values = [Stamp(observation.ObservedUtc), observation.PreviousObservedUtc is { } previous ? Stamp(previous) : "",
                asset.ReleaseId.ToString(CultureInfo.InvariantCulture), asset.AssetId.ToString(CultureInfo.InvariantCulture),
                asset.Version, asset.Prerelease ? "true" : "false", asset.Distribution, asset.Architecture, asset.Package,
                asset.FileName, asset.Count.ToString(CultureInfo.InvariantCulture), observation.Increase?.ToString(CultureInfo.InvariantCulture) ?? "",
                observation.IntervalStatus];
            output.WriteLine(string.Join(',', values.Select(Csv)));
        }
    }

    static DownloadSnapshot? ReadLatest(SqliteConnection db, DateOnly? before = null)
    {
        string day, stamp;
        using (var query = db.CreateCommand())
        {
            query.CommandText = "SELECT day,observed_utc FROM download_snapshots"
                + (before is null ? "" : " WHERE day < $before") + " ORDER BY day DESC LIMIT 1";
            if (before is { } boundary)
                query.Parameters.AddWithValue("$before", boundary.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            using var row = query.ExecuteReader();
            if (!row.Read()) return null;
            day = row.GetString(0); stamp = row.GetString(1);
        }
        using var assets = db.CreateCommand();
        assets.CommandText = History + " WHERE day=$day ORDER BY version DESC,distribution,asset_id";
        assets.Parameters.AddWithValue("$day", day);
        var observations = new List<DownloadObservation>();
        using var reader = assets.ExecuteReader();
        while (reader.Read()) observations.Add(ReadObservation(reader));
        return new(DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture), ParseStamp(stamp), observations);
    }

    static DownloadObservation ReadObservation(SqliteDataReader row) => new(ParseStamp(row.GetString(9)),
        new(row.GetInt64(0), row.GetInt64(1), row.GetString(2), row.GetBoolean(3), row.GetString(4),
            row.GetString(5), row.GetString(6), row.GetString(7), row.GetInt64(8)),
        row.IsDBNull(10) ? null : ParseStamp(row.GetString(10)), row.IsDBNull(11) ? null : row.GetInt64(11));

    SqliteConnection Open(bool readOnly)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false, DefaultTimeout = 5, ForeignKeys = true,
        }.ToString());
        try { db.Open(); return db; }
        catch { db.Dispose(); throw; }
    }

    static string Stamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    static DateTimeOffset ParseStamp(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    static string Csv(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
