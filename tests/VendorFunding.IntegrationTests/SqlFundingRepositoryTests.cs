using VendorFunding.Core;
using VendorFunding.Data;

namespace VendorFunding.IntegrationTests;

/// <summary>
/// Runs the real SQL against a real SQL Server loaded with db/01_schema.sql and
/// db/02_seed.sql. CI starts SQL Server in a container; locally, use docker compose.
/// Connection string comes from the VENDORFUNDING_TEST_DB environment variable.
/// </summary>
public class SqlFundingRepositoryTests
{
    private static SqlFundingRepository Repo()
    {
        var cs = Environment.GetEnvironmentVariable("VENDORFUNDING_TEST_DB")
            ?? "Server=localhost,1433;Database=VendorFunding;User Id=sa;Password=Local_Dev_Pass1;TrustServerCertificate=True";
        return new SqlFundingRepository(cs);
    }

    [Fact]
    public async Task Reads_seeded_vendors_in_vendor_number_order()
    {
        var vendors = await Repo().GetVendorsAsync();

        Assert.Equal(new[] { "V1001", "V1002", "V1003" }, vendors.Select(v => v.VendorNumber));
    }

    [Fact]
    public async Task Maps_agreement_columns_including_null_cap()
    {
        var repo = Repo();

        var billback = await repo.GetAgreementAsync(1);
        var scan = await repo.GetAgreementAsync(2);

        Assert.NotNull(billback);
        Assert.Equal(FundingType.Billback, billback!.Type);
        Assert.Equal(new DateOnly(2026, 3, 1), billback.StartDate);
        Assert.Null(billback.MaxPayout);
        Assert.Equal(40m, scan!.MaxPayout);
    }

    [Fact]
    public async Task Missing_agreement_returns_null()
    {
        Assert.Null(await Repo().GetAgreementAsync(999));
    }

    [Fact]
    public async Task Activity_query_respects_the_date_range()
    {
        var rows = await Repo().GetActivityAsync(1, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));

        Assert.Equal(3, rows.Count);                        // the Feb 28 row is excluded
        Assert.Equal(260, rows.Sum(r => r.UnitsPurchased));
    }

    [Fact]
    public async Task Seeded_billback_accrues_to_130_dollars_end_to_end()
    {
        var repo = Repo();
        var agreement = (await repo.GetAgreementAsync(1))!;
        var activity = await repo.GetActivityAsync(1, agreement.StartDate, agreement.EndDate);

        var result = AccrualCalculator.CalculateEarned(agreement, activity, agreement.EndDate);

        Assert.Equal(130.00m, result.Earned);
    }

    [Fact]
    public async Task Seeded_off_invoice_accrues_to_52_85_end_to_end()
    {
        var repo = Repo();
        var agreement = (await repo.GetAgreementAsync(4))!;
        var activity = await repo.GetActivityAsync(4, agreement.StartDate, agreement.EndDate);

        var result = AccrualCalculator.CalculateEarned(agreement, activity, agreement.EndDate);

        Assert.Equal(FundingType.OffInvoice, agreement.Type);
        Assert.Equal(10m, agreement.PercentOff);
        Assert.Equal(2, activity.Count);                    // the April 2 row is excluded
        Assert.Equal(3.59m, activity[1].UnitCost);
        Assert.Equal(52.85m, result.Earned);                // 10% of $528.50
    }

    [Fact]
    public async Task Sql_aging_report_matches_the_csharp_rule()
    {
        var asOf = new DateOnly(2026, 6, 30);
        var repo = Repo();

        var fromSql = await repo.GetAgingReportAsync(asOf);

        // Build the same report in C# from the raw claims and compare.
        var allClaims = new List<Claim>();
        for (var id = 1; id <= 3; id++)
            allClaims.AddRange(await repo.GetClaimsAsync(id));
        var fromCSharp = ClaimAging.Bucket(allClaims, asOf);

        Assert.Equal(fromCSharp, fromSql);
    }
}
