using Insurance.Helpers;

namespace Insurance.Tests;

public sealed class CorsOriginRulesTests
{
    [Fact]
    public void Allows_configured_ui_origin()
    {
        Assert.True(CorsOriginRules.IsAllowed(
            "https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net",
            "https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net"));
    }

    [Fact]
    public void Allows_ui_stamp_when_config_is_wrong()
    {
        Assert.True(CorsOriginRules.IsAllowed(
            "https://pk-ins-ui-a7aebdgfsdecmah.southindia-01.azurewebsites.net",
            "https://pk-ins-ui.azurewebsites.net"));
    }

    [Fact]
    public void Rejects_unrelated_origin()
    {
        Assert.False(CorsOriginRules.IsAllowed("https://evil.example", "http://localhost:5280"));
    }
}
