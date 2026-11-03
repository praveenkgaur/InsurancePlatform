USE DemoDataBase;
GO

IF OBJECT_ID('dbo.FunctionClaimantLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FunctionClaimantLog
    (
        LogId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LoggedUtc DATETIME2 NOT NULL CONSTRAINT DF_FunctionClaimantLog_LoggedUtc DEFAULT (SYSUTCDATETIME()),
        FunctionName NVARCHAR(100) NOT NULL,
        HttpMethod NVARCHAR(10) NOT NULL,
        ClaimantId INT NULL,
        FullName NVARCHAR(200) NULL,
        City NVARCHAR(100) NULL,
        PinCode NVARCHAR(20) NULL,
        Email NVARCHAR(200) NULL,
        StatusCode INT NOT NULL,
        Succeeded BIT NOT NULL CONSTRAINT DF_FunctionClaimantLog_Succeeded DEFAULT (1),
        Message NVARCHAR(500) NULL,
        RequestJson NVARCHAR(MAX) NULL
    );

    CREATE INDEX IX_FunctionClaimantLog_LoggedUtc ON dbo.FunctionClaimantLog (LoggedUtc DESC);
    CREATE INDEX IX_FunctionClaimantLog_ClaimantId ON dbo.FunctionClaimantLog (ClaimantId);
END
GO
