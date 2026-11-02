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
    public Guid? EntraObjectId { get; set; }
}

public sealed class LoginService
{
    private readonly SqlCommandExecutor _sql;
    private readonly TokenService _tokens;
    private readonly SessionService _sessions;
    private readonly IConfiguration _config;

    public LoginService(SqlCommandExecutor sql, TokenService tokens, SessionService sessions, IConfiguration config)
    {
        _sql = sql;
        _tokens = tokens;
        _sessions = sessions;
        _config = config;
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

    private Task<UserRecord?> FindUserAsync(string where, string key) =>
        _sql.QuerySingleAsync<UserRecord>(
            $"""
            SELECT UserId, UserName, DisplayName, PasswordHash, IsActive, EntraObjectId
            FROM dbo.Users
            WHERE {where}
            """,
            new { Key = key });

    private async Task<UserRecord?> FindAndLinkAsync(string userName, Guid oid)
    {
        var user = await FindUserAsync("UserName = @Key AND IsActive = 1", userName);
        if (user is null)
            return null;
        if (user.EntraObjectId is { } existing && existing != oid)
            return null;
        if (user.EntraObjectId is null)
            await _sql.ExecuteAsync(
                "UPDATE dbo.Users SET EntraObjectId = @Oid WHERE UserId = @UserId AND EntraObjectId IS NULL",
                new { Oid = oid, user.UserId });
        user.EntraObjectId = oid;
        return user;
    }

    private async Task<UserRecord?> ProvisionEntraUserAsync(Guid oid, string email, string? displayName)
    {
        var existing = await FindUserAsync("UserName = @Key AND IsActive = 1", email);
        if (existing is not null)
            return await FindAndLinkAsync(email, oid);

        var name = string.IsNullOrWhiteSpace(displayName) ? email : displayName.Trim();
        var hash = PasswordHasher.Hash(Guid.NewGuid().ToString("N"));
        await _sql.ExecuteAsync(
            """
            INSERT INTO dbo.Users (UserName, PasswordHash, DisplayName, IsActive, EntraObjectId)
            VALUES (@UserName, @PasswordHash, @DisplayName, 1, @Oid)
            """,
            new { UserName = email, PasswordHash = hash, DisplayName = name, Oid = oid });

        var created = await FindUserAsync("EntraObjectId = @Key AND IsActive = 1", oid.ToString());
        if (created is null)
            return null;

        var role = _config["Entra:DefaultRole"];
        if (string.IsNullOrWhiteSpace(role))
            role = "ClaimsAdjuster";
        await _sql.ExecuteAsync(
            """
            INSERT INTO dbo.UserRoles (UserId, RoleId)
            SELECT @UserId, r.RoleId FROM dbo.Roles r
            WHERE r.RoleName = @RoleName
              AND NOT EXISTS (SELECT 1 FROM dbo.UserRoles ur WHERE ur.UserId = @UserId AND ur.RoleId = r.RoleId)
            """,
            new { created.UserId, RoleName = role });
        return created;
    }

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


    public async Task<(SessionUserDto User, Guid SessionId, string RawToken)?> SignInFromEntraAsync(
        string? objectId,
        string? email,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(objectId) || !Guid.TryParse(objectId, out var oid))
            return null;

        var user = await FindUserAsync("EntraObjectId = @Key AND IsActive = 1", oid.ToString());

        var upn = email?.Trim() ?? "";
        if (user is null && upn.Length > 0)
            user = await FindAndLinkAsync(upn, oid);

        if (user is null && upn.Length > 0)
        {
            var mapped = _config[$"Entra:UserMap:{upn}"] ?? _config[$"Entra:UserMap:{upn.ToLowerInvariant()}"];
            if (!string.IsNullOrWhiteSpace(mapped))
                user = await FindAndLinkAsync(mapped, oid);
        }

        if (user is null && _config.GetValue("Entra:AutoProvision", false) && upn.Length > 0)
            user = await ProvisionEntraUserAsync(oid, upn, displayName);

        if (user is null || !user.IsActive)
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
}
