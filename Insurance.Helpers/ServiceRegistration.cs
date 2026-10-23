using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Insurance.Helpers;

public static class ServiceRegistration
{
    public static IServiceCollection AddInsuranceData(this IServiceCollection services, IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("DemoDataBase")
            ?? "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=DemoDataBase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        services.AddSingleton(new SqlCommandExecutor(connection));
        return services;
    }
}
