using Insurance.Helpers;

namespace Insurance.Tests;

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Temporary_password_meets_length_and_character_classes()
    {
        var password = PasswordGenerator.CreateTemporary();
        Assert.Equal(PasswordGenerator.Length, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => "!@#$%*?".Contains(c));
    }

    [Fact]
    public void Temporary_passwords_are_not_identical_across_calls()
    {
        var first = PasswordGenerator.CreateTemporary();
        var second = PasswordGenerator.CreateTemporary();
        Assert.NotEqual(first, second);
    }
}
