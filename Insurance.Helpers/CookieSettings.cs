namespace Insurance.Helpers;

public static class CookieSettings
{
    public const string ProductionCookieName = "__Host-AppSession";
    public const string DevelopmentCookieName = "AppSession";

    public static string Name(bool isHttps) => isHttps ? ProductionCookieName : DevelopmentCookieName;
}
