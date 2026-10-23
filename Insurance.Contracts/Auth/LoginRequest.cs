namespace Insurance.Contracts.Auth;

public sealed class LoginRequest
{
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}
