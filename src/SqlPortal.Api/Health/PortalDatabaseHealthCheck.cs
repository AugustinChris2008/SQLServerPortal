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
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (1) Probe
                FROM
                (
                    SELECT TOP (0) Probe = CAST(1 AS int)
                    FROM [ZZImportTest].[dbo].[zol_ready4import]
                    UNION ALL
                    SELECT TOP (0) CAST(1 AS int)
                    FROM [Backupparameter].[dbo].[ComplianceRules]
                    UNION ALL
                    SELECT TOP (0) CAST(1 AS int)
                    FROM [Backupparameter].[dbo].[ComplianceResults]
                    UNION ALL
                    SELECT CAST(1 AS int)
                ) AS readableSources;
                """;
            command.CommandTimeout = 5;
            _ = await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Portal source tables are readable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Portal database is not reachable.",
                exception);
        }
    }
}

