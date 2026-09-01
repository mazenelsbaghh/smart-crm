using Shared.Queue;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingKnowledgeProjectionTests
{
    [Fact]
    public void Newer_published_revision_applies_and_late_revision_cannot_resurrect_tombstone()
    {
        Assert.Equal(ProjectionVersionDecision.Apply, ProjectionVersionGuard.Decide(3, 4));
        Assert.Equal(ProjectionVersionDecision.ApplyTombstone, ProjectionVersionGuard.Decide(4, 5, true));
        Assert.Equal(ProjectionVersionDecision.Stale, ProjectionVersionGuard.Decide(5, 4));
    }

    [Fact]
    public void Missing_revision_is_a_recoverable_gap_not_a_guess()
    {
        Assert.Equal(ProjectionVersionDecision.Gap, ProjectionVersionGuard.Decide(2, 5));
    }
}
