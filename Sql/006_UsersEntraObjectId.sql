USE DemoDataBase;
GO

IF COL_LENGTH('dbo.Users', 'EntraObjectId') IS NULL
    ALTER TABLE dbo.Users ADD EntraObjectId UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_Users_EntraObjectId' AND object_id = OBJECT_ID('dbo.Users'))
    CREATE UNIQUE INDEX UX_Users_EntraObjectId
        ON dbo.Users(EntraObjectId)
        WHERE EntraObjectId IS NOT NULL;
GO
