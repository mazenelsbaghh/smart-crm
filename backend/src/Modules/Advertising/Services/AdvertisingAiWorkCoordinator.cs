using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed class AdvertisingAiWorkCoordinator(AppDbContext db)
{
    public async Task<AdvertisingAiWorkItem?> GetCurrentAsync(Guid projectId, Guid ownerId, long ownerVersion, string inputHash,
        CancellationToken cancellationToken = default) =>
        await db.AdvertisingAiWorkItems.AsNoTracking().Where(x => x.ProjectId == projectId && x.OwnerId == ownerId &&
            x.OwnerVersion == ownerVersion && x.InputHash == inputHash && x.State == AiWorkState.Pending && x.DeadlineUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
}
