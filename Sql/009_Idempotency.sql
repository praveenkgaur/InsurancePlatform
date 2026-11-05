USE DemoDataBase;
GO

IF OBJECT_ID('dbo.IdempotencyRecord', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.IdempotencyRecord
    (
        IdempotencyKey NVARCHAR(64) NOT NULL PRIMARY KEY,
        HttpMethod NVARCHAR(10) NOT NULL,
        RequestPath NVARCHAR(256) NOT NULL,
        RequestHash NVARCHAR(64) NOT NULL,
        StatusCode INT NOT NULL,
        ResponseBody NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_IdempotencyRecord_CreatedUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE INDEX IX_IdempotencyRecord_CreatedUtc ON dbo.IdempotencyRecord (CreatedUtc);
END
GO
