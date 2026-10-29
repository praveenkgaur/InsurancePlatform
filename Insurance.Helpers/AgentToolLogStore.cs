using Dapper;
using Microsoft.Data.SqlClient;

namespace Insurance.Helpers;

public sealed class AgentToolLogStore
{
    private readonly string _connectionString;
    private static int _ensured;

    public AgentToolLogStore(string connectionString) => _connectionString = connectionString;

    public async Task WriteAsync(
        string pluginName,
        string toolName,
        string? requestJson,
        string? responseJson,
        string? details,
        int durationMs,
        bool succeeded)
    {
        try
        {
            await EnsureTableAsync();
            await using var connection = new SqlConnection(_connectionString);
            await connection.ExecuteAsync("""
                INSERT INTO dbo.AgentToolLog
                    (PluginName, ToolName, RequestJson, ResponseJson, Details, DurationMs, Succeeded)
                VALUES
                    (@PluginName, @ToolName, @RequestJson, @ResponseJson, @Details, @DurationMs, @Succeeded);
                """,
                new
                {
                    PluginName = Truncate(pluginName, 100),
                    ToolName = Truncate(toolName, 200),
                    RequestJson = requestJson,
                    ResponseJson = responseJson,
                    Details = details,
                    DurationMs = durationMs,
                    Succeeded = succeeded
                });
        }
        catch
        {
            // Tool logging must not fail the agent call.
        }
    }

    private async Task EnsureTableAsync()
    {
        if (Interlocked.Exchange(ref _ensured, 1) == 1)
            return;

        await using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync("""
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
            """);
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
