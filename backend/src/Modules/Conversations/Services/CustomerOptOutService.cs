using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Shared.Infrastructure;
using System.Text.RegularExpressions;

namespace Modules.Conversations.Services;

public sealed class CustomerOptOutService(AppDbContext dbContext)
{
    private static readonly string[] NormalizedOptOutPhrases = new[]
    {
        "ماتبعتليش", "ما تبعتليش", "متبعتليش", "ما تبعت ليش",
        "ماتراسلنيش", "ما تراسلنيش", "متراسلنيش",
        "ماتكلمنيش تاني", "ما تكلمنيش تاني", "متكلمنيش تاني",
        "مش عايز رسايل", "مش عايز رسائل", "مش عاوز رسايل", "مش عاوز رسائل",
        "وقف الرسايل", "وقف الرسائل", "الغى الاشتراك", "الغي الاشتراك",
        "احذف رقمي", "امسح رقمي",
        "don't message me", "do not message me", "stop messaging me",
        "stop sending me", "unsubscribe", "opt out", "remove my number"
    }.Select(Normalize).ToArray();

    public async Task ApplyIfRequestedAsync(
        Customer customer,
        string? message,
        CancellationToken cancellationToken = default)
    {
        if (!IsOptOutRequest(message)) return;

        customer.IsBlacklisted = true;

        var pendingFollowUps = await dbContext.FollowUps
            .IgnoreQueryFilters()
            .Where(followUp => followUp.ProjectId == customer.ProjectId
                && followUp.CustomerId == customer.Id
                && followUp.Status == "Pending")
            .ToListAsync(cancellationToken);

        foreach (var followUp in pendingFollowUps)
        {
            followUp.Status = "Cancelled";
        }

    }

    internal static bool IsOptOutRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var normalized = Normalize(message);
        return NormalizedOptOutPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        var normalized = value
            .ToLowerInvariant()
            .Replace('أ', 'ا')
            .Replace('إ', 'ا')
            .Replace('آ', 'ا')
            .Replace('ى', 'ي')
            .Replace('ة', 'ه')
            .Replace("ـ", string.Empty, StringComparison.Ordinal);

        normalized = Regex.Replace(normalized, "[\\u064B-\\u065F\\u0670]", string.Empty);
        normalized = Regex.Replace(normalized, "[^\\p{L}\\p{N}']+", " ");
        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }
}
