namespace VendorFunding.Core;

/// <summary>
/// Works out how much funding a vendor owes under an agreement up to a given date.
/// Pure logic: no database, no clock, so every rule is easy to unit test.
/// </summary>
public static class AccrualCalculator
{
    public static AccrualResult CalculateEarned(
        FundingAgreement agreement,
        IEnumerable<ItemActivity> activity,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(agreement);
        ArgumentNullException.ThrowIfNull(activity);

        if (agreement.EndDate < agreement.StartDate)
            throw new ArgumentException("Agreement ends before it starts.", nameof(agreement));

        // Nothing is earned before the deal starts.
        if (asOf < agreement.StartDate)
            return new AccrualResult(agreement.AgreementId, asOf, 0m, false);

        // Count activity only inside the window AND on or before the as-of date.
        var lastCountedDay = asOf < agreement.EndDate ? asOf : agreement.EndDate;

        decimal earned = agreement.Type switch
        {
            FundingType.Billback => agreement.RatePerUnit * activity
                .Where(a => InRange(a.ActivityDate, agreement.StartDate, lastCountedDay))
                .Sum(a => (long)a.UnitsPurchased),

            FundingType.ScanBased => agreement.RatePerUnit * activity
                .Where(a => InRange(a.ActivityDate, agreement.StartDate, lastCountedDay))
                .Sum(a => (long)a.UnitsSold),

            FundingType.LumpSum => LumpSumEarned(agreement, lastCountedDay),

            _ => throw new NotSupportedException($"Unknown funding type {agreement.Type}."),
        };

        earned = RoundToCents(earned);

        var capApplied = false;
        // Round the cap too: it comes from a DECIMAL(19,4) column, and an amount
        // owed should always be whole cents (40.00, not 40.0000).
        if (agreement.MaxPayout is decimal cap && earned > cap)
        {
            earned = RoundToCents(cap);
            capApplied = true;
        }

        return new AccrualResult(agreement.AgreementId, asOf, earned, capApplied);
    }

    /// <summary>
    /// What to put on the next claim: everything earned so far, minus what has
    /// already been billed. Never negative (if we over-billed, that is a manual
    /// credit, not a negative claim).
    /// </summary>
    public static decimal AmountToBill(decimal earnedToDate, IEnumerable<Claim> priorClaims)
    {
        var alreadyBilled = priorClaims.Sum(c => c.AmountBilled);
        var due = earnedToDate - alreadyBilled;
        return due > 0 ? due : 0m;
    }

    private static decimal LumpSumEarned(FundingAgreement agreement, DateOnly lastCountedDay)
    {
        var totalDays = agreement.EndDate.DayNumber - agreement.StartDate.DayNumber + 1;
        var daysElapsed = lastCountedDay.DayNumber - agreement.StartDate.DayNumber + 1;

        // Last day: return the exact amount so rounding never leaves a penny behind.
        if (daysElapsed >= totalDays)
            return agreement.LumpSumAmount;

        return agreement.LumpSumAmount * daysElapsed / totalDays;
    }

    private static bool InRange(DateOnly day, DateOnly start, DateOnly end) =>
        day >= start && day <= end;

    // Money is rounded half away from zero (2.345 -> 2.35), the convention most
    // people expect on an invoice. .NET's default is banker's rounding (2.345 -> 2.34).
    public static decimal RoundToCents(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
