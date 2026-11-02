namespace Insurance.Contracts.Auth;

public sealed class CreateUserRequest
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RoleName { get; set; } = "";
}
