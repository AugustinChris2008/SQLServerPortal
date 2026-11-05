using Dapper;
using Microsoft.Data.SqlClient;
using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public sealed class RestoreTestRepository : IRestoreTestRepository
{
    private readonly string _portalConnectionString;
    private readonly string _restoreTestConnectionString;
    private readonly TimeProvider _timeProvider;

    public RestoreTestRepository(IConfiguration configuration, TimeProvider timeProvider)
    {
        _portalConnectionString = configuration.GetConnectionString("PortalDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PortalDatabase is required in SqlServer mode.");
        _restoreTestConnectionString = configuration.GetConnectionString("RestoreTestDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:RestoreTestDatabase is required in SqlServer mode.");
        _timeProvider = timeProvider;
    }

    public async Task<RestoreTestOverview> GetOverviewAsync(
        string period,
        CancellationToken cancellationToken)
    {
        var windowStart = ResolveWindowStart(period);
        var databases = await LoadDatabasesAsync(windowStart, cancellationToken);
        var successfullyTested = databases.Count(database => database.CoveredWithinPeriod);
        var failureCategories = databases
            .Where(database => IsCurrentFailure(database, windowStart))
            .GroupBy(database => database.FailureCategory ?? "Sonstiger Fehler")
            .Select(group => new RestoreFailureCategory(group.Key, group.Count()))
            .OrderByDescending(category => category.DatabaseCount)
            .ThenBy(category => category.Category)
            .ToArray();

        return new RestoreTestOverview(
            "SqlServer",
            _timeProvider.GetUtcNow(),
            period,
            windowStart,
            new RestoreTestMetrics(
                databases.Count,
                successfullyTested,
                databases.Count - successfullyTested,
                databases.Count(database => IsCurrentFailure(database, windowStart)),
                databases.Count(database => database.PeriodStatus == "Übersprungen"),
                databases.Count(database => database.PeriodStatus == "Nicht getestet"),
                databases.Sum(database => database.AttemptCountWithinPeriod),
                databases.Count(database => database.AttemptCountWithinPeriod == 0),
                databases.Count(database => database.AttemptCountWithinPeriod == 1),
                databases.Count(database => database.AttemptCountWithinPeriod > 1),
                databases.Sum(database => Math.Max(0, database.AttemptCountWithinPeriod - 1)),
                Percentage(successfullyTested, databases.Count)),
            failureCategories);
    }

    public async Task<PagedResult<RestoreTestDatabase>> GetDatabasesAsync(
        RestoreTestQuery query,
        CancellationToken cancellationToken)
    {
        var windowStart = ResolveWindowStart(query.Period);
        IEnumerable<RestoreTestDatabase> result = await LoadDatabasesAsync(windowStart, cancellationToken);

        if (query.Search is not null)
        {
            result = result.Where(database =>
                database.SourceServer.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || database.DatabaseName.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || Contains(database.RestoreResult, query.Search)
                || Contains(database.DbccResult, query.Search)
                || Contains(database.FailureCategory, query.Search));
        }

        if (query.Status is not null)
        {
            result = result.Where(database =>
                string.Equals(database.PeriodStatus, query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Selection is not null)
        {
            result = result.Where(database =>
                string.Equals(database.SelectionStatus, query.Selection, StringComparison.OrdinalIgnoreCase));
        }

        result = Sort(result, query.SortBy, query.SortDescending);
        var items = result.ToArray();
        var pageItems = items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();
        return new PagedResult<RestoreTestDatabase>(
            pageItems,
            query.Page,
            query.PageSize,
            items.Length);
    }

    public async Task<RestoreTestHistoryDetail?> GetHistoryAsync(
        int databaseId,
        string period,
        CancellationToken cancellationToken)
    {
        var windowStart = ResolveWindowStart(period);
        var database = (await LoadDatabasesAsync(windowStart, cancellationToken))
            .SingleOrDefault(candidate => candidate.Id == databaseId);
        if (database is null)
        {
            return null;
        }

        var history = await LoadHistoryEntriesAsync(database, windowStart, cancellationToken);
        return new RestoreTestHistoryDetail(database, history);
    }

    private async Task<IReadOnlyList<RestoreTestHistoryEntry>> LoadHistoryEntriesAsync(
        RestoreTestDatabase database,
        DateTimeOffset? windowStart,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                TestServer = NULLIF(LTRIM(RTRIM(result.TestServer)), ''),
                BackupFileExists = NULLIF(LTRIM(RTRIM(result.BackupFileExists)), ''),
                result.SizeGB,
                RestoreResult = NULLIF(LTRIM(RTRIM(result.RestoreResult)), ''),
                DbccResult = NULLIF(LTRIM(RTRIM(result.DbccResult)), ''),
                RestoreElapsed = CONVERT(varchar(30), result.RestoreElapsed, 114),
                DbccElapsed = CONVERT(varchar(30), result.DbccElapsed, 114),
                AttemptAtUtc = CASE WHEN attempt.AttemptAt IS NULL THEN NULL ELSE
                    SWITCHOFFSET(attempt.AttemptAt AT TIME ZONE 'Central European Standard Time', '+00:00') END,
                FullDateUtc = CASE WHEN result.FullDate IS NULL THEN NULL ELSE
                    SWITCHOFFSET(result.FullDate AT TIME ZONE 'Central European Standard Time', '+00:00') END,
                FullPath = NULLIF(LTRIM(RTRIM(result.FullPath)), ''),
                DiffDateUtc = CASE WHEN result.DiffDate IS NULL THEN NULL ELSE
                    SWITCHOFFSET(result.DiffDate AT TIME ZONE 'Central European Standard Time', '+00:00') END,
                DiffPath = NULLIF(LTRIM(RTRIM(result.DiffPath)), ''),
                LogDates = NULLIF(LTRIM(RTRIM(result.LogDates)), ''),
                LogPaths = NULLIF(LTRIM(RTRIM(result.LogPaths)), '')
            FROM [dbo].[RestoreResults] AS result
            CROSS APPLY
            (
                SELECT AttemptAt = COALESCE(
                    result.DbccEnd,
                    result.RestoreEnd,
                    result.DbccStart,
                    result.RestoreStart,
                    result.DiffDate,
                    result.FullDate)
            ) AS attempt
            WHERE LTRIM(RTRIM(result.SourceServer)) = @SourceServer
              AND LTRIM(RTRIM(result.[Database])) = @DatabaseName
            ORDER BY CASE WHEN attempt.AttemptAt IS NULL THEN 1 ELSE 0 END,
                attempt.AttemptAt DESC,
                result.RestoreResult DESC;
            """;

        await using var connection = new SqlConnection(_restoreTestConnectionString);
        var command = new CommandDefinition(
            sql,
            new { database.SourceServer, database.DatabaseName },
            cancellationToken: cancellationToken);
        var rows = (await connection.QueryAsync<HistoryResultRow>(command)).AsList();
        return rows.Select((row, index) =>
        {
            var status = ClassifyAttempt(row.RestoreResult, row.DbccResult);
            return new RestoreTestHistoryEntry(
                index + 1,
                status,
                status == "Fehlgeschlagen"
                    ? ClassifyFailure(row.RestoreResult, row.DbccResult, row.BackupFileExists)
                    : null,
                row.AttemptAtUtc is not null
                    && (windowStart is null || row.AttemptAtUtc >= windowStart),
                row.TestServer,
                row.SizeGb,
                ParseBoolean(row.BackupFileExists),
                row.RestoreResult,
                row.DbccResult,
                row.AttemptAtUtc,
                row.RestoreElapsed,
                row.DbccElapsed,
                row.FullDateUtc,
                row.FullPath,
                row.DiffDateUtc,
                row.DiffPath,
                row.LogDates,
                row.LogPaths);
        }).ToArray();
    }

    private async Task<IReadOnlyList<RestoreTestDatabase>> LoadDatabasesAsync(
        DateTimeOffset? windowStart,
        CancellationToken cancellationToken)
    {
        const string inventorySql = """
            ;WITH RankedInventory AS
            (
                SELECT
                    sourceKey.SourceServer,
                    DatabaseName = LTRIM(RTRIM(source.sqldb_name)),
                    InventoryRank = ROW_NUMBER() OVER
                    (
                        PARTITION BY sourceKey.SourceServer, LTRIM(RTRIM(source.sqldb_name))
                        ORDER BY CASE WHEN source.scan_date IS NULL THEN 1 ELSE 0 END,
                            source.scan_date DESC
                    )
                FROM [dbo].[v_SQLPortalDataOverview] AS source
                CROSS APPLY
                (
                    SELECT SourceServer = CASE
                        WHEN NULLIF(LTRIM(RTRIM(source.sqlserverinstance)), '') IS NULL
                            THEN LTRIM(RTRIM(source.hostname))
                        WHEN CHARINDEX('\', LTRIM(RTRIM(source.sqlserverinstance))) > 0
                            THEN LTRIM(RTRIM(source.sqlserverinstance))
                        ELSE CONCAT(LTRIM(RTRIM(source.hostname)), '\',
                            LTRIM(RTRIM(source.sqlserverinstance)))
                    END
                ) AS sourceKey
                WHERE NULLIF(LTRIM(RTRIM(source.hostname)), '') IS NOT NULL
                  AND NULLIF(LTRIM(RTRIM(source.sqldb_name)), '') IS NOT NULL
            )
            SELECT SourceServer, DatabaseName
            FROM RankedInventory
            WHERE InventoryRank = 1;
            """;

        const string resultsSql = """
            ;WITH ClassifiedResults AS
            (
                SELECT
                    SourceServer = LTRIM(RTRIM(SourceServer)),
                    TestServer = NULLIF(LTRIM(RTRIM(TestServer)), ''),
                    DatabaseName = LTRIM(RTRIM([Database])),
                    BackupFileExists = NULLIF(LTRIM(RTRIM(BackupFileExists)), ''),
                    SizeGB,
                    RestoreResult = NULLIF(LTRIM(RTRIM(RestoreResult)), ''),
                    DbccResult = NULLIF(LTRIM(RTRIM(DbccResult)), ''),
                    RestoreElapsed = CONVERT(varchar(30), RestoreElapsed, 114),
                    DbccElapsed = CONVERT(varchar(30), DbccElapsed, 114),
                    FullDate,
                    FullPath = NULLIF(LTRIM(RTRIM(FullPath)), ''),
                    DiffDate,
                    DiffPath = NULLIF(LTRIM(RTRIM(DiffPath)), ''),
                    LogDates = NULLIF(LTRIM(RTRIM(LogDates)), ''),
                    LogPaths = NULLIF(LTRIM(RTRIM(LogPaths)), ''),
                    AttemptAt = COALESCE(DbccEnd, RestoreEnd, DbccStart, RestoreStart, DiffDate, FullDate),
                    IsSuccessful = CASE
                        WHEN UPPER(LTRIM(RTRIM(COALESCE(RestoreResult, '')))) = 'SUCCESS'
                         AND UPPER(LTRIM(RTRIM(COALESCE(DbccResult, '')))) = 'SUCCESS'
                            THEN 1 ELSE 0
                    END
                FROM [dbo].[RestoreResults]
                WHERE NULLIF(LTRIM(RTRIM(SourceServer)), '') IS NOT NULL
                  AND NULLIF(LTRIM(RTRIM([Database])), '') IS NOT NULL
            ),
            RankedResults AS
            (
                SELECT
                    classified.*,
                    ResultRank = ROW_NUMBER() OVER
                    (
                        PARTITION BY classified.SourceServer, classified.DatabaseName
                        ORDER BY CASE WHEN classified.AttemptAt IS NULL THEN 1 ELSE 0 END,
                            classified.AttemptAt DESC,
                            classified.RestoreResult DESC
                    )
                FROM ClassifiedResults AS classified
            ),
            ResultAggregates AS
            (
                SELECT
                    SourceServer,
                    DatabaseName,
                    LastSuccessfulAt = MAX(CASE WHEN IsSuccessful = 1 THEN AttemptAt END),
                    AttemptCountWithinPeriod = SUM(CASE
                        WHEN AttemptAt IS NOT NULL
                         AND (@WindowStartUtc IS NULL OR
                            SWITCHOFFSET(AttemptAt AT TIME ZONE 'Central European Standard Time', '+00:00') >= @WindowStartUtc)
                            THEN 1 ELSE 0 END),
                    SuccessfulAttemptCountWithinPeriod = SUM(CASE
                        WHEN IsSuccessful = 1
                         AND AttemptAt IS NOT NULL
                         AND (@WindowStartUtc IS NULL OR
                            SWITCHOFFSET(AttemptAt AT TIME ZONE 'Central European Standard Time', '+00:00') >= @WindowStartUtc)
                            THEN 1 ELSE 0 END),
                    UndatedAttemptCount = SUM(CASE WHEN AttemptAt IS NULL THEN 1 ELSE 0 END)
                FROM ClassifiedResults
                GROUP BY SourceServer, DatabaseName
            )
            SELECT
                latest.SourceServer,
                latest.TestServer,
                latest.DatabaseName,
                latest.BackupFileExists,
                latest.SizeGB,
                latest.RestoreResult,
                latest.DbccResult,
                latest.RestoreElapsed,
                latest.DbccElapsed,
                LatestAttemptAtUtc = CASE WHEN latest.AttemptAt IS NULL THEN NULL ELSE
                    SWITCHOFFSET(latest.AttemptAt AT TIME ZONE 'Central European Standard Time', '+00:00') END,
                LastSuccessfulAtUtc = CASE WHEN aggregated.LastSuccessfulAt IS NULL THEN NULL ELSE
                    SWITCHOFFSET(aggregated.LastSuccessfulAt AT TIME ZONE 'Central European Standard Time', '+00:00') END,
                aggregated.AttemptCountWithinPeriod,
                aggregated.SuccessfulAttemptCountWithinPeriod,
                aggregated.UndatedAttemptCount,
                FullDateUtc = CASE WHEN latest.FullDate IS NULL THEN NULL ELSE
                    SWITCHOFFSET(latest.FullDate AT TIME ZONE 'Central European Standard Time', '+00:00') END,
                latest.FullPath,
                DiffDateUtc = CASE WHEN latest.DiffDate IS NULL THEN NULL ELSE
                    SWITCHOFFSET(latest.DiffDate AT TIME ZONE 'Central European Standard Time', '+00:00') END,
                latest.DiffPath,
                latest.LogDates,
                latest.LogPaths
            FROM RankedResults AS latest
            INNER JOIN ResultAggregates AS aggregated
                ON aggregated.SourceServer = latest.SourceServer
               AND aggregated.DatabaseName = latest.DatabaseName
            WHERE latest.ResultRank = 1;
            """;

        await using var portalConnection = new SqlConnection(_portalConnectionString);
        var inventoryCommand = new CommandDefinition(
            inventorySql,
            cancellationToken: cancellationToken);
        var inventory = (await portalConnection.QueryAsync<InventoryDatabaseRow>(inventoryCommand)).AsList();

        await using var restoreConnection = new SqlConnection(_restoreTestConnectionString);
        var resultsCommand = new CommandDefinition(
            resultsSql,
            new { WindowStartUtc = windowStart },
            cancellationToken: cancellationToken);
        var results = (await restoreConnection.QueryAsync<RestoreResultRow>(resultsCommand))
            .GroupBy(
                result => DatabaseKey(result.SourceServer, result.DatabaseName),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        return inventory
            .Select(database => CreateDatabase(
                database,
                results.GetValueOrDefault(DatabaseKey(database.SourceServer, database.DatabaseName)),
                windowStart))
            .ToArray();
    }

    private static RestoreTestDatabase CreateDatabase(
        InventoryDatabaseRow inventory,
        RestoreResultRow? result,
        DateTimeOffset? windowStart)
    {
        var latestAttemptStatus = ClassifyLatestAttempt(result);
        var coveredWithinPeriod = result?.LastSuccessfulAtUtc is not null
            && (windowStart is null || result.LastSuccessfulAtUtc >= windowStart);
        var hasCurrentAttempt = result is not null
            && (windowStart is null
                || result.LatestAttemptAtUtc >= windowStart
                || result.LatestAttemptAtUtc is null);
        var periodStatus = coveredWithinPeriod
            ? "Erfolgreich"
            : !hasCurrentAttempt
                ? "Nicht getestet"
                : latestAttemptStatus == "Übersprungen"
                    ? "Übersprungen"
                    : "Fehlgeschlagen";
        var failureCategory = latestAttemptStatus == "Fehlgeschlagen"
            ? ClassifyFailure(result)
            : null;
        var attemptCountWithinPeriod = result?.AttemptCountWithinPeriod ?? 0;

        return new RestoreTestDatabase(
            StableId(DatabaseKey(inventory.SourceServer, inventory.DatabaseName)),
            inventory.SourceServer,
            inventory.DatabaseName,
            periodStatus,
            coveredWithinPeriod,
            SelectionStatus(attemptCountWithinPeriod),
            attemptCountWithinPeriod,
            result?.SuccessfulAttemptCountWithinPeriod ?? 0,
            result?.UndatedAttemptCount ?? 0,
            latestAttemptStatus,
            failureCategory,
            result?.TestServer,
            result?.SizeGb,
            ParseBoolean(result?.BackupFileExists),
            result?.RestoreResult,
            result?.DbccResult,
            result?.LatestAttemptAtUtc,
            result?.LastSuccessfulAtUtc,
            result?.RestoreElapsed,
            result?.DbccElapsed,
            result?.FullDateUtc,
            result?.FullPath,
            result?.DiffDateUtc,
            result?.DiffPath,
            result?.LogDates,
            result?.LogPaths);
    }

    private static string ClassifyLatestAttempt(RestoreResultRow? result)
    {
        if (result is null)
        {
            return "Kein Ergebnis";
        }

        return ClassifyAttempt(result.RestoreResult, result.DbccResult);
    }

    private static string ClassifyAttempt(string? restoreResult, string? dbccResult)
    {
        if (IsSuccess(restoreResult) && IsSuccess(dbccResult))
        {
            return "Erfolgreich";
        }

        if (Contains(restoreResult, "skipped") || Contains(dbccResult, "skipped"))
        {
            return "Übersprungen";
        }

        return "Fehlgeschlagen";
    }

    private static string ClassifyFailure(RestoreResultRow? result)
    {
        if (result is null)
        {
            return "Kein Ergebnis";
        }

        return ClassifyFailure(result.RestoreResult, result.DbccResult, result.BackupFileExists);
    }

    private static string ClassifyFailure(
        string? restoreResult,
        string? dbccResult,
        string? backupFileExists)
    {
        var combined = $"{restoreResult} {dbccResult}";
        if (combined.Contains("nicht erreichbar", StringComparison.OrdinalIgnoreCase))
        {
            return "Nicht erreichbar";
        }

        if (ParseBoolean(backupFileExists) is false
            || combined.Contains("backup", StringComparison.OrdinalIgnoreCase))
        {
            return "Backup nicht verfügbar";
        }

        if (!IsSuccess(restoreResult))
        {
            return "Restore fehlgeschlagen";
        }

        if (!IsSuccess(dbccResult))
        {
            return "DBCC fehlgeschlagen";
        }

        return "Sonstiger Fehler";
    }

    private static IEnumerable<RestoreTestDatabase> Sort(
        IEnumerable<RestoreTestDatabase> source,
        string sortBy,
        bool descending)
    {
        Func<RestoreTestDatabase, object?> selector = sortBy.ToLowerInvariant() switch
        {
            "sourceserver" => database => database.SourceServer,
            "databasename" => database => database.DatabaseName,
            "latestattemptatutc" => database => database.LatestAttemptAtUtc,
            "lastsuccessfulatutc" => database => database.LastSuccessfulAtUtc,
            "sizegb" => database => database.SizeGb,
            "attemptcountwithinperiod" => database => database.AttemptCountWithinPeriod,
            _ => database => PeriodStatusRank(database.PeriodStatus)
        };
        var ordered = descending
            ? source.OrderByDescending(selector)
            : source.OrderBy(selector);
        return ordered
            .ThenBy(database => database.SourceServer)
            .ThenBy(database => database.DatabaseName);
    }

    private static int PeriodStatusRank(string status) => status switch
    {
        "Fehlgeschlagen" => 0,
        "Übersprungen" => 1,
        "Nicht getestet" => 2,
        _ => 3
    };

    private static string SelectionStatus(int attemptCount) => attemptCount switch
    {
        0 => "Nie wiederhergestellt",
        1 => "Einmal wiederhergestellt",
        _ => "Mehrfach wiederhergestellt"
    };

    private static bool IsSuccess(string? value) =>
        string.Equals(value?.Trim(), "Success", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentFailure(
        RestoreTestDatabase database,
        DateTimeOffset? windowStart) =>
        database.LatestAttemptStatus == "Fehlgeschlagen"
        && (windowStart is null
            || database.LatestAttemptAtUtc is null
            || database.LatestAttemptAtUtc >= windowStart);

    private DateTimeOffset? ResolveWindowStart(string period) => period switch
    {
        "730" => _timeProvider.GetUtcNow().AddDays(-730),
        "1095" => _timeProvider.GetUtcNow().AddDays(-1095),
        "1825" => _timeProvider.GetUtcNow().AddDays(-1825),
        "all" => null,
        _ => _timeProvider.GetUtcNow().AddDays(-365)
    };

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static bool? ParseBoolean(string? value) => value?.Trim() switch
    {
        "1" => true,
        "0" => false,
        _ => null
    };

    private static string DatabaseKey(string sourceServer, string databaseName) =>
        $"{sourceServer.Trim()}|{databaseName.Trim()}";

    private static int StableId(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value.ToUpperInvariant())
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return (int)(hash & 2147483647u);
    }

    private static decimal Percentage(int value, int total) =>
        total == 0 ? 0 : Math.Round(value * 100m / total, 1);

    private sealed class InventoryDatabaseRow
    {
        public string SourceServer { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
    }

    private sealed class RestoreResultRow
    {
        public string SourceServer { get; init; } = string.Empty;
        public string DatabaseName { get; init; } = string.Empty;
        public string? TestServer { get; init; }
        public string? BackupFileExists { get; init; }
        public decimal? SizeGb { get; init; }
        public string? RestoreResult { get; init; }
        public string? DbccResult { get; init; }
        public DateTimeOffset? LatestAttemptAtUtc { get; init; }
        public DateTimeOffset? LastSuccessfulAtUtc { get; init; }
        public int AttemptCountWithinPeriod { get; init; }
        public int SuccessfulAttemptCountWithinPeriod { get; init; }
        public int UndatedAttemptCount { get; init; }
        public string? RestoreElapsed { get; init; }
        public string? DbccElapsed { get; init; }
        public DateTimeOffset? FullDateUtc { get; init; }
        public string? FullPath { get; init; }
        public DateTimeOffset? DiffDateUtc { get; init; }
        public string? DiffPath { get; init; }
        public string? LogDates { get; init; }
        public string? LogPaths { get; init; }
    }

    private sealed class HistoryResultRow
    {
        public string? TestServer { get; init; }
        public string? BackupFileExists { get; init; }
        public decimal? SizeGb { get; init; }
        public string? RestoreResult { get; init; }
        public string? DbccResult { get; init; }
        public DateTimeOffset? AttemptAtUtc { get; init; }
        public string? RestoreElapsed { get; init; }
        public string? DbccElapsed { get; init; }
        public DateTimeOffset? FullDateUtc { get; init; }
        public string? FullPath { get; init; }
        public DateTimeOffset? DiffDateUtc { get; init; }
        public string? DiffPath { get; init; }
        public string? LogDates { get; init; }
        public string? LogPaths { get; init; }
    }
}
