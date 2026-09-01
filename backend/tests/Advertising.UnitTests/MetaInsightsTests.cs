using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaInsightsTests
{
    [Fact]
    public void Identical_overlapping_pull_is_deduplicated_but_provider_correction_creates_a_revision()
    {
        var row = new MetaInsightRow("ad", DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1),
            42.5m, 4200, 110, 1.8m, new Dictionary<string, decimal> { ["messaging_conversation_started_7d"] = 8m },
            new Dictionary<string, decimal> { ["purchase"] = 180m });
        var snapshot = new InsightsSnapshot
        {
            Spend = row.Spend, Impressions = row.Impressions, Clicks = row.Clicks, Frequency = row.Frequency,
            ProviderActionsJson = "{\"Actions\":{\"messaging_conversation_started_7d\":8}}",
            ProviderActionValuesJson = "{\"ActionValues\":{\"purchase\":180}}"
        };

        Assert.Equal(MetaInsightRevisionPolicy.Fingerprint(row), MetaInsightRevisionPolicy.Fingerprint(snapshot));
        Assert.NotEqual(MetaInsightRevisionPolicy.Fingerprint(row),
            MetaInsightRevisionPolicy.Fingerprint(row with { Spend = 44m }));
    }
}
