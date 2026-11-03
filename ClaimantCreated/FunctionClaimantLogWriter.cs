using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClaimantCreated;

public sealed class FunctionClaimantLogWriter
{
    private readonly string? _connectionString;
    private readonly ILogger<FunctionClaimantLogWriter> _log;

    public FunctionClaimantLogWriter(IConfiguration config, ILogger<FunctionClaimantLogWriter> log)
    {
        _connectionString = config.GetConnectionString("DemoDataBase")
            ?? config["ConnectionStrings:DemoDataBase"]
            ?? config["SQLAZURECONNSTR_DemoDataBase"];
        _log = log;
    }

    public async Task<bool> WriteAsync(
        string httpMethod,
        int? claimantId,
        string? fullName,
        string? city,
        string? pinCode,
        string? email,
        int statusCode,
        bool succeeded,
        string? message,
        string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            _log.LogWarning("FunctionClaimantLog skipped: ConnectionStrings:DemoDataBase is not set.");
            return false;
        }

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO dbo.FunctionClaimantLog
                (
                    FunctionName, HttpMethod, ClaimantId, FullName, City, PinCode, Email,
                    StatusCode, Succeeded, Message, RequestJson
                )
                VALUES
                (
                    @FunctionName, @HttpMethod, @ClaimantId, @FullName, @City, @PinCode, @Email,
                    @StatusCode, @Succeeded, @Message, @RequestJson
                )
                """;
            cmd.Parameters.AddWithValue("@FunctionName", "ClaimantCreated");
            cmd.Parameters.AddWithValue("@HttpMethod", httpMethod);
            cmd.Parameters.AddWithValue("@ClaimantId", (object?)claimantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@FullName", (object?)fullName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@City", (object?)city ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PinCode", (object?)pinCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StatusCode", statusCode);
            cmd.Parameters.AddWithValue("@Succeeded", succeeded);
            cmd.Parameters.AddWithValue("@Message", (object?)message ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RequestJson", (object?)requestJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "FunctionClaimantLog insert failed");
            return false;
        }
    }
}
