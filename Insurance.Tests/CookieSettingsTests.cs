using Insurance.Helpers;

namespace Insurance.Tests;

public sealed class CookieSettingsTests
{
    [Fact]
    public void Https_uses_host_prefix_cookie()
    {
        Assert.Equal("__Host-AppSession", CookieSettings.Name(true));
    }

    [Fact]
    public void Http_local_uses_app_session_cookie()
    {
        Assert.Equal("AppSession", CookieSettings.Name(false));
    }
}
