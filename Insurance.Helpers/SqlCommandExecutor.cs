using Dapper;
using Microsoft.Data.SqlClient;

namespace Insurance.Helpers;

public sealed class SqlCommandExecutor
{
    private readonly string _connectionString;

    public SqlCommandExecutor(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.ConnectTimeout < 60)
            builder.ConnectTimeout = 60;
        _connectionString = builder.ConnectionString;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<T>(sql, parameters);
        return rows.AsList();
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.QuerySingleOrDefaultAsync<T>(sql, parameters);
    }

    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        return await connection.ExecuteAsync(sql, parameters);
    }
}
