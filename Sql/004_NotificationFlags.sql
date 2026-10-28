USE DemoDataBase;
GO

IF COL_LENGTH('dbo.InsuranceNotification', 'IsRead') IS NULL
    ALTER TABLE dbo.InsuranceNotification ADD IsRead BIT NOT NULL CONSTRAINT DF_Notif_IsRead DEFAULT (0);
IF COL_LENGTH('dbo.InsuranceNotification', 'IsDeleted') IS NULL
    ALTER TABLE dbo.InsuranceNotification ADD IsDeleted BIT NOT NULL CONSTRAINT DF_Notif_IsDeleted DEFAULT (0);
IF COL_LENGTH('dbo.InsuranceNotification', 'Active') IS NULL
    ALTER TABLE dbo.InsuranceNotification ADD Active BIT NOT NULL CONSTRAINT DF_Notif_Active DEFAULT (1);
IF COL_LENGTH('dbo.InsuranceNotification', 'ReadUtc') IS NULL
    ALTER TABLE dbo.InsuranceNotification ADD ReadUtc DATETIME2 NULL;
IF COL_LENGTH('dbo.InsuranceNotification', 'DeletedUtc') IS NULL
    ALTER TABLE dbo.InsuranceNotification ADD DeletedUtc DATETIME2 NULL;
GO
