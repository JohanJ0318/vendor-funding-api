namespace VendorFunding.Core;

/// <summary>
/// How a vendor pays the retailer back for promoting or stocking its products.
/// </summary>
public enum FundingType
{
    /// <summary>
    /// The vendor pays a fixed amount per unit the retailer BUYS during the window.
    /// The retailer bills the vendor after the fact.
    /// </summary>
    Billback = 1,

    /// <summary>
    /// The vendor pays a fixed amount per unit the retailer SELLS at the register
    /// during the window (also called scan-down or scan-based funding).
    /// </summary>
    ScanBased = 2,

    /// <summary>
    /// The vendor pays one fixed amount for the whole agreement (for example an
    /// end-cap display fee). It is earned evenly, day by day, across the window.
    /// </summary>
    LumpSum = 3,
}

public sealed record Vendor(int VendorId, string VendorNumber, string Name);

/// <summary>
/// A deal with a vendor. StartDate and EndDate are both inclusive.
/// </summary>
public sealed record FundingAgreement(
    int AgreementId,
    int VendorId,
    string Description,
    FundingType Type,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RatePerUnit,      // used by Billback and ScanBased
    decimal LumpSumAmount,    // used by LumpSum
    decimal? MaxPayout);      // optional cap on what the vendor will ever pay

/// <summary>
/// One day of activity for one item covered by an agreement.
/// </summary>
public sealed record ItemActivity(
    DateOnly ActivityDate,
    string ItemNumber,
    int UnitsPurchased,
    int UnitsSold);

/// <summary>
/// A bill sent to a vendor for funding earned in a period.
/// </summary>
public sealed record Claim(
    int ClaimId,
    int AgreementId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal AmountBilled,
    decimal AmountCollected,
    DateOnly BilledDate)
{
    public decimal OutstandingBalance => AmountBilled - AmountCollected;
}

public sealed record AccrualResult(
    int AgreementId,
    DateOnly AsOf,
    decimal Earned,
    bool CapApplied);

public sealed record AgingBucket(string Label, int ClaimCount, decimal Outstanding);
