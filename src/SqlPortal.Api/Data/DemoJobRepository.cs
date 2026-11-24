using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public sealed class DemoJobRepository(TimeProvider timeProvider) : IJobRepository
{
    public Task<IReadOnlyList<JobServerSummary>> GetServersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<JobServerSummary> items = CreateRuns()
            .GroupBy(run => run.ServerName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var latest = LatestRuns(group).ToArray();
                return new JobServerSummary(group.Key, latest.Length, latest.Count(IsSuccessful),
                    group.Any(run => !IsSuccessful(run)));
            })
            .ToArray();
        return Task.FromResult(items);
    }

    public Task<PagedResult<JobRun>> GetRunsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        var runs = CreateRuns();
        var selected = query.History ? runs : LatestRuns(runs);
        var filtered = selected.Where(run =>
                (query.Server is null || run.ServerName.Equals(query.Server, StringComparison.OrdinalIgnoreCase))
                && (query.SqlInstance is null || run.SqlInstance.Equals(query.SqlInstance, StringComparison.OrdinalIgnoreCase))
                && (query.JobName is null || run.JobName.Equals(query.JobName, StringComparison.OrdinalIgnoreCase))
                && (query.Status is null || IsSuccessful(run) == (query.Status == JobQuery.Successful))
                && (query.Search is null || run.ServerName.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                    || run.SqlInstance.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                    || run.JobName.Contains(query.Search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(run => run.StartedAt)
            .ThenBy(run => run.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(run => run.SqlInstance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(run => run.JobName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var offset = (int)Math.Min((query.Page - 1L) * query.PageSize, filtered.Length);
        var items = filtered.Skip(offset).Take(query.PageSize)
            .Select(run => query.IncludeMessage ? run : run with { Message = null })
            .ToArray();
        return Task.FromResult(new PagedResult<JobRun>(items, query.Page, query.PageSize, filtered.Length));
    }

    public Task<IReadOnlyList<JobRuleViolation>> GetViolationsAsync(string? server, CancellationToken cancellationToken)
    {
        IReadOnlyList<JobRuleViolation> violations =
        [
            new("DEZANT101V", @"DEZANT101V\KATHI", "DatabaseBackup - USER_DATABASES - LOG", "PROD", "Cluster",
                "MINIMUM", 24, 2, 0, true, false, "Für die Transaktionsprotokolle ist mindestens ein Lauf pro Stunde vorgesehen."),
            new("DEZANT101V", @"DEZANT101V\KATHI", "TEST_FailedJob", "PROD", "Cluster",
                "NONE", null, 1, 1, false, true, "Jeder ausgeführte Lauf muss erfolgreich abgeschlossen werden."),
            new("DEZSQL204V", "DEZSQL204V", "DatabaseIntegrityCheck", "CONS", "Standalone",
                "EXACT", 1, 0, 0, true, false, "Die Integritätsprüfung soll täglich laufen.")
        ];
        return Task.FromResult<IReadOnlyList<JobRuleViolation>>(server is null
            ? violations
            : violations.Where(item => item.ServerName.Equals(server, StringComparison.OrdinalIgnoreCase)).ToArray());
    }

    private IEnumerable<JobRun> CreateRuns()
    {
        var now = timeProvider.GetLocalNow().DateTime;
        return
        [
            Run("DEZANT101V", @"DEZANT101V\KATHI", "DatabaseBackup - USER_DATABASES - LOG", 15),
            Run("DEZANT101V", @"DEZANT101V\KATHI", "IndexOptimize", 20),
            Run("DEZANT101V", @"DEZANT101V\KATHI", "TEST_FailedJob", 37, "Failed", "Der Schritt Generate Error wurde mit einem Fehler beendet."),
            Run("DEZANT101V", @"DEZANT101V\KATHI", "DatabaseBackup - USER_DATABASES - FULL", 55),
            Run("DEZANT101V", @"DEZANT101V\REPORTING", "DatabaseBackup - USER_DATABASES - FULL", 64),
            Run("DEZANT101V", @"DEZANT101V\KATHI", "DatabaseBackup - USER_DATABASES - LOG", 75),
            Run("DEZSQL204V", "DEZSQL204V", "ExportReporting", 90, "Canceled", "Der Lauf wurde abgebrochen."),
            Run("DEZANT101V", @"DEZANT101V\REPORTING", "UpdateStatistics", 120),
            Run("DEZANT101V", @"DEZANT101V\KATHI", "CleanupHistory", 180),
            Run("DEZSQL207V", "DEZSQL207V", "DatabaseBackup - USER_DATABASES - FULL", 230),
            Run("DEZANT101V", @"DEZANT101V\KATHI", "IndexOptimize", 320, "Failed", "Ein früherer Lauf konnte die Datenbank nicht sperren."),
            Run("DEZSQL204V", "DEZSQL204V", "DatabaseIntegrityCheck", 1600)
        ];

        JobRun Run(string server, string instance, string job, int minutesAgo, string status = "Succeeded", string? message = null)
        {
            var started = now.AddMinutes(-minutesAgo);
            return new JobRun(server, instance, job, started, started.AddSeconds(5), "5 s", status,
                message ?? "Der Job wurde erfolgreich abgeschlossen.");
        }
    }

    private static IEnumerable<JobRun> LatestRuns(IEnumerable<JobRun> runs) =>
        runs.OrderByDescending(run => run.StartedAt)
            .DistinctBy(run => (run.ServerName, run.SqlInstance, run.JobName));

    private static bool IsSuccessful(JobRun run) => run.Status == "Succeeded";
}
