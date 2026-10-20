namespace GymApp.Tests;

/// <summary>Tests that point GYMAPP_DATA_DIR (process-wide) at a folder of their own run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DataDirectoryCollection
{
    public const string Name = "Data directory";
}

/// <summary>Points GYMAPP_DATA_DIR at a new temporary folder; puts it back and deletes the folder on dispose.</summary>
public sealed class TempDataDirectory : IDisposable
{
    private readonly string? _previous = Environment.GetEnvironmentVariable("GYMAPP_DATA_DIR");

    public TempDataDirectory()
    {
        Directory.CreateDirectory(Path);
        Environment.SetEnvironmentVariable("GYMAPP_DATA_DIR", Path);
    }

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gymapp-data-{Guid.NewGuid():N}");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GYMAPP_DATA_DIR", _previous);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();   // let go of the database file before deleting it
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}
