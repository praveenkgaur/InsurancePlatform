using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Insurance.Helpers
{
    public sealed class IdempotencyStore
    {
        private readonly SqlCommandExecutor _sql;

        public IdempotencyStore(SqlCommandExecutor sql) => _sql = sql;

        public static string HashBody(string body)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body ?? ""));
            return Convert.ToHexString(bytes);
        }

        public Task<IdempotencyRow?> GetAsync(string key) =>
            _sql.QuerySingleAsync<IdempotencyRow>(
                """
            SELECT IdempotencyKey, HttpMethod, RequestPath, RequestHash, StatusCode, ResponseBody
            FROM dbo.IdempotencyRecord WHERE IdempotencyKey = @key
            """,
                new { key });

        public Task SaveAsync(IdempotencyRow row) =>
            _sql.ExecuteAsync(
                """
            IF NOT EXISTS (SELECT 1 FROM dbo.IdempotencyRecord WHERE IdempotencyKey = @IdempotencyKey)
            INSERT INTO dbo.IdempotencyRecord
                (IdempotencyKey, HttpMethod, RequestPath, RequestHash, StatusCode, ResponseBody)
            VALUES
                (@IdempotencyKey, @HttpMethod, @RequestPath, @RequestHash, @StatusCode, @ResponseBody)
            """,
                row);
    }


    public sealed class IdempotencyRow
    {
        public string IdempotencyKey { get; set; } = "";
        public string HttpMethod { get; set; } = "";
        public string RequestPath { get; set; } = "";
        public string RequestHash { get; set; } = "";
        public int StatusCode { get; set; }
        public string? ResponseBody { get; set; }
    }
}
