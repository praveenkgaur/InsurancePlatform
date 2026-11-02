namespace Insurance.Contracts.Auth;

public sealed class CreateUserResult
{
    public UserAdminDto User { get; set; } = new();
    public string TemporaryPassword { get; set; } = "";
}
