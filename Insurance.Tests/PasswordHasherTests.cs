using Insurance.Helpers;

namespace Insurance.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_then_verify_succeeds_for_same_password()
    {
        var hash = PasswordHasher.Hash("Password@123");
        Assert.True(PasswordHasher.Verify("Password@123", hash));
    }

    [Fact]
    public void Demo_seed_hash_matches_password()
    {
        const string seed = "100000.Jzy103WsxBVX/BubViv+nQ==.IFOWqjnjfsmaOCTnRLYD5syJFFbgpqIoVm7Q3yHUGb4=";
        Assert.True(PasswordHasher.Verify("Password@123", seed));
    }

    [Fact]
    public void Verify_fails_for_wrong_password()
    {
        var hash = PasswordHasher.Hash("Password@123");
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }
}
