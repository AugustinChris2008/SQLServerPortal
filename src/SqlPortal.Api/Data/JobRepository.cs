using Dapper;
using Microsoft.Data.SqlClient;
using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public sealed class JobRepository(IConfiguration configuration) : IJobRepository
{
    private const string HistoryWindowCte = """
        , PeriodOutcomes AS
        (
            SELECT *
            FROM Outcomes
            WHERE @HistoryDays IS NULL OR StartedAt BETWEEN DATEADD(day, -@HistoryDays, @Now) AND @Now
        )
        """;

    private const string LatestOutcomesCte = """
        , RankedOutcomes AS
        (
            SELECT outcomes.*,
                RunRank = ROW_NUMBER() OVER (
                    PARTITION BY ServerName, SqlInstance, JobName
                    ORDER BY StartedAt DESC, EndedAt DESC,
                        CASE WHEN Status = N'Succeeded' THEN 1 ELSE 0 END, Status, Duration, Message)
            FROM PeriodOutcomes AS outcomes
        )
        """;

    private const string InstanceClassification = """
        CROSS APPLY (
            SELECT InstanceHost = LEFT(job.SqlInstance, CHARINDEX(N'\', job.SqlInstance + N'\') - 1),
                InstanceSuffix = CASE WHEN CHARINDEX(N'\', job.SqlInstance) > 0
                    THEN SUBSTRING(job.SqlInstance, CHARINDEX(N'\', job.SqlInstance) + 1, 511)
                    ELSE N'MSSQLSERVER' END
        ) AS identityParts
        OUTER APPLY (
            SELECT TOP (1)
                Environment = NULLIF(UPPER(TRIM(result.Environment)), N''),
                ServerType = NULLIF(TRIM(result.ServerType), N'')
            FROM [dbo].[ComplianceResults] AS result
            WHERE UPPER(TRIM(result.SqlServer)) IN (job.ServerName, identityParts.InstanceHost)
              AND COALESCE(NULLIF(UPPER(TRIM(result.SqlInstance)), N''), N'MSSQLSERVER')
                  IN (job.SqlInstance, identityParts.InstanceSuffix)
            ORDER BY result.EvaluatedAt DESC, result.Environment, result.ServerType
        ) AS classification
        """;

    private const string UnclassifiedInstance = """
        CROSS APPLY (SELECT Environment = CAST(NULL AS nvarchar(50)), ServerType = CAST(NULL AS nvarchar(50))) AS classification
        """;

    private const string DatabaseClock = "DECLARE @Now datetime = GETDATE();\n";

    private readonly string _connectionString = configuration.GetConnectionString("PortalDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:PortalDatabase is required in SqlServer mode.");

    public async Task<IReadOnlyList<JobServerSummary>> GetServersAsync(int? historyDays, CancellationToken cancellationToken)
    {
        var sql = DatabaseClock + JobOutcomesCte + HistoryWindowCte + LatestOutcomesCte + """

            SELECT ServerName,
                TotalJobs = COUNT(CASE WHEN RunRank = 1 THEN 1 END),
                SuccessfulJobs = COUNT(CASE WHEN RunRank = 1 AND Status = N'Succeeded' THEN 1 END),
                HasUnsuccessfulHistory = CAST(MAX(CASE WHEN Status <> N'Succeeded' THEN 1 ELSE 0 END) AS bit)
            FROM RankedOutcomes
            GROUP BY ServerName
            ORDER BY ServerName;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return (await connection.QueryAsync<JobServerSummary>(
            new CommandDefinition(sql, new { HistoryDays = historyDays }, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<PagedResult<JobRun>> GetRunsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        var outcomes = JobOutcomesCte + HistoryWindowCte;
        var ranking = query.History ? outcomes : outcomes + LatestOutcomesCte;
        var source = query.History ? "PeriodOutcomes" : "RankedOutcomes";
        var latestFilter = query.History ? "" : "RunRank = 1 AND ";
        var filtered = $"""

            , Filtered AS
            (
                SELECT ServerName, SqlInstance, JobName, StartedAt, EndedAt, Duration, Status, Message
                FROM {source}
                WHERE {latestFilter}(@Server IS NULL OR ServerName = UPPER(@Server))
                  AND (@SqlInstance IS NULL OR SqlInstance = UPPER(@SqlInstance))
                  AND (@JobName IS NULL OR JobName = @JobName)
                  AND (@Status IS NULL OR
                    (@Status = N'successful' AND Status = N'Succeeded') OR
                    (@Status = N'not-successful' AND Status <> N'Succeeded'))
                  AND (@Search IS NULL OR CHARINDEX(UPPER(@Search), ServerName) > 0 OR
                    CHARINDEX(UPPER(@Search), SqlInstance) > 0 OR
                    CHARINDEX(UPPER(@Search), UPPER(JobName)) > 0)
            )

            """;
        var cte = ranking + filtered;
        var sql = DatabaseClock + cte + """
            SELECT ServerName, SqlInstance, JobName, StartedAt, EndedAt, Duration, Status,
                Message = CASE WHEN @IncludeMessage = 1 THEN Filtered.Message END
            FROM Filtered
            ORDER BY StartedAt DESC, EndedAt DESC, ServerName, SqlInstance, JobName, Status, Duration, Filtered.Message
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """ + cte + """
            SELECT COUNT(1) FROM Filtered;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var parameters = new
        {
            query.Search,
            query.Server,
            query.SqlInstance,
            query.JobName,
            query.Status,
            query.IncludeMessage,
            query.HistoryDays,
            Offset = (query.Page - 1L) * query.PageSize,
            query.PageSize
        };
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await grid.ReadAsync<JobRun>()).AsList();
        var total = await grid.ReadSingleAsync<int>();
        return new PagedResult<JobRun>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<JobMonitoringResult>> GetMonitoringAsync(string? server, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Reuse the portal's instance metadata when it is available.
        var hasClassification = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.ComplianceResults', N'U') IS NOT NULL
                AND HAS_PERMS_BY_NAME(N'dbo.ComplianceResults', N'OBJECT', N'SELECT') = 1 THEN 1 ELSE 0 END AS bit);
            """,
            cancellationToken: cancellationToken));
        var classification = hasClassification ? InstanceClassification : UnclassifiedInstance;
        var sql = DatabaseClock + JobOutcomesCte + $"""

            , KnownJobs AS
            (
                SELECT DISTINCT ServerName, SqlInstance, JobName
                FROM Outcomes
                WHERE @Server IS NULL OR ServerName = UPPER(@Server)
            )
            SELECT job.ServerName, job.SqlInstance, job.JobName, classification.Environment, classification.ServerType,
                RunCountCheckType = UPPER(TRIM(monitoringRule.RunCountCheckType)), monitoringRule.ExpectedRuns,
                monitoringRule.MonitoringWindowHours, monitoringRule.CheckSuccess,
                counts.ActualRuns, counts.SuccessfulRuns, counts.FailedRuns, monitoringRule.Description
            FROM KnownJobs AS job
            {classification}
            INNER JOIN [dbo].[JobMonitoringRules] AS monitoringRule
                ON UPPER(TRIM(monitoringRule.JobName)) = UPPER(job.JobName)
               AND UPPER(TRIM(monitoringRule.Environment)) IN (N'ALL', classification.Environment)
               AND UPPER(TRIM(monitoringRule.ServerType)) IN (N'ALL', UPPER(classification.ServerType))
            CROSS APPLY (
                SELECT ActualRuns = COUNT(*),
                    SuccessfulRuns = COUNT(CASE WHEN jobRun.Status = N'Succeeded' THEN 1 END),
                    FailedRuns = COUNT(CASE WHEN jobRun.Status = N'Failed' THEN 1 END)
                FROM Outcomes AS jobRun
                WHERE jobRun.ServerName = job.ServerName AND jobRun.SqlInstance = job.SqlInstance AND jobRun.JobName = job.JobName
                  AND jobRun.StartedAt BETWEEN DATEADD(hour, -monitoringRule.MonitoringWindowHours, @Now) AND @Now
            ) AS counts
            ORDER BY job.ServerName, job.SqlInstance, job.JobName, monitoringRule.MonitoringWindowHours, RunCountCheckType,
                monitoringRule.ExpectedRuns, monitoringRule.Description;
            """;

        return (await connection.QueryAsync<JobMonitoringResult>(
            new CommandDefinition(sql, new { Server = server }, cancellationToken: cancellationToken))).AsList();
    }

    private const string JobOutcomesCte = """
        ;WITH Outcomes AS
        (
            SELECT
                ServerName = UPPER(host.ServerName),
                SqlInstance = UPPER(CASE
                    WHEN CHARINDEX(N'\', names.SqlInstance) > 0 THEN names.SqlInstance
                    WHEN CHARINDEX(N'\', names.InstanceName) > 0 THEN names.InstanceName
                    WHEN names.InstanceName IS NULL OR UPPER(names.InstanceName) = N'MSSQLSERVER'
                        OR UPPER(names.InstanceName) = UPPER(host.ServerName)
                        OR UPPER(names.InstanceName) = UPPER(names.SqlInstance)
                        THEN COALESCE(names.SqlInstance, host.ServerName)
                    ELSE CONCAT(COALESCE(names.SqlInstance, host.ServerName), N'\', names.InstanceName)
                END),
                JobName = TRIM(h.Job),
                StartedAt = COALESCE(h.StartDate, h.RunDate),
                EndedAt = h.EndDate,
                h.Duration,
                Status = CASE WHEN UPPER(TRIM(h.Status)) IN (N'CANCELED', N'CANCELLED') THEN N'Canceled'
                    WHEN UPPER(TRIM(h.Status)) = N'SUCCEEDED' THEN N'Succeeded'
                    ELSE N'Failed' END,
                h.Message
            FROM [dbo].[JobRunHistory] AS h
            CROSS APPLY (SELECT
                ComputerName = NULLIF(TRIM(h.ComputerName), N''),
                SqlInstance = NULLIF(TRIM(h.SqlInstance), N''),
                InstanceName = NULLIF(TRIM(h.InstanceName), N'')
            ) AS names
            CROSS APPLY (SELECT RawHost = COALESCE(names.ComputerName, names.SqlInstance,
                CASE WHEN CHARINDEX(N'\', names.InstanceName) > 0 THEN names.InstanceName END)
            ) AS instance
            CROSS APPLY (SELECT ServerName = NULLIF(
                LEFT(instance.RawHost, CHARINDEX(N'\', instance.RawHost + N'\') - 1), N'')
            ) AS host
            WHERE UPPER(TRIM(h.StepName)) = N'(JOB OUTCOME)'
              AND UPPER(TRIM(h.Status)) IN (N'SUCCEEDED', N'FAILED', N'CANCELED', N'CANCELLED')
              AND NULLIF(TRIM(h.Job), N'') IS NOT NULL
              AND host.ServerName IS NOT NULL
        )
        """;
}
