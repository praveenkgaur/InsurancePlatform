USE DemoDataBase;
GO

IF OBJECT_ID('dbo.RolePermissions', 'U') IS NOT NULL DROP TABLE dbo.RolePermissions;
IF OBJECT_ID('dbo.UserRoles', 'U') IS NOT NULL DROP TABLE dbo.UserRoles;
IF OBJECT_ID('dbo.Sessions', 'U') IS NOT NULL DROP TABLE dbo.Sessions;
IF OBJECT_ID('dbo.Permissions', 'U') IS NOT NULL DROP TABLE dbo.Permissions;
IF OBJECT_ID('dbo.Roles', 'U') IS NOT NULL DROP TABLE dbo.Roles;
IF OBJECT_ID('dbo.Users', 'U') IS NOT NULL DROP TABLE dbo.Users;
IF OBJECT_ID('dbo.InsuranceClaims', 'U') IS NOT NULL DROP TABLE dbo.InsuranceClaims;
IF OBJECT_ID('dbo.InsurancePolicies', 'U') IS NOT NULL DROP TABLE dbo.InsurancePolicies;
IF OBJECT_ID('dbo.Invoices', 'U') IS NOT NULL DROP TABLE dbo.Invoices;
GO

CREATE TABLE dbo.Users
(
    UserId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserName NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(500) NOT NULL,
    DisplayName NVARCHAR(200) NOT NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1)
);

CREATE TABLE dbo.Roles
(
    RoleId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RoleName NVARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE dbo.Permissions
(
    PermissionId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PermissionKey NVARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE dbo.UserRoles
(
    UserId INT NOT NULL,
    RoleId INT NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId),
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (RoleId)
);

CREATE TABLE dbo.RolePermissions
(
    RoleId INT NOT NULL,
    PermissionId INT NOT NULL,
    CONSTRAINT PK_RolePermissions PRIMARY KEY (RoleId, PermissionId),
    CONSTRAINT FK_RolePermissions_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles (RoleId),
    CONSTRAINT FK_RolePermissions_Permissions FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions (PermissionId)
);

CREATE TABLE dbo.Sessions
(
    SessionId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    UserId INT NOT NULL,
    SessionTokenHash NVARCHAR(200) NOT NULL,
    AccessToken NVARCHAR(MAX) NOT NULL,
    RefreshToken NVARCHAR(MAX) NOT NULL,
    ExpiresUtc DATETIME2 NOT NULL,
    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_Sessions_CreatedUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT FK_Sessions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId)
);

CREATE TABLE dbo.InsuranceClaims
(
    ClaimId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ClaimNumber NVARCHAR(50) NOT NULL UNIQUE,
    PolicyNumber NVARCHAR(50) NOT NULL,
    ClaimantName NVARCHAR(200) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(30) NOT NULL,
    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_Claims_CreatedUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.InsurancePolicies
(
    PolicyId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PolicyNumber NVARCHAR(50) NOT NULL UNIQUE,
    HolderName NVARCHAR(200) NOT NULL,
    ProductName NVARCHAR(200) NOT NULL,
    Premium DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(30) NOT NULL,
    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_Policies_CreatedUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.Invoices
(
    InvoiceId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    InvoiceNumber NVARCHAR(50) NOT NULL UNIQUE,
    PolicyNumber NVARCHAR(50) NOT NULL,
    Amount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(30) NOT NULL,
    DueDate DATE NOT NULL,
    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_Invoices_CreatedUtc DEFAULT (SYSUTCDATETIME())
);
GO

INSERT INTO dbo.Roles (RoleName) VALUES ('Admin'), ('ClaimsAdjuster'), ('PolicyAdmin'), ('BillingClerk');
INSERT INTO dbo.Permissions (PermissionKey) VALUES
    ('Claims.View'), ('Claims.Approve'), ('Claims.Delete'),
    ('Policies.View'), ('Policies.Create'), ('Policies.Update'),
    ('Billing.View');

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Roles r
CROSS JOIN dbo.Permissions p
WHERE r.RoleName = 'Admin';

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Roles r
JOIN dbo.Permissions p ON p.PermissionKey IN ('Claims.View', 'Claims.Approve')
WHERE r.RoleName = 'ClaimsAdjuster';

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Roles r
JOIN dbo.Permissions p ON p.PermissionKey IN ('Policies.View', 'Policies.Create', 'Policies.Update')
WHERE r.RoleName = 'PolicyAdmin';

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT r.RoleId, p.PermissionId
FROM dbo.Roles r
JOIN dbo.Permissions p ON p.PermissionKey = 'Billing.View'
WHERE r.RoleName = 'BillingClerk';

INSERT INTO dbo.InsuranceClaims (ClaimNumber, PolicyNumber, ClaimantName, Amount, Status)
VALUES
    ('CLM-1001', 'POL-2001', 'Anita Sharma', 18500.00, 'Open'),
    ('CLM-1002', 'POL-2002', 'Rahul Mehta', 42000.00, 'Open'),
    ('CLM-1003', 'POL-2001', 'Anita Sharma', 9500.00, 'Approved');

INSERT INTO dbo.InsurancePolicies (PolicyNumber, HolderName, ProductName, Premium, Status)
VALUES
    ('POL-2001', 'Anita Sharma', 'Health Protect Plus', 12400.00, 'Active'),
    ('POL-2002', 'Rahul Mehta', 'Motor Comprehensive', 8600.00, 'Active'),
    ('POL-2003', 'Neha Kapoor', 'Term Life Secure', 15600.00, 'Active');

INSERT INTO dbo.Invoices (InvoiceNumber, PolicyNumber, Amount, Status, DueDate)
VALUES
    ('INV-3001', 'POL-2001', 12400.00, 'Paid', '2026-09-15'),
    ('INV-3002', 'POL-2002', 8600.00, 'Due', '2026-10-30'),
    ('INV-3003', 'POL-2003', 15600.00, 'Due', '2026-11-12');
GO
