USE DemoDataBase;
GO

IF COL_LENGTH('dbo.InsuranceClaimants', 'FirstName') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD FirstName NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'LastName') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD LastName NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'DateOfBirth') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD DateOfBirth DATE NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'Gender') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD Gender NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'AlternatePhone') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD AlternatePhone NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'AddressLine1') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD AddressLine1 NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'AddressLine2') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD AddressLine2 NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'City') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD City NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'StateName') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD StateName NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'PinCode') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD PinCode NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'Country') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD Country NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'IdType') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD IdType NVARCHAR(30) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'IdNumber') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD IdNumber NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.InsuranceClaimants', 'Notes') IS NULL
    ALTER TABLE dbo.InsuranceClaimants ADD Notes NVARCHAR(500) NULL;
GO

UPDATE dbo.InsuranceClaimants
SET FirstName = FullName
WHERE FirstName IS NULL AND FullName IS NOT NULL;
GO
