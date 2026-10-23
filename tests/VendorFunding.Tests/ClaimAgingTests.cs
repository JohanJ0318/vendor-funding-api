using VendorFunding.Core;

namespace VendorFunding.Tests;

public class ClaimAgingTests
{
    private static readonly DateOnly AsOf = new(2026, 6, 30);

    private static Claim Billed(DateOnly billedDate, decimal billed, decimal collected = 0m) =>
        new(0, 1, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), billed, collected, billedDate);

    [Theory]
    [InlineData(0, "0-30")]
    [InlineData(30, "0-30")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "61-90")]
    [InlineData(90, "61-90")]
    [InlineData(91, "90+")]
    public void Bucket_boundaries_are_inclusive_on_the_upper_end(int days, string expected)
    {
        Assert.Equal(expected, ClaimAging.LabelFor(days));
    }

    [Fact]
    public void Matches_the_seed_data_report()
    {
        // Same claims as db/02_seed.sql, so this agrees with the SQL aging query.
        var claims = new[]
        {
            Billed(new DateOnly(2026, 3, 20), 100m, 100m),   // paid: excluded
            Billed(new DateOnly(2026, 6, 15), 30m),          // 15 days
            Billed(new DateOnly(2026, 4, 20), 40m, 10m),     // 71 days, $30 left
            Billed(new DateOnly(2026, 3, 5), 3100m),         // 117 days
        };

        var report = ClaimAging.Bucket(claims, AsOf);

        Assert.Equal(new[] { "0-30", "31-60", "61-90", "90+" }, report.Select(b => b.Label));
        Assert.Equal(new AgingBucket("0-30", 1, 30m), report[0]);
        Assert.Equal(new AgingBucket("31-60", 0, 0m), report[1]);
        Assert.Equal(new AgingBucket("61-90", 1, 30m), report[2]);
        Assert.Equal(new AgingBucket("90+", 1, 3100m), report[3]);
    }
}
