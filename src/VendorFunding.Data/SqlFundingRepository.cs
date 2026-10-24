using Microsoft.Data.SqlClient;
using VendorFunding.Core;

namespace VendorFunding.Data;

/// <summary>
/// SQL Server implementation of IFundingRepository using plain ADO.NET.
/// Every query is hand-written and parameterized (no string concatenation),
/// which is what prevents SQL injection.
/// </summary>
public sealed class SqlFundingRepository : IFundingRepository
{
    private readonly string _connectionString;

    public SqlFundingRepository(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<Vendor>> GetVendorsAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT VendorId, VendorNumber, Name FROM dbo.Vendor ORDER BY VendorNumber;";

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var vendors = new List<Vendor>();
        while (await reader.ReadAsync(ct))
            vendors.Add(new Vendor(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        return vendors;
    }

    public async Task<FundingAgreement?> GetAgreementAsync(int agreementId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT AgreementId, VendorId, Description, FundingType, StartDate, EndDate,
                   RatePerUnit, LumpSumAmount, MaxPayout, PercentOff
            FROM dbo.FundingAgreement
            WHERE AgreementId = @AgreementId;
            """;

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@AgreementId", agreementId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return null;

        return new FundingAgreement(
            AgreementId: reader.GetInt32(0),
            VendorId: reader.GetInt32(1),
            Description: reader.GetString(2),
            Type: (FundingType)reader.GetByte(3),
            StartDate: DateOnly.FromDateTime(reader.GetDateTime(4)),
            EndDate: DateOnly.FromDateTime(reader.GetDateTime(5)),
            RatePerUnit: reader.GetDecimal(6),
            LumpSumAmount: reader.GetDecimal(7),
            MaxPayout: reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            PercentOff: reader.GetDecimal(9));
    }

    public async Task<IReadOnlyList<ItemActivity>> GetActivityAsync(
        int agreementId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT ActivityDate, ItemNumber, UnitsPurchased, UnitsSold, UnitCost
            FROM dbo.ItemActivity
            WHERE AgreementId = @AgreementId
              AND ActivityDate BETWEEN @From AND @To
            ORDER BY ActivityDate, ItemNumber;
            """;

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@AgreementId", agreementId);
        cmd.Parameters.AddWithValue("@From", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@To", to.ToDateTime(TimeOnly.MinValue));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var rows = new List<ItemActivity>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ItemActivity(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetDecimal(4)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<Claim>> GetClaimsAsync(int agreementId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT ClaimId, AgreementId, PeriodStart, PeriodEnd, AmountBilled, AmountCollected, BilledDate
            FROM dbo.Claim
            WHERE AgreementId = @AgreementId
            ORDER BY PeriodStart;
            """;

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@AgreementId", agreementId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var claims = new List<Claim>();
        while (await reader.ReadAsync(ct))
        {
            claims.Add(new Claim(
                reader.GetInt32(0),
                reader.GetInt32(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                DateOnly.FromDateTime(reader.GetDateTime(6))));
        }
        return claims;
    }

    public async Task<int> InsertClaimAsync(
        int agreementId, DateOnly periodStart, DateOnly periodEnd,
        decimal amount, DateOnly billedDate, CancellationToken ct = default)
    {
        // OUTPUT INSERTED.ClaimId returns the new identity value in the same round trip.
        const string sql = """
            INSERT INTO dbo.Claim (AgreementId, PeriodStart, PeriodEnd, AmountBilled, BilledDate)
            OUTPUT INSERTED.ClaimId
            VALUES (@AgreementId, @PeriodStart, @PeriodEnd, @Amount, @BilledDate);
            """;

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@AgreementId", agreementId);
        cmd.Parameters.AddWithValue("@PeriodStart", periodStart.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@PeriodEnd", periodEnd.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.Add(new SqlParameter("@Amount", System.Data.SqlDbType.Decimal)
        {
            Precision = 19,
            Scale = 4,
            Value = amount,
        });
        cmd.Parameters.AddWithValue("@BilledDate", billedDate.ToDateTime(TimeOnly.MinValue));

        var id = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(id);
    }

    public async Task<IReadOnlyList<AgingBucket>> GetAgingReportAsync(DateOnly asOf, CancellationToken ct = default)
    {
        // Same query as report 1 in db/03_reports.sql.
        const string sql = """
            SELECT b.Bucket,
                   COUNT(c.ClaimId) AS ClaimCount,
                   COALESCE(SUM(c.AmountBilled - c.AmountCollected), 0) AS Outstanding
            FROM (VALUES (1, '0-30'), (2, '31-60'), (3, '61-90'), (4, '90+')) AS b(SortOrder, Bucket)
            LEFT JOIN
            (
                SELECT ClaimId, AmountBilled, AmountCollected,
                       CASE
                           WHEN DATEDIFF(DAY, BilledDate, @AsOf) <= 30 THEN '0-30'
                           WHEN DATEDIFF(DAY, BilledDate, @AsOf) <= 60 THEN '31-60'
                           WHEN DATEDIFF(DAY, BilledDate, @AsOf) <= 90 THEN '61-90'
                           ELSE '90+'
                       END AS Bucket
                FROM dbo.Claim
                WHERE AmountBilled > AmountCollected
            ) AS c ON c.Bucket = b.Bucket
            GROUP BY b.SortOrder, b.Bucket
            ORDER BY b.SortOrder;
            """;

        await using var conn = await OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@AsOf", asOf.ToDateTime(TimeOnly.MinValue));
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var buckets = new List<AgingBucket>();
        while (await reader.ReadAsync(ct))
            buckets.Add(new AgingBucket(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2)));
        return buckets;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
