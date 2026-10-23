namespace VendorFunding.Core;

/// <summary>
/// Everything the API needs from the database. The API depends on this interface,
/// not on SQL Server, which keeps the business logic testable without a database.
/// </summary>
public interface IFundingRepository
{
    Task<IReadOnlyList<Vendor>> GetVendorsAsync(CancellationToken ct = default);
    Task<FundingAgreement?> GetAgreementAsync(int agreementId, CancellationToken ct = default);
    Task<IReadOnlyList<ItemActivity>> GetActivityAsync(int agreementId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<Claim>> GetClaimsAsync(int agreementId, CancellationToken ct = default);
    Task<int> InsertClaimAsync(int agreementId, DateOnly periodStart, DateOnly periodEnd, decimal amount, DateOnly billedDate, CancellationToken ct = default);
    Task<IReadOnlyList<AgingBucket>> GetAgingReportAsync(DateOnly asOf, CancellationToken ct = default);
}
