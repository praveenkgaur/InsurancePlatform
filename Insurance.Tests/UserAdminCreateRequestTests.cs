using Insurance.Contracts.Auth;

namespace Insurance.Tests;

public sealed class UserAdminCreateRequestTests
{
    [Fact]
    public void Create_request_does_not_carry_a_password()
    {
        var request = new CreateUserRequest();
        Assert.Equal("", request.UserName);
        Assert.Equal("", request.RoleName);
        Assert.Null(typeof(CreateUserRequest).GetProperty("Password"));
    }
}
