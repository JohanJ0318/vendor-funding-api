-- Reporting queries. Run in SSMS, Azure Data Studio, or sqlcmd after the seed.
-- Each one is the kind of question a finance team asks.
USE VendorFunding;
GO

DECLARE @AsOf DATE = '2026-06-30';

-- 1. Accounts-receivable aging: unpaid vendor claims by how long they have been open.
--    The API's /reports/aging endpoint runs this same query.
SELECT
    b.Bucket,
    COUNT(c.ClaimId)                                         AS ClaimCount,
    COALESCE(SUM(c.AmountBilled - c.AmountCollected), 0)     AS Outstanding
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

-- 2. Balance by vendor: what each vendor has been billed, has paid, and still owes.
SELECT
    v.VendorNumber,
    v.Name,
    SUM(c.AmountBilled)                      AS TotalBilled,
    SUM(c.AmountCollected)                   AS TotalCollected,
    SUM(c.AmountBilled - c.AmountCollected)  AS Outstanding
FROM dbo.Vendor v
JOIN dbo.FundingAgreement a ON a.VendorId = v.VendorId
JOIN dbo.Claim c            ON c.AgreementId = a.AgreementId
GROUP BY v.VendorNumber, v.Name
HAVING SUM(c.AmountBilled - c.AmountCollected) > 0
ORDER BY Outstanding DESC;

-- 3. Running total of units purchased under the billback deal (window function).
SELECT
    ActivityDate,
    ItemNumber,
    UnitsPurchased,
    SUM(UnitsPurchased) OVER (ORDER BY ActivityDate, ItemNumber
                              ROWS UNBOUNDED PRECEDING) AS RunningUnits
FROM dbo.ItemActivity
WHERE AgreementId = 1
  AND ActivityDate BETWEEN '2026-03-01' AND '2026-03-31'
ORDER BY ActivityDate, ItemNumber;

-- 4. Data-quality check: activity recorded outside its agreement's window.
--    These rows exist in the source data but must never be paid on.
SELECT ia.AgreementId, ia.ActivityDate, ia.ItemNumber, a.StartDate, a.EndDate
FROM dbo.ItemActivity ia
JOIN dbo.FundingAgreement a ON a.AgreementId = ia.AgreementId
WHERE ia.ActivityDate NOT BETWEEN a.StartDate AND a.EndDate;
GO
