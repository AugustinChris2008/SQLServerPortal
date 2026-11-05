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

        var restoreTestConnectionString = configuration.GetConnectionString("RestoreTestDatabase");
        if (string.IsNullOrWhiteSpace(restoreTestConnectionString))
        {
            return HealthCheckResult.Unhealthy("Restore-test database connection string is missing.");
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
                    FROM [dbo].[v_SQLPortalDataOverview]
                    UNION ALL
                    SELECT TOP (0) CAST(1 AS int)
                    FROM [dbo].[ComplianceRules]
                    UNION ALL
                    SELECT TOP (0) CAST(1 AS int)
                    FROM [dbo].[ComplianceResults]
                    UNION ALL
                    SELECT TOP (0) CAST(1 AS int)
                    FROM [dbo].[ComplianceCurrentValues]
                    UNION ALL
                    SELECT TOP (0) CAST(1 AS int)
                    FROM [dbo].[SQLServerSupportLifecycle]
                    UNION ALL
                    SELECT CAST(1 AS int)
                ) AS readableSources;
                """;
            command.CommandTimeout = 5;
            _ = await command.ExecuteScalarAsync(cancellationToken);

            await using var restoreConnection = new SqlConnection(restoreTestConnectionString);
            await restoreConnection.OpenAsync(cancellationToken);
            await using var restoreCommand = restoreConnection.CreateCommand();
            restoreCommand.CommandText = "SELECT TOP (0) 1 FROM [dbo].[RestoreResults];";
            restoreCommand.CommandTimeout = 5;
            _ = await restoreCommand.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("Portal and restore-test source tables are readable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "Portal database is not reachable.",
                exception);
        }
    }
}

