-- Small, made-up data set. Vendor names are fictional.
USE VendorFunding;
GO

INSERT INTO dbo.Vendor (VendorNumber, Name) VALUES
    ('V1001', N'Sunrise Dairy Co.'),
    ('V1002', N'Gulf Coast Snacks'),
    ('V1003', N'Orchard Grove Juices');

-- 1: billback, $0.50 per unit purchased, March 2026
-- 2: scan-based, $0.25 per unit sold, March 2026, capped at $40
-- 3: lump sum, $3,100 end-cap display fee, March 2026 (31 days -> $100/day)
INSERT INTO dbo.FundingAgreement
    (VendorId, Description, FundingType, StartDate, EndDate, RatePerUnit, LumpSumAmount, MaxPayout)
VALUES
    (1, N'Spring milk billback',      1, '2026-03-01', '2026-03-31', 0.50, 0,    NULL),
    (2, N'Chips scan-down promotion', 2, '2026-03-01', '2026-03-31', 0.25, 0,    40.00),
    (3, N'End-cap display fee',       3, '2026-03-01', '2026-03-31', 0,    3100, NULL);

INSERT INTO dbo.ItemActivity (AgreementId, ActivityDate, ItemNumber, UnitsPurchased, UnitsSold) VALUES
    (1, '2026-02-28', 'MILK-GAL', 999, 0),   -- before the window: must not count
    (1, '2026-03-02', 'MILK-GAL', 120, 0),
    (1, '2026-03-09', 'MILK-GAL',  80, 0),
    (1, '2026-03-16', 'MILK-HG',   60, 0),
    (2, '2026-03-05', 'CHIP-REG',   0, 90),
    (2, '2026-03-12', 'CHIP-REG',   0, 70),  -- 160 sold x $0.25 = $40, right at the cap
    (2, '2026-03-20', 'CHIP-BBQ',   0, 40);  -- pushes past the cap

-- Claims in different aging buckets as of 2026-06-30.
INSERT INTO dbo.Claim (AgreementId, PeriodStart, PeriodEnd, AmountBilled, AmountCollected, BilledDate) VALUES
    (1, '2026-03-01', '2026-03-15', 100.00, 100.00, '2026-03-20'),  -- paid in full
    (1, '2026-03-16', '2026-03-31',  30.00,   0.00, '2026-06-15'),  -- 15 days
    (2, '2026-03-01', '2026-03-31',  40.00,  10.00, '2026-04-20'),  -- 71 days, partly paid
    (3, '2026-03-01', '2026-03-31', 3100.00,  0.00, '2026-03-05');  -- 117 days
GO
