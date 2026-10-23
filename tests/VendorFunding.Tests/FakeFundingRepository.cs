using VendorFunding.Core;

namespace VendorFunding.Tests;

/// <summary>In-memory stand-in for SQL Server, used by the API tests.</summary>
public sealed class FakeFundingRepository : IFundingRepository
{
    public List<Vendor> Vendors { get; } = new();
    public List<FundingAgreement> Agreements { get; } = new();
    public Dictionary<int, List<ItemActivity>> Activity { get; } = new();
    public List<Claim> Claims { get; } = new();

    public Task<IReadOnlyList<Vendor>> GetVendorsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Vendor>>(Vendors);

    public Task<FundingAgreement?> GetAgreementAsync(int agreementId, CancellationToken ct = default) =>
        Task.FromResult(Agreements.FirstOrDefault(a => a.AgreementId == agreementId));

    public Task<IReadOnlyList<ItemActivity>> GetActivityAsync(int agreementId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = Activity.TryGetValue(agreementId, out var list)
            ? list.Where(a => a.ActivityDate >= from && a.ActivityDate <= to).ToList()
            : new List<ItemActivity>();
        return Task.FromResult<IReadOnlyList<ItemActivity>>(rows);
    }

    public Task<IReadOnlyList<Claim>> GetClaimsAsync(int agreementId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Claim>>(Claims.Where(c => c.AgreementId == agreementId).ToList());

    public Task<int> InsertClaimAsync(int agreementId, DateOnly periodStart, DateOnly periodEnd, decimal amount, DateOnly billedDate, CancellationToken ct = default)
    {
        var id = Claims.Count + 1;
        Claims.Add(new Claim(id, agreementId, periodStart, periodEnd, amount, 0m, billedDate));
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<AgingBucket>> GetAgingReportAsync(DateOnly asOf, CancellationToken ct = default) =>
        Task.FromResult(ClaimAging.Bucket(Claims, asOf));
}
