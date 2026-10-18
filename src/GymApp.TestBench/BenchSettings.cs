using System;
using System.IO;
using System.Text.Json;

namespace GymApp.TestBench;

/// <summary>What happens to the test data on the next start.</summary>
public enum DataReset { None, Empty, CopyReal }

/// <summary>The TestBench's own state, kept next to its data so it survives restarts: how far the clock is moved and
/// a pending data reset (done at the next start, before anything opens the database).</summary>
public sealed class BenchSettings
{
    public int DayOffset { get; set; }
    public DataReset PendingReset { get; set; }

    private static string LocalData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string Root => Path.Combine(LocalData, "GymApp-TestBench");
    public static string DataDir => Path.Combine(Root, "data");
    /// <summary>The real app's folder (the default of AppDbContext.DataDirectory).</summary>
    public static string RealDataDir => Path.Combine(LocalData, "GymApp");
    private static string FilePath => Path.Combine(Root, "bench.json");

    public static BenchSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<BenchSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { /* a broken file just means the defaults */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }

    /// <summary>Runs the pending reset, if any. Files are deleted before copying, so a copy never writes into a
    /// database file the previous run still has open.</summary>
    public void ApplyPendingReset()
    {
        if (PendingReset == DataReset.None) return;

        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, recursive: true);
        Directory.CreateDirectory(DataDir);

        if (PendingReset == DataReset.CopyReal && Directory.Exists(RealDataDir))
            foreach (var name in new[] { "gymapp.db", "gymapp.db-wal", "gymapp.db-shm", "remember.json" })
            {
                var from = Path.Combine(RealDataDir, name);
                if (File.Exists(from)) File.Copy(from, Path.Combine(DataDir, name));
            }

        PendingReset = DataReset.None;
        Save();
    }
}
