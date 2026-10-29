namespace Insurance.Helpers;

public static class CorsOriginRules
{
    public static bool IsAllowed(string? origin, string? configuredUiOrigin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        var o = origin.Trim().TrimEnd('/');
        var ui = (configuredUiOrigin ?? "").Trim().TrimEnd('/');
        if (string.Equals(o, ui, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(o, "http://localhost:5280", StringComparison.OrdinalIgnoreCase)
            || string.Equals(o, "http://localhost:5281", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Uri.TryCreate(o, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = uri.Host;
        return host.StartsWith("pk-ins-ui", StringComparison.OrdinalIgnoreCase)
            && host.EndsWith(".azurewebsites.net", StringComparison.OrdinalIgnoreCase);
    }
}
