using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

// Builds foods.db (the USDA catalog GainStreak bundles) from FoodData Central CSV downloads.
// Usage: dotnet run --project tools/food-catalog -- <foundation-folder> <sr-legacy-folder> [output-file]
// See README.md. The input folders stay outside the repo.

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: food-catalog <foundation-csv-folder> <sr-legacy-csv-folder> [output foods.db]");
    return 1;
}

var output = args.Length > 2 ? args[2] : "foods.db";
var sources = new[]
{
    new Source(args[0], "foundation_food", "foundation"),
    new Source(args[1], "sr_legacy_food", "sr_legacy"),
};

const int SchemaVersion = 1;
const double KjPerKcal = 4.184;

if (File.Exists(output)) File.Delete(output);

var foods = new List<FoodRow>();
foreach (var s in sources)
{
    var dir = s.Dir;
    if (!File.Exists(Path.Combine(dir, "food.csv"))) { Console.Error.WriteLine($"No food.csv in {dir}"); return 1; }

    // 1. which foods
    var ids = new Dictionary<long, FoodRow>();
    foreach (var r in Csv.Read(Path.Combine(dir, "food.csv")))
    {
        if (r["data_type"] != s.DataType) continue;
        var id = long.Parse(r["fdc_id"], CultureInfo.InvariantCulture);
        ids[id] = new FoodRow { FdcId = id, Name = r["description"].Trim(), Source = s.Tag, PublicationDate = r["publication_date"] };
    }

    // 2. nutrients (per 100 g)
    foreach (var r in Csv.Read(Path.Combine(dir, "food_nutrient.csv")))
    {
        if (!long.TryParse(r["fdc_id"], out var id) || !ids.TryGetValue(id, out var f)) continue;
        if (!double.TryParse(r["amount"], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) continue;
        switch (r["nutrient_id"])
        {
            case "1008": f.Kcal1008 = amount; break;
            case "2048": f.Kcal2048 = amount; break;
            case "2047": f.Kcal2047 = amount; break;
            case "1062": f.Kj = amount; break;
            case "1003": f.Protein = amount; break;
            case "1004": f.Fat = amount; break;
            case "1005": f.Carbs = amount; break;
        }
    }

    // 3. household portions
    var units = new Dictionary<string, string>();
    foreach (var r in Csv.Read(Path.Combine(dir, "measure_unit.csv"))) units[r["id"]] = r["name"];
    foreach (var r in Csv.Read(Path.Combine(dir, "food_portion.csv")))
    {
        if (!long.TryParse(r["fdc_id"], out var id) || !ids.TryGetValue(id, out var f)) continue;
        if (!double.TryParse(r["gram_weight"], NumberStyles.Float, CultureInfo.InvariantCulture, out var grams) || grams < 1 || grams > 2000) continue;
        double.TryParse(r["amount"], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount);
        if (amount <= 0) amount = 1;
        var name = PortionName(amount, units.GetValueOrDefault(r["measure_unit_id"], ""), r["portion_description"], r["modifier"]);
        if (name is null) continue;
        int.TryParse(r["seq_num"], out var seq);
        f.Portions.Add((seq == 0 ? 999 : seq, name, Math.Round(grams, 1)));
    }

    foods.AddRange(ids.Values);
}

// Keep foods with energy; kcal as published, else Atwater, else kJ / 4.184.
var kept = new List<FoodRow>();
var dropped = 0;
foreach (var f in foods)
{
    var kcal = f.Kcal1008 ?? f.Kcal2048 ?? f.Kcal2047 ?? (f.Kj is { } kj ? kj / KjPerKcal : null);
    if (kcal is null) { dropped++; continue; }
    f.Kcal = kcal.Value;
    kept.Add(f);
}

var catalogVersion = string.Join(" + ", sources.Select(s => $"{s.Tag} {Latest(foods, s.Tag)}"));

using (var db = new SqliteConnection($"Data Source={output}"))
{
    db.Open();
    Exec(db, "PRAGMA journal_mode = DELETE;");
    Exec(db, """
        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE food (
            fdc_id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            source TEXT NOT NULL,
            kcal REAL NOT NULL,
            protein REAL, carbs REAL, fat REAL,
            serving_name TEXT, serving_grams REAL);
        CREATE TABLE portion (
            fdc_id INTEGER NOT NULL, seq INTEGER NOT NULL, name TEXT NOT NULL, grams REAL NOT NULL);
        CREATE INDEX portion_food ON portion (fdc_id);
        CREATE VIRTUAL TABLE food_fts USING fts5(
            name, content='food', content_rowid='fdc_id',
            tokenize='unicode61 remove_diacritics 2', prefix='2 3 4');
        """);

    using var tx = db.BeginTransaction();
    foreach (var f in kept)
    {
        var first = f.Portions.OrderBy(p => p.Seq).FirstOrDefault();
        Insert(db, tx, "INSERT INTO food VALUES ($id,$n,$s,$k,$p,$c,$f,$sn,$sg)",
            ("$id", f.FdcId), ("$n", f.Name), ("$s", f.Source), ("$k", Math.Round(f.Kcal, 1)),
            ("$p", Round(f.Protein)), ("$c", Round(f.Carbs)), ("$f", Round(f.Fat)),
            ("$sn", first.Name), ("$sg", first.Name is null ? null : first.Grams));
        var seen = new HashSet<string>();
        foreach (var p in f.Portions.OrderBy(p => p.Seq))
            if (seen.Add(p.Name))
                Insert(db, tx, "INSERT INTO portion VALUES ($id,$q,$n,$g)", ("$id", f.FdcId), ("$q", p.Seq), ("$n", p.Name), ("$g", p.Grams));
    }
    ExecTx(db, tx, "INSERT INTO food_fts(rowid, name) SELECT fdc_id, name FROM food;");
    ExecTx(db, tx, "INSERT INTO food_fts(food_fts) VALUES('optimize');");
    Insert(db, tx, "INSERT INTO meta VALUES ('catalog_version', $v)", ("$v", catalogVersion));
    Insert(db, tx, "INSERT INTO meta VALUES ('schema_version', $v)", ("$v", SchemaVersion.ToString()));
    tx.Commit();
    Exec(db, "VACUUM;");
}

// The app embeds foods.version next to foods.db and re-extracts the catalog when it changes
File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "foods.version"), catalogVersion);
var size = new FileInfo(output).Length;
Console.WriteLine($"Wrote {output}: {kept.Count} foods ({dropped} dropped without energy), {size / 1024.0 / 1024.0:F2} MB");
Console.WriteLine($"Catalog version: {catalogVersion}");
Console.WriteLine($"Foods with a serving: {kept.Count(f => f.Portions.Count > 0)}; without protein/carbs/fat: {kept.Count(f => f.Protein is null || f.Carbs is null || f.Fat is null)}");
return 0;

static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 2);

static string Latest(List<FoodRow> foods, string tag) =>
    foods.Where(f => f.Source == tag).Select(f => f.PublicationDate).Where(d => d.Length > 0).DefaultIfEmpty("").Max()!;

static string? PortionName(double amount, string unit, string description, string modifier)
{
    var amountText = amount == Math.Floor(amount) ? ((long)amount).ToString() : amount.ToString("0.##", CultureInfo.InvariantCulture);
    var parts = new List<string>();
    var hasUnit = unit.Length > 0 && unit != "undetermined";
    if (hasUnit) parts.Add($"{amountText} {unit}");
    else if (description.Length == 0 && modifier.Length == 0) return null;
    foreach (var t in new[] { description.Trim(), modifier.Trim() })
        if (t.Length > 0 && !parts.Contains(t)) parts.Add(t);
    var name = string.Join(", ", parts);
    // SR Legacy writes "serving 1 roll with icing" or "1 slice": keep what's there, tidy a leading amount.
    if (!hasUnit && !char.IsDigit(name[0])) name = $"{amountText} {name}";
    return name;
}

static void Exec(SqliteConnection db, string sql) => ExecTx(db, null, sql);
static void ExecTx(SqliteConnection db, SqliteTransaction? tx, string sql)
{
    using var c = db.CreateCommand();
    c.Transaction = tx;
    c.CommandText = sql;
    c.ExecuteNonQuery();
}

static void Insert(SqliteConnection db, SqliteTransaction tx, string sql, params (string, object?)[] ps)
{
    using var c = db.CreateCommand();
    c.Transaction = tx;
    c.CommandText = sql;
    foreach (var (n, v) in ps) c.Parameters.AddWithValue(n, v ?? DBNull.Value);
    c.ExecuteNonQuery();
}

record Source(string Dir, string DataType, string Tag);

class FoodRow
{
    public long FdcId;
    public string Name = "";
    public string Source = "";
    public string PublicationDate = "";
    public double? Kcal1008, Kcal2048, Kcal2047, Kj, Protein, Carbs, Fat;
    public double Kcal;
    public List<(int Seq, string Name, double Grams)> Portions = new();
}

static class Csv
{
    /// <summary>Reads a CSV with a header row as dictionaries; handles quoted fields with commas, "" and newlines.</summary>
    public static IEnumerable<Dictionary<string, string>> Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        var header = ReadRecord(reader);
        if (header is null) yield break;
        List<string>? rec;
        while ((rec = ReadRecord(reader)) is not null)
        {
            if (rec.Count == 1 && rec[0].Length == 0) continue;
            var d = new Dictionary<string, string>(header.Count);
            for (var i = 0; i < header.Count; i++) d[header[i]] = i < rec.Count ? rec[i] : "";
            yield return d;
        }
    }

    private static List<string>? ReadRecord(StreamReader r)
    {
        if (r.Peek() < 0) return null;
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        int c;
        while ((c = r.Read()) >= 0)
        {
            var ch = (char)c;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (r.Peek() == '"') { r.Read(); sb.Append('"'); }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else if (ch == '\n') break;
            else if (ch != '\r') sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return fields;
    }
}
