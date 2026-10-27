using GymApp.Data;
using GymApp.Data.CompiledModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GymApp.Tests;

/// <summary>The app runs on the compiled model in GymApp.Data/CompiledModels (16c). After any change to the entities
/// or OnModelCreating it must be regenerated (see CLAUDE.md), or the phones would read and write with the old shape.</summary>
public sealed class CompiledModelTests
{
    private const string Regenerate = "The compiled model is out of date. Regenerate it: dotnet ef dbcontext optimize " +
        "--project src/GymApp.Data --startup-project src/GymApp.Desktop --output-dir CompiledModels";

    [Fact] public void TheAppsContexts_UseTheCompiledModel()
    {
        using var db = Open(out var connection);
        Assert.Same(AppDbContextModel.Instance, db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Model);
        connection.Dispose();
    }

    [Fact] public void CompiledModel_MatchesTheLiveModel()
    {
        using var db = Open(out var connection);
        var compiled = Describe(db.Model);
        var live = Describe(db.GetService<IDesignTimeModel>().Model);   // built from OnModelCreating now

        var missing = live.Except(compiled).ToList();
        var extra = compiled.Except(live).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{Regenerate}\nNot in the compiled model: {string.Join("; ", missing)}\nOnly in the compiled model: {string.Join("; ", extra)}");
        connection.Dispose();
    }

    /// <summary>The app no longer asks Migrate() to check this (it can't on iOS), so this does: every change to the
    /// model has its migration.</summary>
    [Fact] public void EveryModelChange_HasAMigration()
    {
        using var db = Open(out var connection);
        Assert.False(db.Database.HasPendingModelChanges(),
            "The model has changes with no migration. Add one: dotnet ef migrations add <Name> --project src/GymApp.Data --startup-project src/GymApp.Desktop");
        connection.Dispose();
    }

    private static AppDbContext Open(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }

    /// <summary>One line per table, column, key, foreign key and index: what reading and writing depend on.</summary>
    private static List<string> Describe(IModel model)
    {
        static string Names(IEnumerable<IReadOnlyProperty> properties) => string.Join(",", properties.Select(p => p.Name));

        var lines = new List<string>();
        foreach (var e in model.GetEntityTypes().OrderBy(e => e.Name))
        {
            lines.Add($"{e.Name} table {e.GetTableName()}");
            lines.AddRange(e.GetProperties().Select(p =>
                $"{e.Name}.{p.Name} {p.ClrType.Name} null={p.IsNullable} column={p.GetColumnName()} max={p.GetMaxLength()} " +
                $"generated={p.ValueGenerated} converter={p.GetValueConverter()?.ProviderClrType.Name}"));
            lines.AddRange(e.GetKeys().Select(k => $"{e.Name} key ({Names(k.Properties)}) primary={k.IsPrimaryKey()}"));
            lines.AddRange(e.GetForeignKeys().Select(f =>
                $"{e.Name} fk ({Names(f.Properties)}) -> {f.PrincipalEntityType.Name} {f.DeleteBehavior} required={f.IsRequired}"));
            lines.AddRange(e.GetIndexes().Select(i =>
                $"{e.Name} index ({Names(i.Properties)}) unique={i.IsUnique} filter={i.GetFilter()}"));
        }
        return lines;
    }
}
