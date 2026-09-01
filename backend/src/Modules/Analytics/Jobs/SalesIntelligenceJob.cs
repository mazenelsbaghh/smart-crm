using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Modules.Analytics.Application.Services;
using Shared.Infrastructure;

namespace Modules.Analytics.Jobs;

public sealed class SalesIntelligenceJob(
    AppDbContext db,
    ConversationSalesAnalyzer analyzer,
    WhatsAppGatewaySessionClient gatewaySessionClient,
    ILogger<SalesIntelligenceJob> logger)
{
    private const int RecentAnalysisBatchSize = 10;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    [AutomaticRetry(Attempts = 0)]
    public Task AnalyzeRecentAsync(CancellationToken cancellationToken) =>
        AnalyzeRecentAsync(DateTime.UtcNow, cancellationToken);

    internal async Task AnalyzeRecentAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await RecentCandidatesAsync(nowUtc, cancellationToken);
        try
        {
            foreach (var candidate in candidates)
                await AnalyzeCandidateAsync(candidate, cancellationToken);
        }
        catch (AiEngineUnavailableException exception)
        {
            logger.LogWarning(
                exception,
                "Sales AI provider is unavailable; recent activity analysis will resume on its next run.");
        }
    }

    private async Task<List<RecentAnalysisCandidate>> RecentCandidatesAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var cutoff = SalesAnalysisRecencyPolicy.Cutoff(nowUtc);
        var candidates = db.Conversations.IgnoreQueryFilters()
            .Where(conversation => conversation.LastMessageTimestamp >= cutoff
                && conversation.LastMessageTimestamp <= nowUtc
                && !db.ConversationSalesAnalyses.IgnoreQueryFilters().Any(analysis =>
                    analysis.ProjectId == conversation.ProjectId
                    && analysis.ConversationId == conversation.Id
                    && analysis.AnalysisVersion >= ConversationSalesAnalyzer.CurrentAnalysisVersion
                    && analysis.AnalyzedThroughMessageAtUtc >= conversation.LastMessageTimestamp));
        var whatsAppAccountRows = await candidates
            .Where(conversation => conversation.Channel == "WhatsApp")
            .Select(conversation => new
            {
                conversation.ProjectId,
                AccountId = conversation.WhatsAppAccountId ?? conversation.ProjectId
            })
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var connectedWhatsAppAccounts = await ConnectedWhatsAppAccountsAsync(
            whatsAppAccountRows.Select(row => new WhatsAppSessionKey(row.ProjectId, row.AccountId)),
            cancellationToken);

        var selected = await candidates
            .Where(conversation => conversation.Channel != "WhatsApp")
            .OrderByDescending(conversation => conversation.LastMessageTimestamp)
            .Select(conversation => new RecentAnalysisCandidate(
                conversation.ProjectId,
                conversation.Id,
                conversation.LastMessageTimestamp))
            .Take(RecentAnalysisBatchSize)
            .ToListAsync(cancellationToken);
        foreach (var account in connectedWhatsAppAccounts)
        {
            selected.AddRange(await candidates
                .Where(conversation => conversation.Channel == "WhatsApp"
                    && conversation.ProjectId == account.ProjectId
                    && (conversation.WhatsAppAccountId ?? conversation.ProjectId) == account.AccountId)
                .OrderByDescending(conversation => conversation.LastMessageTimestamp)
                .Select(conversation => new RecentAnalysisCandidate(
                    conversation.ProjectId,
                    conversation.Id,
                    conversation.LastMessageTimestamp))
                .Take(RecentAnalysisBatchSize)
                .ToListAsync(cancellationToken));
        }

        return selected
            .OrderByDescending(candidate => candidate.LastMessageTimestamp)
            .Take(RecentAnalysisBatchSize)
            .ToList();
    }

    private async Task<HashSet<WhatsAppSessionKey>> ConnectedWhatsAppAccountsAsync(
        IEnumerable<WhatsAppSessionKey> accounts,
        CancellationToken cancellationToken)
    {
        var checks = accounts.Select(async account => new
        {
            Account = account,
            Session = await gatewaySessionClient.GetAsync(
                account.ProjectId,
                account.AccountId,
                cancellationToken)
        });
        var statuses = await Task.WhenAll(checks);
        foreach (var status in statuses.Where(status => !status.Session.Connected))
        {
            logger.LogInformation(
                "Skipping recent WhatsApp sales analysis for project {ProjectId}, account {AccountId} because the gateway status is {Status}.",
                status.Account.ProjectId,
                status.Account.AccountId,
                status.Session.Status);
        }
        return statuses
            .Where(status => status.Session.Connected)
            .Select(status => status.Account)
            .ToHashSet();
    }

    private async Task AnalyzeCandidateAsync(
        RecentAnalysisCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            await analyzer.AnalyzeAsync(
                candidate.ProjectId,
                candidate.ConversationId,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is KeyNotFoundException
            or InvalidOperationException and not AiEngineUnavailableException)
        {
            logger.LogWarning(
                exception,
                "Sales AI analysis failed for project {ProjectId}, conversation {ConversationId}.",
                candidate.ProjectId,
                candidate.ConversationId);
        }
    }

    private sealed record RecentAnalysisCandidate(
        Guid ProjectId,
        Guid ConversationId,
        DateTime LastMessageTimestamp);

    private readonly record struct WhatsAppSessionKey(Guid ProjectId, Guid AccountId);
}
