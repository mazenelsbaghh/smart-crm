using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;

namespace Shared.Security;

public interface IProjectSecretVault
{
    bool IsProtected(string? storedValue);
    string Protect(Guid projectId, string secret);
    string? Unprotect(Guid projectId, string? storedValue);
}

public sealed class ProjectSecretVault : IProjectSecretVault
{
    private const string VersionPrefix = "v1:";
    private readonly IDataProtector _rootProtector;

    public ProjectSecretVault(IDataProtectionProvider provider) =>
        _rootProtector = provider.CreateProtector("Projects.Gemini.Credentials.v1");

    public bool IsProtected(string? storedValue) =>
        storedValue?.StartsWith(VersionPrefix, StringComparison.Ordinal) == true;

    public string Protect(Guid projectId, string secret)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project id is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Secret is required.", nameof(secret));
        return $"{VersionPrefix}{ForProject(projectId).Protect(secret.Trim())}";
    }

    public string? Unprotect(Guid projectId, string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue)) return null;
        if (!IsProtected(storedValue)) return storedValue.Trim(); // Read-only compatibility until startup migration runs.
        return ForProject(projectId).Unprotect(storedValue[VersionPrefix.Length..]);
    }

    private IDataProtector ForProject(Guid projectId) =>
        _rootProtector.CreateProtector(projectId.ToString("N"));
}

public static class ProjectSecretMigration
{
    public static async Task<int> ProtectLegacyGeminiKeysAsync(
        AppDbContext db,
        IProjectSecretVault vault,
        CancellationToken cancellationToken = default)
    {
        var settingsWithLegacyCredentials = await db.ProjectSettings.IgnoreQueryFilters()
            .Where(settings => settings.GeminiApiKey != string.Empty
                || settings.GeminiAgentPlatformApiKey != string.Empty)
            .ToListAsync(cancellationToken);
        var migrated = 0;
        foreach (var settings in settingsWithLegacyCredentials)
        {
            var plannerCredential = ProtectLegacyCredential(
                settings.ProjectId,
                settings.GeminiApiKey,
                vault);
            var agentPlatformCredential = ProtectLegacyCredential(
                settings.ProjectId,
                settings.GeminiAgentPlatformApiKey,
                vault);
            if (!plannerCredential.Changed && !agentPlatformCredential.Changed) continue;

            settings.GeminiApiKey = plannerCredential.StoredValue;
            settings.GeminiAgentPlatformApiKey = agentPlatformCredential.StoredValue;
            settings.UpdatedAt = DateTime.UtcNow;
            if (plannerCredential.Changed) migrated++;
            if (agentPlatformCredential.Changed) migrated++;
        }

        if (migrated > 0) await db.SaveChangesAsync(cancellationToken);
        return migrated;
    }

    private static (string StoredValue, bool Changed) ProtectLegacyCredential(
        Guid projectId,
        string storedValue,
        IProjectSecretVault vault)
    {
        if (storedValue.Length == 0) return (storedValue, false);
        if (string.IsNullOrWhiteSpace(storedValue)) return (string.Empty, true);
        return vault.IsProtected(storedValue)
            ? (storedValue, false)
            : (vault.Protect(projectId, storedValue), true);
    }
}
