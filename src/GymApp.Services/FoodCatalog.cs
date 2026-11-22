using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GymApp.Core;
using Microsoft.Data.Sqlite;

namespace GymApp.Services;

/// <summary>The USDA catalog (Phase 26): a read-only SQLite file, built by tools/food-catalog and embedded in this assembly.
/// It is extracted to the data folder when its version changes and read with Microsoft.Data.Sqlite directly (no
/// DbContext, so no compiled model), searched with FTS5. Never throws for a missing catalog: searches then find nothing.</summary>
public class FoodCatalog
{
    public const string FileName = "foods.db";
    private const string VersionFileName = "foods.version";

    private readonly string _folder;
    private readonly Func<Stream?> _openDb;
    private readonly Func<Stream?> _openVersion;
    private readonly object _gate = new();
    private bool _ready;

    /// <param name="dataFolder">Where the extracted catalog lives.</param>
    /// <param name="openDb">For tests: the catalog file to extract. Default: the one embedded in this assembly.</param>
    /// <param name="openVersion">For tests: its version text.</param>
    public FoodCatalog(string dataFolder, Func<Stream?>? openDb = null, Func<Stream?>? openVersion = null)
    {
        _folder = dataFolder;
        _openDb = openDb ?? (() => Embedded("GymApp.Services.foods.db"));
        _openVersion = openVersion ?? (() => Embedded("GymApp.Services.foods.version"));
    }

    public string Path => System.IO.Path.Combine(_folder, FileName);

    private static Stream? Embedded(string name) => typeof(FoodCatalog).Assembly.GetManifestResourceStream(name);

    /// <summary>The version of the catalog on disk after <see cref="EnsureExtracted"/>, e.g. "foundation 2026-10-29 + sr_legacy 2019-04-01".</summary>
    public string? Version { get; private set; }

    /// <summary>Copies the embedded catalog to the data folder if the file is missing or its version differs. Safe to call
    /// again; true when a catalog is there to read.</summary>
    public bool EnsureExtracted()
    {
        lock (_gate)
        {
            if (_ready) return true;
            try
            {
                Directory.CreateDirectory(_folder);
                using var versionStream = _openVersion();
                var embedded = versionStream is null ? null : new StreamReader(versionStream).ReadToEnd().Trim();
                var versionFile = System.IO.Path.Combine(_folder, VersionFileName);
                var onDisk = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;

                if (!File.Exists(Path) || embedded is null || onDisk != embedded)
                {
                    using var db = _openDb();
                    if (db is null) return false;
                    var temp = Path + ".tmp";
                    using (var dst = File.Create(temp)) db.CopyTo(dst);
                    SqliteConnection.ClearAllPools();   // nothing may hold the old file
                    File.Move(temp, Path, overwrite: true);
                    if (embedded is not null) File.WriteAllText(versionFile, embedded);
                    onDisk = embedded;
                }
                Version = onDisk;
                _ready = true;
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
            {
                return false;
            }
        }
    }

    /// <summary>Every typed word, as a whole word first ("egg" finds "Egg, whole, raw" before "Eggnog"), then as a prefix
    /// ("chick bre" finds chicken breast), each best match first. Empty when the text has no letters or digits.</summary>
    public Task<IReadOnlyList<CatalogFood>> SearchAsync(string text, int limit = 25) =>
        Task.Run<IReadOnlyList<CatalogFood>>(() =>
        {
            var words = BuildMatch(text, prefix: false);
            var prefixes = BuildMatch(text, prefix: true);
            if (words is null || prefixes is null || !EnsureExtracted()) return [];

            var result = new List<CatalogFood>();
            var seen = new HashSet<long>();
            using var db = Open();
            foreach (var match in new[] { words, prefixes })
            {
                if (result.Count >= limit) break;
                using var c = db.CreateCommand();
                c.CommandText = """
                    SELECT f.fdc_id, f.name, f.kcal, f.protein, f.carbs, f.fat, f.serving_name, f.serving_grams
                    FROM food_fts JOIN food f ON f.fdc_id = food_fts.rowid
                    WHERE food_fts MATCH $q
                    ORDER BY rank, length(f.name)
                    LIMIT $n
                    """;
                c.Parameters.AddWithValue("$q", match);
                c.Parameters.AddWithValue("$n", limit);
                using var r = c.ExecuteReader();
                while (r.Read() && result.Count < limit)
                {
                    var food = Read(r, portions: []);
                    if (seen.Add(food.FdcId)) result.Add(food);
                }
            }
            return result;
        });

    /// <summary>One food by USDA id, with all its portions; null when it isn't in this version of the catalog.</summary>
    public Task<CatalogFood?> GetAsync(long fdcId) =>
        Task.Run<CatalogFood?>(() =>
        {
            if (!EnsureExtracted()) return null;
            using var db = Open();
            using var c = db.CreateCommand();
            c.CommandText = "SELECT fdc_id, name, kcal, protein, carbs, fat, serving_name, serving_grams FROM food WHERE fdc_id = $id";
            c.Parameters.AddWithValue("$id", fdcId);
            CatalogFood? food;
            using (var r = c.ExecuteReader()) food = r.Read() ? Read(r, []) : null;
            if (food is null) return null;

            var portions = new List<CatalogPortion>();
            using var p = db.CreateCommand();
            p.CommandText = "SELECT name, grams FROM portion WHERE fdc_id = $id ORDER BY seq";
            p.Parameters.AddWithValue("$id", fdcId);
            using (var r = p.ExecuteReader()) while (r.Read()) portions.Add(new CatalogPortion(r.GetString(0), r.GetDouble(1)));
            return food with { Portions = portions };
        });

    /// <summary>The FTS5 query for typed text: each word a quoted prefix ("chick"* "bre"*), so punctuation can't break the
    /// syntax. Null when nothing is left.</summary>
    public static string? BuildMatch(string? text, bool prefix = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var sb = new StringBuilder();
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length == 0) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append('"').Append(word).Append(prefix ? "\"*" : "\"");
            word.Clear();
        }
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) word.Append(ch);
            else Flush();
        }
        Flush();
        return sb.Length == 0 ? null : sb.ToString();
    }

    private SqliteConnection Open()
    {
        SQLitePCL.Batteries_V2.Init();   // a no-op once EF Core has done it
        var db = new SqliteConnection($"Data Source={Path};Mode=ReadOnly");
        db.Open();
        return db;
    }

    private static CatalogFood Read(SqliteDataReader r, IReadOnlyList<CatalogPortion> portions) => new(
        r.GetInt64(0), r.GetString(1), r.GetDouble(2),
        r.IsDBNull(3) ? null : r.GetDouble(3), r.IsDBNull(4) ? null : r.GetDouble(4), r.IsDBNull(5) ? null : r.GetDouble(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetDouble(7), portions);
}
