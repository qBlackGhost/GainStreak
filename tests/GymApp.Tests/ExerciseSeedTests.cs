using GymApp.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public class ExerciseSeedTests
{
    [Fact] public void BuiltInExerciseNames_AreUnique()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        using var db = new AppDbContext(options);
        db.Database.Migrate();

        var names = db.Exercises.Where(e => e.OwnerUserId == null).Select(e => e.Name.ToLower()).ToList();
        Assert.NotEmpty(names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}