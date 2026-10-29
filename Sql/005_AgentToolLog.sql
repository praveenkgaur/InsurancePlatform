USE DemoDataBase;
GO

IF OBJECT_ID('dbo.AgentToolLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AgentToolLog
    (
        LogId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LoggedUtc DATETIME2 NOT NULL CONSTRAINT DF_AgentToolLog_LoggedUtc DEFAULT (SYSUTCDATETIME()),
        PluginName NVARCHAR(100) NOT NULL,
        ToolName NVARCHAR(200) NOT NULL,
        RequestJson NVARCHAR(MAX) NULL,
        ResponseJson NVARCHAR(MAX) NULL,
        Details NVARCHAR(MAX) NULL,
        DurationMs INT NULL,
        Succeeded BIT NOT NULL CONSTRAINT DF_AgentToolLog_Succeeded DEFAULT (1)
    );

    CREATE INDEX IX_AgentToolLog_LoggedUtc ON dbo.AgentToolLog (LoggedUtc DESC);
END
GO
