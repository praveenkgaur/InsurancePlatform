namespace Insurance.Contracts.Auth;

public sealed class UserAdminDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; }
    public Guid? EntraObjectId { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}
