using GymApp.Data;
using GymApp.Data.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GymApp.Tests;

public sealed class TestDb : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDb()
    {
        _conn.Open();   // the in-memory database lives as long as this connection
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options;
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public int AddUser(string username)
    {
        using var db = CreateDbContext();
        var user = new User { Username = username, PasswordHash = "x" };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    public void Dispose() => _conn.Dispose();
}