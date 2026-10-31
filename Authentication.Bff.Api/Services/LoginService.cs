using Insurance.Contracts.Auth;
using Insurance.Helpers;

namespace Authentication.Bff.Api.Services;

public sealed class UserRecord
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class LoginService
{
    private readonly SqlCommandExecutor _sql;
    private readonly TokenService _tokens;
    private readonly SessionService _sessions;

    public LoginService(SqlCommandExecutor sql, TokenService tokens, SessionService sessions)
    {
        _sql = sql;
        _tokens = tokens;
        _sessions = sessions;
    }

    public async Task<(SessionUserDto User, Guid SessionId, string RawToken)?> SignInAsync(LoginRequest request)
    {
        var user = await _sql.QuerySingleAsync<UserRecord>(
            "SELECT UserId, UserName, DisplayName, PasswordHash, IsActive FROM dbo.Users WHERE UserName = @UserName",
            new { request.UserName });
        if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            return null;

        var roles = await LoadRolesAsync(user.UserId);
        var permissions = await LoadPermissionsAsync(user.UserId);
        var access = _tokens.CreateAccessToken(user.UserName, roles, permissions);
        var refresh = _tokens.CreateRefreshToken();
        var session = await _sessions.CreateAsync(user.UserId, access, refresh);
        var dto = new SessionUserDto
        {
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            Roles = roles,
            Permissions = permissions
        };
        return (dto, session.SessionId, session.RawToken);
    }

    public async Task<SessionUserDto?> GetCurrentAsync(Guid sessionId, string rawToken)
    {
        var session = await _sessions.GetAsync(sessionId, rawToken);
        if (session is null)
            return null;
        return new SessionUserDto
        {
            UserName = session.UserName,
            DisplayName = session.DisplayName,
            Roles = await LoadRolesAsync(session.UserId),
            Permissions = await LoadPermissionsAsync(session.UserId)
        };
    }

    public async Task<string?> GetAccessTokenAsync(Guid sessionId, string rawToken)
    {
        var user = await GetCurrentAsync(sessionId, rawToken);
        if (user is null)
            return null;
        // Mint a new JWT for each API call. /auth/me uses the session cookie only;
        // a stored token older than Jwt:AccessTokenMinutes (30) made Claims/Policies return 401.
        return _tokens.CreateAccessToken(user.UserName, user.Roles, user.Permissions);
    }

    public Task SignOutAsync(Guid sessionId) => _sessions.DeleteAsync(sessionId);

    public Task<IReadOnlyList<string>> LoadRolesAsync(int userId) =>
        _sql.QueryAsync<string>(
            """
            SELECT r.RoleName
            FROM dbo.UserRoles ur
            INNER JOIN dbo.Roles r ON r.RoleId = ur.RoleId
            WHERE ur.UserId = @UserId
            """,
            new { UserId = userId });

    public Task<IReadOnlyList<string>> LoadPermissionsAsync(int userId) =>
        _sql.QueryAsync<string>(
            """
            SELECT DISTINCT p.PermissionKey
            FROM dbo.UserRoles ur
            INNER JOIN dbo.RolePermissions rp ON rp.RoleId = ur.RoleId
            INNER JOIN dbo.Permissions p ON p.PermissionId = rp.PermissionId
            WHERE ur.UserId = @UserId
            """,
            new { UserId = userId });
}
