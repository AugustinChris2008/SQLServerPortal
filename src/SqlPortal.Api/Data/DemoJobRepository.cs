using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public sealed class DemoJobRepository(TimeProvider timeProvider) : IJobRepository
{
    public Task<IReadOnlyList<JobServerSummary>> GetServersAsync(
        int? historyDays, bool includeInactive, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetLocalNow();
        var runs = CreateRuns(now.DateTime);
        var periodRuns = FilterByHistoryDays(runs, now.DateTime, historyDays);
        var periodByServer = periodRuns.ToLookup(run => run.ServerName, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<JobServerSummary> items = (includeInactive ? runs : periodRuns)
            .Select(run => run.ServerName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(serverName => serverName, StringComparer.OrdinalIgnoreCase)
            .Select(serverName =>
            {
                var serverRuns = periodByServer[serverName];
                var latest = LatestRuns(serverRuns).ToArray();
                (string? Environment, string? ServerType, DateTimeOffset? LastScan) inventory = serverName switch
                {
                    "DEZANT101V" => ("PROD", "Cluster", now.AddMinutes(-45)),
                    "DEZSQL204V" => ("CONS", "Standalone", now.AddHours(-2)),
                    "DEZSQL207V" => ("TEST", "Standalone", now.AddHours(-4)),
                    _ => (null, null, null)
                };
                return new JobServerSummary(serverName, latest.Length, latest.Count(IsSuccessful),
                    serverRuns.Any(run => !IsSuccessful(run)), inventory.Environment, inventory.ServerType,
                    inventory.LastScan?.ToUniversalTime());
            })
            .ToArray();
        return Task.FromResult(items);
    }

    public Task<PagedResult<JobRun>> GetRunsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetLocalNow().DateTime;
        var runs = FilterByHistoryDays(CreateRuns(now), now, query.HistoryDays);
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

    public Task<IReadOnlyList<JobMonitoringResult>> GetMonitoringAsync(string? server, CancellationToken cancellationToken)
    {
        JobMonitoringResult[] rules =
        [
            new("DEZANT101V", @"DEZANT101V\KATHI", "DatabaseBackup - USER_DATABASES - LOG", "PROD", "Cluster",
                "MINIMUM", 24, 24, true, 0, 0, 0, "Für die Transaktionsprotokolle ist mindestens ein Lauf pro Stunde vorgesehen."),
            new("DEZANT101V", @"DEZANT101V\KATHI", "TEST_FailedJob", "PROD", "Cluster",
                "NONE", null, 24, true, 0, 0, 0, "Fehlgeschlagene Testläufe werden gemeldet."),
            new("DEZSQL204V", "DEZSQL204V", "DatabaseIntegrityCheck", "CONS", "Standalone",
                "EXACT", 1, 24, false, 0, 0, 0, "Die Integritätsprüfung soll täglich laufen."),
            new("DEZSQL204V", "DEZSQL204V", "WeeklyCleanup", "CONS", "Standalone",
                "MINIMUM", 1, 168, true, 0, 0, 0, "Die Bereinigung soll mindestens einmal pro Woche laufen."),
            new("DEZSQL204V", "DEZSQL204V", "ExportReporting", "CONS", "Standalone",
                "NONE", null, 24, true, 0, 0, 0, null)
        ];
        var now = timeProvider.GetLocalNow().DateTime;
        var runs = CreateRuns(now).ToLookup(run => (run.ServerName, run.SqlInstance, run.JobName));
        IReadOnlyList<JobMonitoringResult> results = rules
            .Where(rule => server is null || rule.ServerName.Equals(server, StringComparison.OrdinalIgnoreCase))
            .Select(rule =>
            {
                var windowStart = now.AddHours(-rule.MonitoringWindowHours);
                var windowRuns = runs[(rule.ServerName, rule.SqlInstance, rule.JobName)]
                    .Where(run => run.StartedAt >= windowStart && run.StartedAt <= now)
                    .ToArray();
                return rule with
                {
                    ActualRuns = windowRuns.Length,
                    SuccessfulRuns = windowRuns.Count(IsSuccessful),
                    FailedRuns = windowRuns.Count(run => run.Status == "Failed")
                };
            })
            .ToArray();
        return Task.FromResult(results);
    }

    private static IEnumerable<JobRun> CreateRuns(DateTime now)
    {
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
            Run("DEZSQL204V", "DEZSQL204V", "DatabaseIntegrityCheck", 1600),
            Run("DEZSQL204V", "DEZSQL204V", "WeeklyCleanup", 4 * 1440),
            Run("DEZSQL204V", "DEZSQL204V", "WeeklyCleanup", 10 * 1440, "Failed", "Die Bereinigung ist fehlgeschlagen.")
        ];

        JobRun Run(string server, string instance, string job, int minutesAgo, string status = "Succeeded", string? message = null)
        {
            var started = now.AddMinutes(-minutesAgo);
            return new JobRun(server, instance, job, started, started.AddSeconds(5), "5 s", status,
                message ?? "Der Job wurde erfolgreich abgeschlossen.");
        }
    }

    private static IEnumerable<JobRun> FilterByHistoryDays(IEnumerable<JobRun> runs, DateTime now, int? historyDays) =>
        historyDays is int days
            ? runs.Where(run => run.StartedAt >= now.AddDays(-days) && run.StartedAt <= now)
            : runs;

    private static IEnumerable<JobRun> LatestRuns(IEnumerable<JobRun> runs) =>
        runs.OrderByDescending(run => run.StartedAt)
            .DistinctBy(run => (run.ServerName, run.SqlInstance, run.JobName));

    private static bool IsSuccessful(JobRun run) => run.Status == "Succeeded";
}
