using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SqlPortal.Api.Health;

public sealed class PortalDatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var mode = configuration["DataSource:Mode"] ?? "Demo";
        if (!string.Equals(mode, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return HealthCheckResult.Healthy("Demo data source is ready.");
        }

        var connectionString = configuration.GetConnectionString("PortalDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("Portal database connection string is missing.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy("Portal database is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Portal database is not reachable.",
                exception);
        }
    }
}

