using Microsoft.AspNetCore.Mvc;
using Modules.Advertising.Services;
using Shared.Security;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager/campaigns")]
public sealed class AdvertisingCampaignImportController(IProjectAuthorizationService authorization, ExistingCampaignImportService importer)
    : AdvertisingControllerBase(authorization)
{
    [HttpGet("facebook-existing")]
    public async Task<IActionResult> Existing(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        try
        {
            var candidates = await importer.PreviewAsync(projectId, cancellationToken);
            return Ok(candidates.Select(candidate => new
            {
                candidate.Ad.AdId, candidate.Ad.AdName, candidate.Ad.CampaignId, candidate.Ad.CampaignName, candidate.Ad.AdSetName,
                candidate.Ad.Status, candidate.Ad.EffectiveStatus, candidate.Ad.Objective, candidate.Ad.DailyBudget,
                candidate.Ad.PublisherPlatforms, candidate.Ad.FacebookPositions, candidate.AlreadyManaged, candidate.Eligible, candidate.IneligibleReason
            }));
        }
        catch (AdvertisingException exception) { return StatusCode(exception.StatusCode, new { code = exception.Code, message = exception.Message }); }
    }

    [HttpPost("import-facebook")]
    public async Task<IActionResult> Import(Guid projectId, [FromBody] ImportExistingAdsRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        try { return Ok(await importer.ImportAsync(projectId, request.AdIds ?? [], cancellationToken)); }
        catch (AdvertisingException exception) { return StatusCode(exception.StatusCode, new { code = exception.Code, message = exception.Message }); }
    }
}

public sealed record ImportExistingAdsRequest(IReadOnlyCollection<string>? AdIds);
