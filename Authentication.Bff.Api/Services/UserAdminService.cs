using Insurance.Contracts.Auth;
using Insurance.Helpers;

namespace Authentication.Bff.Api.Services;

public sealed class UserAdminService
{
    private readonly SqlCommandExecutor _sql;

    public UserAdminService(SqlCommandExecutor sql) => _sql = sql;

    public async Task<IReadOnlyList<UserAdminDto>> ListAsync()
    {
        var users = await _sql.QueryAsync<UserRow>(
            """
            SELECT UserId, UserName, DisplayName, IsActive, EntraObjectId
            FROM dbo.Users
            ORDER BY UserId
            """);
        var roles = await _sql.QueryAsync<UserRoleRow>(
            """
            SELECT ur.UserId, r.RoleName
            FROM dbo.UserRoles ur
            INNER JOIN dbo.Roles r ON r.RoleId = ur.RoleId
            """);
        var byUser = roles.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).ToArray());
        return users.Select(u => new UserAdminDto
        {
            UserId = u.UserId,
            UserName = u.UserName,
            DisplayName = u.DisplayName,
            IsActive = u.IsActive,
            EntraObjectId = u.EntraObjectId,
            Roles = byUser.TryGetValue(u.UserId, out var list) ? list : []
        }).ToList();
    }

    public Task<IReadOnlyList<string>> ListRolesAsync() =>
        _sql.QueryAsync<string>("SELECT RoleName FROM dbo.Roles ORDER BY RoleName");

    public async Task<(CreateUserResult? Created, string? Error)> CreateAsync(CreateUserRequest request)
    {
        var userName = (request.UserName ?? "").Trim();
        var display = (request.DisplayName ?? "").Trim();
        var roleName = (request.RoleName ?? "").Trim();

        if (userName.Length < 3 || userName.Length > 100)
            return (null, "User name must be 3–100 characters.");
        if (display.Length < 1)
            display = userName;
        if (string.IsNullOrWhiteSpace(roleName))
            return (null, "Choose a role.");

        var roleId = await _sql.QuerySingleAsync<int?>(
            "SELECT RoleId FROM dbo.Roles WHERE RoleName = @RoleName",
            new { RoleName = roleName });
        if (roleId is null)
            return (null, $"Unknown role '{roleName}'.");

        var exists = await _sql.QuerySingleAsync<int?>(
            "SELECT UserId FROM dbo.Users WHERE UserName = @UserName",
            new { UserName = userName });
        if (exists is not null)
            return (null, "That user name already exists.");

        var password = PasswordGenerator.CreateTemporary();
        var hash = PasswordHasher.Hash(password);
        var userId = await _sql.QuerySingleAsync<int>(
            """
            INSERT INTO dbo.Users (UserName, PasswordHash, DisplayName, IsActive)
            VALUES (@UserName, @PasswordHash, @DisplayName, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            new { UserName = userName, PasswordHash = hash, DisplayName = display });

        await _sql.ExecuteAsync(
            "INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@UserId, @RoleId)",
            new { UserId = userId, RoleId = roleId.Value });

        var user = (await ListAsync()).FirstOrDefault(u => u.UserId == userId);
        if (user is null)
            return (null, "User was created but could not be loaded.");
        return (new CreateUserResult { User = user, TemporaryPassword = password }, null);
    }

    private sealed class UserRow
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsActive { get; set; }
        public Guid? EntraObjectId { get; set; }
    }

    private sealed class UserRoleRow
    {
        public int UserId { get; set; }
        public string RoleName { get; set; } = "";
    }
}
