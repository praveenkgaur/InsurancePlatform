USE DemoDataBase;
GO

IF OBJECT_ID('dbo.InsuranceClaimants', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.InsuranceClaimants
    (
        ClaimantId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FullName NVARCHAR(200) NOT NULL,
        Email NVARCHAR(200) NULL,
        Phone NVARCHAR(50) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_Claimants_CreatedUtc DEFAULT (SYSUTCDATETIME())
    );
END
GO

INSERT INTO dbo.InsuranceClaimants (FullName)
SELECT DISTINCT c.ClaimantName
FROM dbo.InsuranceClaims c
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.InsuranceClaimants x WHERE x.FullName = c.ClaimantName
);
GO
