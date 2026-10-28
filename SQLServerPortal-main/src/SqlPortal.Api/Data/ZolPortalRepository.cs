using Dapper;
using Microsoft.Data.SqlClient;
using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

/// <summary>
/// Read-only projection of ZZImportTest.dbo.zol_ready4import onto the existing portal API.
/// </summary>
public sealed class ZolPortalRepository : IPortalRepository
{
    private const string CurrentInventoryCte = """
        ;WITH SourceRows AS
        (
            SELECT
                ServerKey = CONCAT(
                    COALESCE(NULLIF(LTRIM(RTRIM(hardwareid)), ''),
                        NULLIF(LTRIM(RTRIM(hostname)), ''), 'Unbekannt'),
                    '|',
                    COALESCE(NULLIF(LTRIM(RTRIM(sqlserverinstance)), ''), 'MSSQLSERVER')),
                ServerName = COALESCE(
                    NULLIF(LTRIM(RTRIM(hostname)), ''),
                    NULLIF(LTRIM(RTRIM(sqlserverinstance)), ''),
                    'Unbekannt'),
                InstanceName = COALESCE(
                    NULLIF(LTRIM(RTRIM(sqlserverinstance)), ''),
                    NULLIF(LTRIM(RTRIM(hostname)), ''),
                    'Unbekannt'),
                Description = NULLIF(LTRIM(RTRIM(description)), ''),
                SqlServerVersion = COALESCE(NULLIF(LTRIM(RTRIM(sqlserverversion)), ''), 'Nicht ermittelt'),
                SqlServerEdition = COALESCE(NULLIF(LTRIM(RTRIM(sqlserverskuname)), ''), 'Nicht ermittelt'),
                DatabaseName = COALESCE(NULLIF(LTRIM(RTRIM(sqldb_name)), ''), 'Unbekannt'),
                DatabaseSize = UPPER(NULLIF(LTRIM(RTRIM(sqldb_size)), '')),
                RecoveryModel = COALESCE(NULLIF(LTRIM(RTRIM(sqldb_recoverymodel)), ''), 'Nicht ermittelt'),
                UserAccess = COALESCE(NULLIF(LTRIM(RTRIM(sqldb_useraccess)), ''), 'Nicht ermittelt'),
                BusinessContact = COALESCE(NULLIF(LTRIM(RTRIM(sqldb_business_contact)), ''), 'Nicht ermittelt'),
                BackupPath = NULLIF(LTRIM(RTRIM(sqldb_Backupfull)), ''),
                SqlDatabaseId = sql_dbid,
                CollectedAtUtc = CASE
                    WHEN scan_date IS NULL THEN NULL
                    ELSE SWITCHOFFSET(scan_date AT TIME ZONE 'Central European Standard Time', '+00:00')
                END
            FROM [ZZImportTest].[dbo].[zol_ready4import]
        ),
        ParsedRows AS
        (
            SELECT
                source.*,
                ServerId = CAST(CONVERT(bigint, CHECKSUM(source.ServerKey)) & 2147483647 AS int),
                DatabaseId = CAST(CONVERT(bigint, CHECKSUM(CONCAT(
                    source.ServerKey, '|', COALESCE(CONVERT(varchar(20), source.SqlDatabaseId), ''),
                    '|', source.DatabaseName))) & 2147483647 AS int),
                Environment = CASE
                    WHEN UPPER(COALESCE(source.Description, '')) LIKE '%#PROD#%'
                      OR UPPER(COALESCE(source.Description, '')) LIKE '%PRODUKTION%' THEN N'Produktion'
                    WHEN UPPER(COALESCE(source.Description, '')) LIKE '%#UAT#%'
                      OR UPPER(COALESCE(source.Description, '')) LIKE '%#TEST#%'
                      OR UPPER(COALESCE(source.Description, '')) LIKE '%ABNAHME%' THEN N'Abnahme'
                    WHEN UPPER(COALESCE(source.Description, '')) LIKE '%#DEV#%'
                      OR UPPER(COALESCE(source.Description, '')) LIKE '%ENTWICKLUNG%' THEN N'Entwicklung'
                    ELSE N'Nicht zugeordnet'
                END,
                SizeNumber = TRY_CONVERT(decimal(18, 4), REPLACE(
                    REPLACE(REPLACE(REPLACE(REPLACE(source.DatabaseSize,
                        ' TB', ''), ' GB', ''), ' MB', ''), ' KB', ''), ',', '.')),
                BackupToken = CASE
                    WHEN source.BackupPath IS NULL THEN NULL
                    ELSE RIGHT(REPLACE(UPPER(source.BackupPath), '.BAK', ''), 15)
                END
            FROM SourceRows AS source
        ),
        InventoryRows AS
        (
            SELECT
                parsed.*,
                SizeGb = CAST(CASE
                    WHEN parsed.DatabaseSize LIKE '% TB' THEN parsed.SizeNumber * 1024
                    WHEN parsed.DatabaseSize LIKE '% GB' THEN parsed.SizeNumber
                    WHEN parsed.DatabaseSize LIKE '% KB' THEN parsed.SizeNumber / 1048576
                    ELSE parsed.SizeNumber / 1024
                END AS decimal(18, 2)),
                LastFullBackupAtUtc = CASE
                    WHEN parsed.BackupToken LIKE '[12][0-9][0-9][0-9][01][0-9][0-3][0-9]_[0-2][0-9][0-5][0-9][0-5][0-9]'
                    THEN SWITCHOFFSET(
                        TRY_CONVERT(datetime2(0), CONCAT(
                            SUBSTRING(parsed.BackupToken, 1, 4), '-',
                            SUBSTRING(parsed.BackupToken, 5, 2), '-',
                            SUBSTRING(parsed.BackupToken, 7, 2), ' ',
                            SUBSTRING(parsed.BackupToken, 10, 2), ':',
                            SUBSTRING(parsed.BackupToken, 12, 2), ':',
                            SUBSTRING(parsed.BackupToken, 14, 2)), 120)
                            AT TIME ZONE 'Central European Standard Time', '+00:00')
                    ELSE NULL
                END,
                SnapshotRank = ROW_NUMBER() OVER
                (
                    PARTITION BY parsed.ServerKey, parsed.DatabaseName
                    ORDER BY CASE WHEN parsed.CollectedAtUtc IS NULL THEN 1 ELSE 0 END,
                        parsed.CollectedAtUtc DESC
                )
            FROM ParsedRows AS parsed
        ),
        CurrentRows AS
        (
            SELECT *
            FROM InventoryRows
            WHERE SnapshotRank = 1
        )
        """;

    private const string ServerRowsCte = CurrentInventoryCte + """
        , ServerRows AS
        (
            SELECT
                currentRow.*,
                DatabaseCount = COUNT(1) OVER (PARTITION BY currentRow.ServerKey),
                ServerRank = ROW_NUMBER() OVER
                (
                    PARTITION BY currentRow.ServerKey
                    ORDER BY CASE WHEN currentRow.CollectedAtUtc IS NULL THEN 1 ELSE 0 END,
                        currentRow.CollectedAtUtc DESC,
                        currentRow.DatabaseName
                )
            FROM CurrentRows AS currentRow
        )
        """;

    private static readonly IReadOnlyDictionary<string, string> ServerSortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "ServerName",
            ["databaseCount"] = "DatabaseCount",
            ["memoryGb"] = "MemoryGb",
            ["collectedAtUtc"] = "CollectedAtUtc"
        };

    private static readonly IReadOnlyDictionary<string, string> DatabaseSortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "DatabaseName",
            ["serverName"] = "ServerName",
            ["status"] = "Status",
            ["sizeGb"] = "SizeGb",
            ["logUsedPercent"] = "LogUsedPercent",
            ["collectedAtUtc"] = "CollectedAtUtc"
        };

    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;

    public ZolPortalRepository(IConfiguration configuration, TimeProvider timeProvider)
    {
        _connectionString = configuration.GetConnectionString("PortalDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PortalDatabase is required in SqlServer mode.");
        _timeProvider = timeProvider;
    }

    public async Task<PortalOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var sql = ServerRowsCte + """
            SELECT
                TotalServers = COUNT(1),
                RecentServers = COALESCE(SUM(CASE
                    WHEN CollectedAtUtc >= DATEADD(DAY, -1, SYSUTCDATETIME()) THEN 1 ELSE 0 END), 0),
                WeekOldServers = COALESCE(SUM(CASE
                    WHEN CollectedAtUtc < DATEADD(DAY, -1, SYSUTCDATETIME())
                     AND CollectedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME()) THEN 1 ELSE 0 END), 0),
                OlderServers = COALESCE(SUM(CASE
                    WHEN CollectedAtUtc < DATEADD(DAY, -7, SYSUTCDATETIME()) THEN 1 ELSE 0 END), 0),
                TotalMemoryGb = CAST(0 AS decimal(18, 2))
            FROM ServerRows
            WHERE ServerRank = 1;
            """ + CurrentInventoryCte + """
            SELECT
                TotalDatabases = COUNT(1),
                HealthyDatabases = 0,
                AttentionDatabases = 0,
                TotalSizeGb = COALESCE(SUM(SizeGb), 0)
            FROM CurrentRows;
            """ + CurrentInventoryCte + """
            SELECT
                Environment,
                ServerCount = COUNT(DISTINCT ServerId),
                DatabaseCount = COUNT(1)
            FROM CurrentRows
            GROUP BY Environment
            ORDER BY ServerCount DESC, Environment;
            """ + ServerRowsCte + """
            SELECT TOP (6)
                Id = ServerId,
                Name = ServerName,
                InstanceName,
                Environment,
                Version = SqlServerVersion,
                Edition = SqlServerEdition,
                DatabaseCount,
                MemoryGb = CAST(NULL AS decimal(18, 2)),
                CollectedAtUtc
            FROM ServerRows
            WHERE ServerRank = 1
            ORDER BY CASE WHEN CollectedAtUtc IS NULL THEN 0 ELSE 1 END, CollectedAtUtc, ServerName;

            SELECT TOP (0)
                Id = CAST(0 AS int),
                ServerName = CAST(N'' AS nvarchar(128)),
                Name = CAST(N'' AS nvarchar(128)),
                Environment = CAST(N'' AS nvarchar(50)),
                Status = CAST(N'' AS nvarchar(30)),
                SizeGb = CAST(NULL AS decimal(18, 2)),
                LogUsedPercent = CAST(NULL AS decimal(5, 2)),
                LastFullBackupAtUtc = CAST(NULL AS datetimeoffset),
                CollectedAtUtc = CAST(NULL AS datetimeoffset);
            """ + CurrentInventoryCte + """
            SELECT OldestCollectedAtUtc = MIN(CollectedAtUtc)
            FROM CurrentRows;
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);

        var servers = await grid.ReadSingleAsync<ServerMetricsRow>();
        var databases = await grid.ReadSingleAsync<DatabaseMetricsRow>();
        var environments = (await grid.ReadAsync<EnvironmentSummary>()).AsList();
        var focusServers = (await grid.ReadAsync<PortalServer>()).AsList();
        var alerts = (await grid.ReadAsync<DatabaseAlert>()).AsList();
        var oldest = await grid.ReadSingleAsync<OldestCollectionRow>();

        return new PortalOverview(
            "SqlServer",
            _timeProvider.GetUtcNow(),
            oldest.OldestCollectedAtUtc,
            new OverviewMetrics(
                servers.TotalServers,
                servers.RecentServers,
                servers.WeekOldServers,
                servers.OlderServers,
                databases.TotalDatabases,
                databases.HealthyDatabases,
                databases.AttentionDatabases,
                databases.TotalSizeGb,
                servers.TotalMemoryGb),
            environments,
            focusServers,
            alerts);
    }

    public async Task<PagedResult<PortalServer>> GetServersAsync(
        PortalQuery query,
        CancellationToken cancellationToken)
    {
        var orderBy = GetSortColumn(ServerSortColumns, query.SortBy, "ServerName");
        var direction = query.SortDescending ? "DESC" : "ASC";
        var sql = ServerRowsCte + $$"""
            SELECT
                Id = ServerId,
                Name = ServerName,
                InstanceName,
                Environment,
                Version = SqlServerVersion,
                Edition = SqlServerEdition,
                DatabaseCount,
                MemoryGb = CAST(NULL AS decimal(18, 2)),
                CollectedAtUtc
            FROM ServerRows
            WHERE ServerRank = 1
              AND (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR InstanceName LIKE @SearchPattern OR SqlServerVersion LIKE @SearchPattern
                OR SqlServerEdition LIKE @SearchPattern OR Description LIKE @SearchPattern)
              AND (@Environment IS NULL OR Environment = @Environment)
            ORDER BY {{orderBy}} {{direction}}{{(orderBy == "ServerName" ? "" : ", ServerName")}}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """ + ServerRowsCte + """
            SELECT COUNT(1)
            FROM ServerRows
            WHERE ServerRank = 1
              AND (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR InstanceName LIKE @SearchPattern OR SqlServerVersion LIKE @SearchPattern
                OR SqlServerEdition LIKE @SearchPattern OR Description LIKE @SearchPattern)
              AND (@Environment IS NULL OR Environment = @Environment);
            """;

        var parameters = CreateParameters(query);
        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var items = (await grid.ReadAsync<PortalServer>()).AsList();
        var total = await grid.ReadSingleAsync<int>();
        return new PagedResult<PortalServer>(items, query.Page, query.PageSize, total);
    }

    public async Task<PagedResult<PortalDatabase>> GetDatabasesAsync(
        PortalQuery query,
        CancellationToken cancellationToken)
    {
        var orderBy = GetSortColumn(DatabaseSortColumns, query.SortBy, "DatabaseName");
        var direction = query.SortDescending ? "DESC" : "ASC";
        var sql = CurrentInventoryCte + $$"""
            SELECT
                Id = DatabaseId,
                ServerId,
                ServerName,
                Name = DatabaseName,
                Environment,
                Status = N'Unbewertet',
                State = UserAccess,
                RecoveryModel,
                CompatibilityLevel = CAST(NULL AS int),
                SizeGb,
                DataUsedPercent = CAST(NULL AS decimal(5, 2)),
                LogUsedPercent = CAST(NULL AS decimal(5, 2)),
                LastFullBackupAtUtc,
                LastLogBackupAtUtc = CAST(NULL AS datetimeoffset),
                Owner = BusinessContact,
                CollectedAtUtc
            FROM CurrentRows
            WHERE (@SearchPattern IS NULL OR DatabaseName LIKE @SearchPattern
                OR ServerName LIKE @SearchPattern OR InstanceName LIKE @SearchPattern
                OR BusinessContact LIKE @SearchPattern OR Description LIKE @SearchPattern)
              AND (@Status IS NULL OR @Status = N'Unbewertet')
              AND (@Environment IS NULL OR Environment = @Environment)
            ORDER BY {{orderBy}} {{direction}}{{(orderBy == "ServerName" ? "" : ", ServerName")}}{{(orderBy == "DatabaseName" ? "" : ", DatabaseName")}}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """ + ServerRowsCte + """
            SELECT COUNT(1)
            FROM ServerRows
            WHERE (@SearchPattern IS NULL OR DatabaseName LIKE @SearchPattern
                OR ServerName LIKE @SearchPattern OR InstanceName LIKE @SearchPattern
                OR BusinessContact LIKE @SearchPattern OR Description LIKE @SearchPattern)
              AND (@Status IS NULL OR @Status = N'Unbewertet')
              AND (@Environment IS NULL OR Environment = @Environment);
            """;

        var parameters = CreateParameters(query);
        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var items = (await grid.ReadAsync<PortalDatabase>()).AsList();
        var total = await grid.ReadSingleAsync<int>();
        return new PagedResult<PortalDatabase>(items, query.Page, query.PageSize, total);
    }

    public Task<ConfigurationOverview> GetConfigurationOverviewAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ConfigurationOverview(
            "SqlServer",
            _timeProvider.GetUtcNow(),
            new ConfigurationMetrics(0, 0, 0, 0, 0),
            [],
            []));
    }

    public Task<PagedResult<ConfigurationCheck>> GetConfigurationChecksAsync(
        ConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PagedResult<ConfigurationCheck>([], query.Page, query.PageSize, 0));
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    private static object CreateParameters(PortalQuery query) => new
    {
        SearchPattern = query.Search is null ? null : $"%{query.Search}%",
        query.Status,
        query.Environment,
        Offset = (query.Page - 1) * query.PageSize,
        query.PageSize
    };

    private static string GetSortColumn(
        IReadOnlyDictionary<string, string> allowedColumns,
        string requestedColumn,
        string defaultColumn) =>
        allowedColumns.TryGetValue(requestedColumn, out var column) ? column : defaultColumn;

    private sealed class ServerMetricsRow
    {
        public int TotalServers { get; init; }
        public int RecentServers { get; init; }
        public int WeekOldServers { get; init; }
        public int OlderServers { get; init; }
        public decimal TotalMemoryGb { get; init; }
    }

    private sealed class DatabaseMetricsRow
    {
        public int TotalDatabases { get; init; }
        public int HealthyDatabases { get; init; }
        public int AttentionDatabases { get; init; }
        public decimal TotalSizeGb { get; init; }
    }

    private sealed class OldestCollectionRow
    {
        public DateTimeOffset? OldestCollectedAtUtc { get; init; }
    }
}
