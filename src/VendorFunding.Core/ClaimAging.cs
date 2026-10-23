namespace VendorFunding.Core;

/// <summary>
/// Groups unpaid claims by how long they have been outstanding, the same way an
/// accounts receivable aging report does. The SQL report in db/queries.sql does
/// this in the database; this version exists so the rule is unit tested.
/// </summary>
public static class ClaimAging
{
    public static readonly string[] Labels = { "0-30", "31-60", "61-90", "90+" };

    public static IReadOnlyList<AgingBucket> Bucket(IEnumerable<Claim> claims, DateOnly asOf)
    {
        var groups = claims
            .Where(c => c.OutstandingBalance > 0)
            .GroupBy(c => LabelFor(asOf.DayNumber - c.BilledDate.DayNumber))
            .ToDictionary(g => g.Key, g => g.ToList());

        return Labels
            .Select(label => groups.TryGetValue(label, out var list)
                ? new AgingBucket(label, list.Count, list.Sum(c => c.OutstandingBalance))
                : new AgingBucket(label, 0, 0m))
            .ToList();
    }

    public static string LabelFor(int daysOutstanding) => daysOutstanding switch
    {
        <= 30 => "0-30",
        <= 60 => "31-60",
        <= 90 => "61-90",
        _ => "90+",
    };
}
