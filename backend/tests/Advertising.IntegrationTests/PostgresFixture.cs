using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Services;
using Npgsql;
using Pgvector.EntityFrameworkCore;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using Testcontainers.PostgreSql;
using Xunit;

namespace Advertising.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string? _externalConnectionString =
        Environment.GetEnvironmentVariable("ADVERTISING_TEST_POSTGRES_CONNECTION");
    private readonly PostgreSqlContainer? _postgres;

    public PostgresFixture()
    {
        if (string.IsNullOrWhiteSpace(_externalConnectionString))
            _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
                .WithDatabase("advertising_integration")
                .WithUsername("advertising_test")
                .WithPassword("advertising_test_password")
                .Build();
    }

    public string ConnectionString => _externalConnectionString ?? _postgres!.GetConnectionString();

    public Task InitializeAsync() => _postgres?.StartAsync() ?? Task.CompletedTask;

    public Task DisposeAsync() => _postgres?.DisposeAsync().AsTask() ?? Task.CompletedTask;

    public AppDbContext CreateContext(TenantContext? tenantContext = null, params IInterceptor[] interceptors)
        => CreateContext(ConnectionString, tenantContext, interceptors);

    public AppDbContext CreateContextWithEventBus(
        TenantContext tenantContext,
        IEventBus eventBus,
        params IInterceptor[] interceptors) =>
        CreateContext(ConnectionString, tenantContext, interceptors, eventBus);

    public async Task<IsolatedPostgresDatabase> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"advertising_migration_{Guid.NewGuid():N}";
        var adminConnectionBuilder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Pooling = false
        };
        await using var adminConnection = new NpgsqlConnection(adminConnectionBuilder.ConnectionString);
        await adminConnection.OpenAsync();

        await using (var permissionCommand = adminConnection.CreateCommand())
        {
            permissionCommand.CommandText = """
                SELECT rolcreatedb OR rolsuper
                FROM pg_roles
                WHERE rolname = current_user;
                """;
            var canCreateDatabase = (bool?)await permissionCommand.ExecuteScalarAsync() == true;
            if (!canCreateDatabase)
            {
                throw new InvalidOperationException(
                    "The canonical-phone migration test requires PostgreSQL CREATEDB permission to create an isolated database. The shared database was not modified.");
            }
        }

        await using (var createCommand = adminConnection.CreateCommand())
        {
            createCommand.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)} TEMPLATE template0";
            await createCommand.ExecuteNonQueryAsync();
        }

        var isolatedConnectionBuilder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        var isolatedDatabase = new IsolatedPostgresDatabase(
            adminConnectionBuilder.ConnectionString,
            isolatedConnectionBuilder.ConnectionString,
            databaseName);
        try
        {
            await using var isolatedConnection = new NpgsqlConnection(isolatedDatabase.ConnectionString);
            await isolatedConnection.OpenAsync();
            await using var extensionCommand = isolatedConnection.CreateCommand();
            extensionCommand.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await extensionCommand.ExecuteNonQueryAsync();
            return isolatedDatabase;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            await isolatedDatabase.DisposeAsync();
            throw new InvalidOperationException(
                "The isolated migration database was created, but the test role cannot install the vector extension. The isolated database was dropped and the shared database was not modified.",
                exception);
        }
        catch
        {
            await isolatedDatabase.DisposeAsync();
            throw;
        }
    }

    internal static AppDbContext CreateContext(
        string connectionString,
        TenantContext? tenantContext = null,
        IInterceptor[]? interceptors = null,
        IEventBus? eventBus = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector());
        if (interceptors is { Length: > 0 })
            optionsBuilder.AddInterceptors(interceptors);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDataProtection();
        serviceCollection.AddSingleton(new AdvertisingOptions());
        if (eventBus != null)
            serviceCollection.AddSingleton(eventBus);
        var services = serviceCollection.BuildServiceProvider();

        return new AppDbContext(optionsBuilder.Options, tenantContext ?? new TenantContext(), services);
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

public sealed class IsolatedPostgresDatabase(
    string adminConnectionString,
    string connectionString,
    string databaseName) : IAsyncDisposable
{
    public string ConnectionString { get; } = connectionString;

    public AppDbContext CreateContext(TenantContext? tenantContext = null, params IInterceptor[] interceptors) =>
        PostgresFixture.CreateContext(ConnectionString, tenantContext, interceptors);

    public async ValueTask DisposeAsync()
    {
        await using var adminConnection = new NpgsqlConnection(adminConnectionString);
        await adminConnection.OpenAsync();

        try
        {
            await using var terminateCommand = adminConnection.CreateCommand();
            terminateCommand.CommandText = """
                    SELECT pg_terminate_backend(pid)
                    FROM pg_stat_activity
                    WHERE datname = @database_name AND pid <> pg_backend_pid();
                    """;
            terminateCommand.Parameters.AddWithValue("database_name", databaseName);
            await terminateCommand.ExecuteNonQueryAsync();
        }
        finally
        {
            await using var dropCommand = adminConnection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE)";
            await dropCommand.ExecuteNonQueryAsync();
        }
    }

    private static string QuoteIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Advertising PostgreSQL";
}
