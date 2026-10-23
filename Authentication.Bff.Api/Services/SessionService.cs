using Insurance.Helpers;

namespace Authentication.Bff.Api.Services;

public sealed class SessionRecord
{
    public Guid SessionId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
}

public sealed class SessionService
{
    private readonly SqlCommandExecutor _sql;

    public SessionService(SqlCommandExecutor sql) => _sql = sql;

    public async Task<(Guid SessionId, string RawToken)> CreateAsync(int userId, string accessToken, string refreshToken)
    {
        var sessionId = Guid.NewGuid();
        var rawToken = TokenHash.NewOpaqueToken();
        await _sql.ExecuteAsync(
            """
            INSERT INTO dbo.Sessions (SessionId, UserId, SessionTokenHash, AccessToken, RefreshToken, ExpiresUtc)
            VALUES (@SessionId, @UserId, @SessionTokenHash, @AccessToken, @RefreshToken, @ExpiresUtc)
            """,
            new
            {
                SessionId = sessionId,
                UserId = userId,
                SessionTokenHash = TokenHash.Sha256(rawToken),
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresUtc = DateTime.UtcNow.AddHours(8)
            });
        return (sessionId, rawToken);
    }

    public Task<SessionRecord?> GetAsync(Guid sessionId, string rawToken) =>
        _sql.QuerySingleAsync<SessionRecord>(
            """
            SELECT s.SessionId, s.UserId, u.UserName, u.DisplayName, s.AccessToken, s.ExpiresUtc
            FROM dbo.Sessions s
            INNER JOIN dbo.Users u ON u.UserId = s.UserId
            WHERE s.SessionId = @SessionId AND s.SessionTokenHash = @SessionTokenHash AND s.ExpiresUtc > SYSUTCDATETIME()
            """,
            new { SessionId = sessionId, SessionTokenHash = TokenHash.Sha256(rawToken) });

    public Task DeleteAsync(Guid sessionId) =>
        _sql.ExecuteAsync("DELETE FROM dbo.Sessions WHERE SessionId = @SessionId", new { SessionId = sessionId });
}
