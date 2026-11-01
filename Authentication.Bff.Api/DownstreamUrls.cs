namespace Authentication.Bff.Api;

internal static class DownstreamUrls
{
    public static bool UseApim(IConfiguration config) => config.GetValue("UseApim", false);

    public static string BaseUrl(IConfiguration config, string name)
    {
        if (UseApim(config))
        {
            var gateway = (config["Apim:Gateway"] ?? "").Trim().TrimEnd('/');
            var suffix = (config[$"Apim:{name}"] ?? name.ToLowerInvariant()).Trim().Trim('/');
            return string.IsNullOrWhiteSpace(gateway) ? "" : $"{gateway}/{suffix}";
        }

        return (config[$"Downstream:{name}"] ?? "").Trim().TrimEnd('/');
    }
}
