using Testcontainers.PostgreSql;

namespace GymApp.Server.Tests;

/// <summary>One Postgres container for the whole run. Every test gets its own database on it (see <see cref="ServerTest"/>).
/// Needs Docker running.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    /// <summary>The connection string for a database of that name on the container. It doesn't exist until migrated.</summary>
    public string ConnectionString(string database) =>
        new Npgsql.NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>The server tests share the container, so they run as one collection.</summary>
[CollectionDefinition(Name)]
public sealed class ServerCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Server";
}
