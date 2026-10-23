namespace Insurance.Contracts.Auth;

public sealed class SessionUserDto
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];
}
