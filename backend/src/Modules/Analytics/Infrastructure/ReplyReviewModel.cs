using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;

namespace Modules.Analytics.Infrastructure;

internal static class ReplyReviewModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<ReplyReviewSchedule>().HasIndex(row => row.ProjectId).IsUnique();
        var cases = builder.Entity<ReplyReviewCase>();
        cases.HasIndex(row => new { row.ProjectId, row.ConversationId }).IsUnique();
        cases.HasIndex(row => new { row.State, row.NextRunAtUtc });
        cases.Property(row => row.State).HasConversion<string>().HasMaxLength(30);
        cases.Property(row => row.LeaseToken).IsConcurrencyToken();
        cases.HasOne<Conversation>().WithMany().HasForeignKey(row => row.ConversationId).OnDelete(DeleteBehavior.Cascade);
        var runs = builder.Entity<ReplyReviewRun>();
        runs.HasIndex(row => new { row.ProjectId, row.CaseId, row.FinishedAtUtc });
        runs.HasOne<ReplyReviewCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Cascade);
    }
}
