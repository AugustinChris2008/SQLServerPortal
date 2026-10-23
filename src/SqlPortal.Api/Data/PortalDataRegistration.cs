namespace SqlPortal.Api.Data;

public static class PortalDataRegistration
{
    public static IServiceCollection AddPortalData(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var mode = configuration["DataSource:Mode"] ?? "Demo";

        if (string.Equals(mode, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IPortalRepository, SqlPortalRepository>();
        }
        else if (string.Equals(mode, "Demo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPortalRepository, DemoPortalRepository>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported DataSource:Mode '{mode}'. Use 'Demo' or 'SqlServer'.");
        }

        return services;
    }
}

