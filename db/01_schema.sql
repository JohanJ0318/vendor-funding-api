-- Vendor funding schema for SQL Server (T-SQL).
-- Run order: 01_schema.sql, 02_seed.sql. Safe to re-run: it drops and recreates.

IF DB_ID('VendorFunding') IS NULL
    CREATE DATABASE VendorFunding;
GO

USE VendorFunding;
GO

DROP TABLE IF EXISTS dbo.Claim;
DROP TABLE IF EXISTS dbo.ItemActivity;
DROP TABLE IF EXISTS dbo.FundingAgreement;
DROP TABLE IF EXISTS dbo.Vendor;
GO

CREATE TABLE dbo.Vendor
(
    VendorId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Vendor PRIMARY KEY,
    VendorNumber VARCHAR(20)       NOT NULL CONSTRAINT UQ_Vendor_Number UNIQUE,
    Name         NVARCHAR(200)     NOT NULL
);

CREATE TABLE dbo.FundingAgreement
(
    AgreementId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FundingAgreement PRIMARY KEY,
    VendorId      INT               NOT NULL CONSTRAINT FK_Agreement_Vendor REFERENCES dbo.Vendor(VendorId),
    Description   NVARCHAR(200)     NOT NULL,
    FundingType   TINYINT           NOT NULL,          -- 1 Billback, 2 ScanBased, 3 LumpSum, 4 OffInvoice
    StartDate     DATE              NOT NULL,
    EndDate       DATE              NOT NULL,
    RatePerUnit   DECIMAL(19,4)     NOT NULL CONSTRAINT DF_Agreement_Rate DEFAULT 0,
    LumpSumAmount DECIMAL(19,4)     NOT NULL CONSTRAINT DF_Agreement_Lump DEFAULT 0,
    MaxPayout     DECIMAL(19,4)     NULL,
    PercentOff    DECIMAL(9,4)      NOT NULL CONSTRAINT DF_Agreement_Percent DEFAULT 0,  -- 10 = 10% off unit cost
    CONSTRAINT CK_Agreement_Type    CHECK (FundingType IN (1, 2, 3, 4)),
    CONSTRAINT CK_Agreement_Percent CHECK (PercentOff BETWEEN 0 AND 100),
    CONSTRAINT CK_Agreement_Dates CHECK (EndDate >= StartDate)
);

CREATE TABLE dbo.ItemActivity
(
    AgreementId    INT         NOT NULL CONSTRAINT FK_Activity_Agreement REFERENCES dbo.FundingAgreement(AgreementId),
    ActivityDate   DATE        NOT NULL,
    ItemNumber     VARCHAR(20) NOT NULL,
    UnitsPurchased INT         NOT NULL CONSTRAINT DF_Activity_Purchased DEFAULT 0,
    UnitsSold      INT         NOT NULL CONSTRAINT DF_Activity_Sold DEFAULT 0,
    UnitCost       DECIMAL(19,4) NOT NULL CONSTRAINT DF_Activity_Cost DEFAULT 0,
    CONSTRAINT PK_ItemActivity PRIMARY KEY (AgreementId, ActivityDate, ItemNumber)
);

CREATE TABLE dbo.Claim
(
    ClaimId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Claim PRIMARY KEY,
    AgreementId     INT               NOT NULL CONSTRAINT FK_Claim_Agreement REFERENCES dbo.FundingAgreement(AgreementId),
    PeriodStart     DATE              NOT NULL,
    PeriodEnd       DATE              NOT NULL,
    AmountBilled    DECIMAL(19,4)     NOT NULL,
    AmountCollected DECIMAL(19,4)     NOT NULL CONSTRAINT DF_Claim_Collected DEFAULT 0,
    BilledDate      DATE              NOT NULL,
    CONSTRAINT CK_Claim_Amounts CHECK (AmountBilled >= 0 AND AmountCollected >= 0)
);

-- The aging report filters on unpaid claims by billed date; this index covers it.
CREATE INDEX IX_Claim_BilledDate ON dbo.Claim (BilledDate) INCLUDE (AmountBilled, AmountCollected);
GO
