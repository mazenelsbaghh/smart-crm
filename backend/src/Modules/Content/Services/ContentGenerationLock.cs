using System.Data;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;

namespace Modules.Content.Services;

internal sealed class ContentGenerationLock(AppDbContext dbContext, string resource) : IAsyncDisposable
{
    public static string Sample(Guid projectId) => $"content:sample:{projectId:N}";
    public static string Week(Guid projectId) => $"content:week:{projectId:N}";
    public static string Cards(Guid projectId, Guid gameId) => $"content:cards:{projectId:N}:{gameId:N}";

    public static Task<ContentGenerationLock?> TryAcquireAsync(
        AppDbContext dbContext, string resource, CancellationToken cancellationToken) =>
        AcquireCoreAsync(dbContext, resource, "pg_try_advisory_lock", cancellationToken);

    public static async Task<ContentGenerationLock> AcquireAsync(
        AppDbContext dbContext, string resource, CancellationToken cancellationToken) =>
        (await AcquireCoreAsync(dbContext, resource, "pg_advisory_lock", cancellationToken))!;

    private static async Task<ContentGenerationLock?> AcquireCoreAsync(
        AppDbContext dbContext, string resource, string function, CancellationToken cancellationToken)
    {
        var generationLock = new ContentGenerationLock(dbContext, resource);
        if (!dbContext.Database.IsRelational()) return generationLock;
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var acquired = await generationLock.ExecuteAsync(function, cancellationToken);
            if (function == "pg_advisory_lock" || acquired is true) return generationLock;
        }
        catch
        {
            await dbContext.Database.CloseConnectionAsync();
            throw;
        }
        await dbContext.Database.CloseConnectionAsync();
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (!dbContext.Database.IsRelational()) return;
        try
        {
            if (dbContext.Database.GetDbConnection().State == ConnectionState.Open)
                await ExecuteAsync("pg_advisory_unlock", CancellationToken.None);
        }
        finally
        {
            // Explicit unlock prevents a pooled connection retaining ownership after this worker finishes.
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<object?> ExecuteAsync(string function, CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {function}(hashtextextended(@resource, 0))";
        command.CommandTimeout = function == "pg_advisory_lock" ? 1800 : 30;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
