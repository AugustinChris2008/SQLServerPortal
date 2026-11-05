using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public sealed class DemoRestoreTestRepository : IRestoreTestRepository
{
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<RestoreTestDatabase> _databases;

    public DemoRestoreTestRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        var now = timeProvider.GetUtcNow();
        _databases =
        [
            Create(1, "SQL-PROD-01\\MSSQLSERVER", "ERP_Core", "Erfolgreich", true,
                4, 3, "Erfolgreich", null, 842.4m, true, "Success", "Success", now.AddDays(-5), now.AddDays(-5)),
            Create(2, "SQL-PROD-02\\MSSQLSERVER", "DWH_Facts", "Erfolgreich", true,
                2, 2, "Erfolgreich", null, 2418.7m, true, "Success", "Success", now.AddDays(-132), now.AddDays(-132)),
            Create(3, "SQL-LEGACY-01\\MSSQLSERVER", "LegacyBilling", "Fehlgeschlagen", false,
                1, 0, "Fehlgeschlagen", "Nicht erreichbar", 218m, null, "SQL-Server / Datenbank nicht erreichbar", null, now.AddDays(-2), null),
            Create(4, "SQL-UAT-01\\MSSQLSERVER", "ERP_UAT", "Fehlgeschlagen", false,
                2, 0, "Fehlgeschlagen", "DBCC fehlgeschlagen", 196m, true, "Success", "CHECKDB meldet Konsistenzfehler", now.AddDays(-12), null),
            Create(5, "SQL-DEV-01\\DEV", "DevPlayground", "Übersprungen", false,
                1, 0, "Übersprungen", null, 72m, false, "Skipped - Copy only", "Skipped", now.AddDays(-22), null),
            Create(6, "SQL-PROD-01\\MSSQLSERVER", "ERP_Archive", "Nicht getestet", false,
                0, 0, "Kein Ergebnis", null, null, null, null, null, null, null),
            Create(7, "SQL-TEST-01\\MSSQLSERVER", "TestSandbox", "Nicht getestet", false,
                0, 0, "Erfolgreich", null, 1.2m, true, "Success", "Success", now.AddDays(-410), now.AddDays(-410)),
            Create(8, "SQL-AG-DR01\\MSSQLSERVER", "Customer360", "Fehlgeschlagen", false,
                1, 0, "Fehlgeschlagen", "Backup nicht verfügbar", 684m, false, "Backup file does not exist", null, now.AddDays(-8), null)
        ];
    }

    public Task<RestoreTestOverview> GetOverviewAsync(
        string period,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var databases = GetDatabasesForPeriod(period);
        var successful = databases.Count(database => database.CoveredWithinPeriod);
        var windowStart = ResolveWindowStart(period);
        var categories = databases
            .Where(database => database.FailureCategory is not null
                && (windowStart is null
                    || database.LatestAttemptAtUtc is null
                    || database.LatestAttemptAtUtc >= windowStart))
            .GroupBy(database => database.FailureCategory!)
            .Select(group => new RestoreFailureCategory(group.Key, group.Count()))
            .OrderByDescending(category => category.DatabaseCount)
            .ToArray();
        return Task.FromResult(new RestoreTestOverview(
            "Demo",
            _timeProvider.GetUtcNow(),
            period,
            windowStart,
            new RestoreTestMetrics(
                databases.Length,
                successful,
                databases.Length - successful,
                databases.Count(database => database.LatestAttemptStatus == "Fehlgeschlagen"
                    && (windowStart is null || database.LatestAttemptAtUtc is null || database.LatestAttemptAtUtc >= windowStart)),
                databases.Count(database => database.PeriodStatus == "Übersprungen"),
                databases.Count(database => database.PeriodStatus == "Nicht getestet"),
                databases.Sum(database => database.AttemptCountWithinPeriod),
                databases.Count(database => database.AttemptCountWithinPeriod == 0),
                databases.Count(database => database.AttemptCountWithinPeriod == 1),
                databases.Count(database => database.AttemptCountWithinPeriod > 1),
                databases.Sum(database => Math.Max(0, database.AttemptCountWithinPeriod - 1)),
                Percentage(successful, databases.Length)),
            categories));
    }

    public Task<PagedResult<RestoreTestDatabase>> GetDatabasesAsync(
        RestoreTestQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<RestoreTestDatabase> result = GetDatabasesForPeriod(query.Period);
        if (query.Search is not null)
        {
            result = result.Where(database =>
                database.SourceServer.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || database.DatabaseName.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || database.RestoreResult?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true
                || database.DbccResult?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true
                || database.FailureCategory?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (query.Status is not null)
        {
            result = result.Where(database => database.PeriodStatus == query.Status);
        }

        if (query.Selection is not null)
        {
            result = result.Where(database => database.SelectionStatus == query.Selection);
        }

        result = query.SortBy switch
        {
            "sourceServer" => query.SortDescending
                ? result.OrderByDescending(database => database.SourceServer)
                : result.OrderBy(database => database.SourceServer),
            "databaseName" => query.SortDescending
                ? result.OrderByDescending(database => database.DatabaseName)
                : result.OrderBy(database => database.DatabaseName),
            "latestAttemptAtUtc" => query.SortDescending
                ? result.OrderByDescending(database => database.LatestAttemptAtUtc)
                : result.OrderBy(database => database.LatestAttemptAtUtc),
            "lastSuccessfulAtUtc" => query.SortDescending
                ? result.OrderByDescending(database => database.LastSuccessfulAtUtc)
                : result.OrderBy(database => database.LastSuccessfulAtUtc),
            "attemptCountWithinPeriod" => query.SortDescending
                ? result.OrderByDescending(database => database.AttemptCountWithinPeriod)
                : result.OrderBy(database => database.AttemptCountWithinPeriod),
            _ => result.OrderBy(database => database.PeriodStatus == "Fehlgeschlagen" ? 0
                : database.PeriodStatus == "Übersprungen" ? 1
                : database.PeriodStatus == "Nicht getestet" ? 2 : 3)
        };

        var items = result.ToArray();
        return Task.FromResult(new PagedResult<RestoreTestDatabase>(
            items.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(),
            query.Page,
            query.PageSize,
            items.Length));
    }

    public Task<RestoreTestHistoryDetail?> GetHistoryAsync(
        int databaseId,
        string period,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var database = GetDatabasesForPeriod(period).SingleOrDefault(candidate => candidate.Id == databaseId);
        if (database is null)
        {
            return Task.FromResult<RestoreTestHistoryDetail?>(null);
        }

        var windowStart = ResolveWindowStart(period);
        var history = CreateHistory(databaseId)
            .Select(entry => entry with
            {
                WithinPeriod = entry.AttemptAtUtc is not null
                    && (windowStart is null || entry.AttemptAtUtc >= windowStart)
            })
            .ToArray();
        return Task.FromResult<RestoreTestHistoryDetail?>(new(database, history));
    }

    private RestoreTestDatabase[] GetDatabasesForPeriod(string period)
    {
        var windowStart = ResolveWindowStart(period);
        return _databases.Select(database =>
        {
            var history = CreateHistory(database.Id);
            var entriesInPeriod = history
                .Where(entry => entry.AttemptAtUtc is not null
                    && (windowStart is null || entry.AttemptAtUtc >= windowStart))
                .ToArray();
            var successfulAttempts = entriesInPeriod.Count(entry => entry.Status == "Erfolgreich");
            var covered = successfulAttempts > 0;
            var latestInPeriod = entriesInPeriod.FirstOrDefault();
            var periodStatus = covered
                ? "Erfolgreich"
                : latestInPeriod is null
                    ? "Nicht getestet"
                    : latestInPeriod.Status == "Übersprungen"
                        ? "Übersprungen"
                        : "Fehlgeschlagen";
            return database with
            {
                PeriodStatus = periodStatus,
                CoveredWithinPeriod = covered,
                SelectionStatus = RestoreFrequency(entriesInPeriod.Length),
                AttemptCountWithinPeriod = entriesInPeriod.Length,
                SuccessfulAttemptCountWithinPeriod = successfulAttempts
            };
        }).ToArray();
    }

    private IReadOnlyList<RestoreTestHistoryEntry> CreateHistory(int databaseId)
    {
        var now = _timeProvider.GetUtcNow();
        return databaseId switch
        {
            1 =>
            [
                History(1, "Erfolgreich", null, now.AddDays(-5), "Success", "Success"),
                History(2, "Erfolgreich", null, now.AddDays(-90), "Success", "Success"),
                History(3, "Erfolgreich", null, now.AddDays(-220), "Success", "Success"),
                History(4, "Fehlgeschlagen", "DBCC fehlgeschlagen", now.AddDays(-300),
                    "Success", "CHECKDB meldet Konsistenzfehler")
            ],
            2 =>
            [
                History(1, "Erfolgreich", null, now.AddDays(-132), "Success", "Success"),
                History(2, "Erfolgreich", null, now.AddDays(-250), "Success", "Success")
            ],
            3 => [History(1, "Fehlgeschlagen", "Nicht erreichbar", now.AddDays(-2),
                "SQL-Server / Datenbank nicht erreichbar", null)],
            4 =>
            [
                History(1, "Fehlgeschlagen", "DBCC fehlgeschlagen", now.AddDays(-12),
                    "Success", "CHECKDB meldet Konsistenzfehler"),
                History(2, "Fehlgeschlagen", "Restore fehlgeschlagen", now.AddDays(-170),
                    "Restore aborted", null)
            ],
            5 => [History(1, "Übersprungen", null, now.AddDays(-22), "Skipped - Copy only", "Skipped")],
            7 => [History(1, "Erfolgreich", null, now.AddDays(-410), "Success", "Success", false)],
            8 => [History(1, "Fehlgeschlagen", "Backup nicht verfügbar", now.AddDays(-8),
                "Backup file does not exist", null)],
            _ => []
        };
    }

    private DateTimeOffset? ResolveWindowStart(string period) => period switch
    {
        "730" => _timeProvider.GetUtcNow().AddDays(-730),
        "1095" => _timeProvider.GetUtcNow().AddDays(-1095),
        "1825" => _timeProvider.GetUtcNow().AddDays(-1825),
        "all" => null,
        _ => _timeProvider.GetUtcNow().AddDays(-365)
    };

    private static string RestoreFrequency(int attempts) => attempts switch
    {
        0 => "Nie wiederhergestellt",
        1 => "Einmal wiederhergestellt",
        _ => "Mehrfach wiederhergestellt"
    };

    private static RestoreTestDatabase Create(
        int id,
        string sourceServer,
        string databaseName,
        string periodStatus,
        bool covered,
        int attemptCountWithinPeriod,
        int successfulAttemptCountWithinPeriod,
        string latestStatus,
        string? failureCategory,
        decimal? sizeGb,
        bool? backupFileExists,
        string? restoreResult,
        string? dbccResult,
        DateTimeOffset? latestAttemptAtUtc,
        DateTimeOffset? lastSuccessfulAtUtc) =>
        new(
            id,
            sourceServer,
            databaseName,
            periodStatus,
            covered,
            attemptCountWithinPeriod switch
            {
                0 => "Nie wiederhergestellt",
                1 => "Einmal wiederhergestellt",
                _ => "Mehrfach wiederhergestellt"
            },
            attemptCountWithinPeriod,
            successfulAttemptCountWithinPeriod,
            0,
            latestStatus,
            failureCategory,
            "RESTORE-TEST-01",
            sizeGb,
            backupFileExists,
            restoreResult,
            dbccResult,
            latestAttemptAtUtc,
            lastSuccessfulAtUtc,
            latestAttemptAtUtc is null ? null : "00:04:31",
            latestAttemptAtUtc is null ? null : "00:01:12",
            latestAttemptAtUtc?.AddDays(-1),
            latestAttemptAtUtc is null ? null : @"\\backup\full\database.bak",
            null,
            null,
            null,
            null);

    private static RestoreTestHistoryEntry History(
        int sequence,
        string status,
        string? failureCategory,
        DateTimeOffset attemptAtUtc,
        string? restoreResult,
        string? dbccResult,
        bool withinPeriod = true) =>
        new(
            sequence,
            status,
            failureCategory,
            withinPeriod,
            "RESTORE-TEST-01",
            null,
            true,
            restoreResult,
            dbccResult,
            attemptAtUtc,
            "00:04:31",
            "00:01:12",
            attemptAtUtc.AddDays(-1),
            @"\\backup\full\database.bak",
            null,
            null,
            null,
            null);

    private static decimal Percentage(int value, int total) =>
        total == 0 ? 0 : Math.Round(value * 100m / total, 1);
}
