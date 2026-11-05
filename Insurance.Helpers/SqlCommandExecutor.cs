using Dapper;
using Microsoft.Data.SqlClient;

namespace Insurance.Helpers;


public sealed class SqlCommandExecutor
{
    private static readonly int[] Transient =
        [40613, 40197, 40501, 49918, 10928, 10929, 10053, 10054, 10060, 233, 64, 20, -2];

    private readonly string _connectionString;

    public SqlCommandExecutor(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.ConnectTimeout < 60)
            builder.ConnectTimeout = 60;
        _connectionString = builder.ConnectionString;
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null) =>
        WithRetry(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            var rows = await connection.QueryAsync<T>(sql, parameters);
            return (IReadOnlyList<T>)rows.AsList();
        });

    public Task<T?> QuerySingleAsync<T>(string sql, object? parameters = null) =>
        WithRetry(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            return await connection.QuerySingleOrDefaultAsync<T>(sql, parameters);
        });

    public Task<int> ExecuteAsync(string sql, object? parameters = null) =>
        WithRetry(async () =>
        {
            await using var connection = new SqlConnection(_connectionString);
            return await connection.ExecuteAsync(sql, parameters);
        });

    private static async Task<T> WithRetry<T>(Func<Task<T>> action)
    {
        const int max = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (SqlException ex) when (attempt < max && Transient.Contains(ex.Number))
            {
                await Task.Delay(500 * attempt);
            }
        }
    }
}