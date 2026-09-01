using Modules.Brain.Domain;

namespace Modules.Content.Services;

internal static class ContentKnowledgeQuery
{
    private const string ApprovedStatus = "Approved";
    private const string LegacyPublishedStatus = "Published";

    public static IQueryable<KnowledgeDocument> ReadyForGeneration(
        this IQueryable<KnowledgeDocument> documents,
        Guid projectId) =>
        documents.Where(document => document.ProjectId == projectId
            && (document.Status == ApprovedStatus
                || document.Status == LegacyPublishedStatus));
}
