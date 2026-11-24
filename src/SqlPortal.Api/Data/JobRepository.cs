using Dapper;
using Microsoft.Data.SqlClient;
using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public sealed class JobRepository(IConfiguration configuration) : IJobRepository
{
    private const string Columns = "ComputerName, InstanceName, SqlInstance, Job, StepName, RunDate, StartDate, EndDate, Duration, Status, Message";
    private const string HistorySource = "SELECT " + Columns + " FROM [dbo].[JobRunHistory]";

    // The failed-run view contains job steps as well as overall outcomes.
    private const string UnsuccessfulHistorySource =
        "SELECT " + Columns + " FROM [dbo].[vw_FailedJobRuns] UNION ALL " +
        HistorySource + " WHERE UPPER(TRIM(Status)) IN (N'CANCELED', N'CANCELLED')";

    private const string LatestOutcomesCte = """
        , RankedOutcomes AS
        (
            SELECT outcomes.*,
                RunRank = ROW_NUMBER() OVER (
                    PARTITION BY ServerName, SqlInstance, JobName
                    ORDER BY StartedAt DESC, EndedAt DESC,
                        CASE WHEN Status = N'Succeeded' THEN 1 ELSE 0 END, Status, Duration, Message)
            FROM Outcomes AS outcomes
        )
        """;

    private readonly string _connectionString = configuration.GetConnectionString("PortalDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:PortalDatabase is required in SqlServer mode.");

    public async Task<IReadOnlyList<JobServerSummary>> GetServersAsync(CancellationToken cancellationToken)
    {
        var sql = JobOutcomesCte(HistorySource) + LatestOutcomesCte + """

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
            new CommandDefinition(sql, cancellationToken: cancellationToken))).AsList();
    }

    public async Task<PagedResult<JobRun>> GetRunsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        var historySource = query.History && query.Status == JobQuery.NotSuccessful
            ? UnsuccessfulHistorySource
            : HistorySource;
        var outcomes = JobOutcomesCte(historySource);
        var ranking = query.History ? outcomes : outcomes + LatestOutcomesCte;
        var source = query.History ? "Outcomes" : "RankedOutcomes";
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
        var sql = cte + """
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
            Offset = (query.Page - 1L) * query.PageSize,
            query.PageSize
        };
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await grid.ReadAsync<JobRun>()).AsList();
        var total = await grid.ReadSingleAsync<int>();
        return new PagedResult<JobRun>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<JobRuleViolation>> GetViolationsAsync(string? server, CancellationToken cancellationToken)
    {
        // Rules are scoped per instance. Match the latest known classification
        // from the same compliance source used in the configuration tab.
        var sql = JobOutcomesCte(HistorySource) + """

            , KnownJobs AS
            (
                SELECT ServerName, SqlInstance, JobName,
                    ActualRuns = COUNT(CASE WHEN StartedAt >= bounds.WindowStart AND StartedAt <= bounds.WindowEnd THEN 1 END),
                    UnsuccessfulRuns = COUNT(CASE WHEN StartedAt >= bounds.WindowStart AND StartedAt <= bounds.WindowEnd
                        AND Status <> N'Succeeded' THEN 1 END)
                FROM (
                    SELECT DISTINCT ServerName, SqlInstance, JobName, StartedAt, EndedAt, Status
                    FROM Outcomes
                    WHERE @Server IS NULL OR ServerName = UPPER(@Server)
                ) AS runs
                CROSS JOIN (SELECT WindowStart = DATEADD(hour, -24, GETDATE()), WindowEnd = GETDATE()) AS bounds
                GROUP BY ServerName, SqlInstance, JobName
            ),
            Compared AS
            (
                SELECT job.ServerName, job.SqlInstance, job.JobName, classification.Environment, classification.ServerType,
                    normalized.RunCountCheckType, monitoringRule.ExpectedRuns24h, job.ActualRuns, job.UnsuccessfulRuns,
                    RunCountViolated = CAST(CASE
                        WHEN normalized.RunCountCheckType = N'EXACT'
                            AND (monitoringRule.ExpectedRuns24h IS NULL OR job.ActualRuns <> monitoringRule.ExpectedRuns24h) THEN 1
                        WHEN normalized.RunCountCheckType = N'MINIMUM'
                            AND (monitoringRule.ExpectedRuns24h IS NULL OR job.ActualRuns < monitoringRule.ExpectedRuns24h) THEN 1
                        ELSE 0 END AS bit),
                    SuccessViolated = CAST(CASE WHEN monitoringRule.CheckSuccess = 1 AND job.UnsuccessfulRuns > 0
                        THEN 1 ELSE 0 END AS bit),
                    monitoringRule.Description
                FROM KnownJobs AS job
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
                INNER JOIN [dbo].[JobMonitoringRules] AS monitoringRule
                    ON UPPER(TRIM(monitoringRule.JobName)) = UPPER(job.JobName)
                   AND UPPER(TRIM(monitoringRule.Environment)) IN (N'ALL', classification.Environment)
                   AND UPPER(TRIM(monitoringRule.ServerType)) IN (N'ALL', UPPER(classification.ServerType))
                CROSS APPLY (SELECT RunCountCheckType = UPPER(TRIM(monitoringRule.RunCountCheckType))) AS normalized
            )
            SELECT DISTINCT ServerName, SqlInstance, JobName, Environment, ServerType, RunCountCheckType, ExpectedRuns24h,
                ActualRuns, UnsuccessfulRuns, RunCountViolated, SuccessViolated, Description
            FROM Compared
            WHERE RunCountViolated = 1 OR SuccessViolated = 1
            ORDER BY ServerName, SqlInstance, JobName, RunCountCheckType, ExpectedRuns24h, Description;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return (await connection.QueryAsync<JobRuleViolation>(
            new CommandDefinition(sql, new { Server = server }, cancellationToken: cancellationToken))).AsList();
    }

    private static string JobOutcomesCte(string source) => $"""
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
            FROM ({source}) AS h
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
