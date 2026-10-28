using Dapper;
using Microsoft.Data.SqlClient;
using SqlPortal.Api.Models;
using System.Globalization;

namespace SqlPortal.Api.Data;

public sealed class SqlPortalRepository : IPortalRepository
{
    private static readonly IReadOnlyDictionary<string, string> ServerSortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Name",
            ["version"] = "Version",
            ["edition"] = "Edition",
            ["databaseCount"] = "DatabaseCount",
            ["memoryGb"] = "MemoryGb",
            ["collectedAtUtc"] = "CollectedAtUtc"
        };

    private static readonly IReadOnlyDictionary<string, string> DatabaseSortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "Name",
            ["serverName"] = "ServerName",
            ["status"] = "Status",
            ["sizeGb"] = "SizeGb",
            ["logUsedPercent"] = "LogUsedPercent",
            ["collectedAtUtc"] = "CollectedAtUtc"
        };

    private static readonly IReadOnlyDictionary<string, string> ConfigurationSortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["serverName"] = "ServerName",
            ["parameterName"] = "ParameterName",
            ["category"] = "Category",
            ["status"] = "CASE Status WHEN N'Abweichung' THEN 0 WHEN N'Nicht ermittelt' THEN 1 ELSE 2 END",
            ["collectedAtUtc"] = "CollectedAtUtc"
        };

    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;

    public SqlPortalRepository(IConfiguration configuration, TimeProvider timeProvider)
    {
        _connectionString = configuration.GetConnectionString("PortalDatabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PortalDatabase is required in SqlServer mode.");
        _timeProvider = timeProvider;
    }

    public async Task<PortalOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                TotalServers = COUNT(1),
                RecentServers = COALESCE(SUM(CASE
                    WHEN CollectedAtUtc >= DATEADD(DAY, -1, SYSUTCDATETIME()) THEN 1 ELSE 0 END), 0),
                WeekOldServers = COALESCE(SUM(CASE
                    WHEN CollectedAtUtc < DATEADD(DAY, -1, SYSUTCDATETIME())
                     AND CollectedAtUtc >= DATEADD(DAY, -7, SYSUTCDATETIME()) THEN 1 ELSE 0 END), 0),
                OlderServers = COALESCE(SUM(CASE
                    WHEN CollectedAtUtc < DATEADD(DAY, -7, SYSUTCDATETIME()) THEN 1 ELSE 0 END), 0),
                TotalMemoryGb = COALESCE(SUM(MemoryGb), 0)
            FROM portal.vServerOverview;

            SELECT
                TotalDatabases = COUNT(1),
                HealthyDatabases = COALESCE(SUM(CASE WHEN Status = N'Gesund' THEN 1 ELSE 0 END), 0),
                AttentionDatabases = COALESCE(SUM(CASE WHEN Status <> N'Gesund' THEN 1 ELSE 0 END), 0),
                TotalSizeGb = COALESCE(SUM(SizeGb), 0)
            FROM portal.vDatabaseOverview;

            SELECT
                serverData.Environment,
                ServerCount = COUNT(DISTINCT serverData.Id),
                DatabaseCount = COUNT(databaseData.Id)
            FROM portal.vServerOverview AS serverData
            LEFT JOIN portal.vDatabaseOverview AS databaseData ON databaseData.ServerId = serverData.Id
            GROUP BY serverData.Environment
            ORDER BY ServerCount DESC, serverData.Environment;

            SELECT TOP (6)
                Id, Name, InstanceName, Environment, Version, Edition,
                DatabaseCount, MemoryGb, CollectedAtUtc,
                Description = CAST(NULL AS nvarchar(255))
            FROM portal.vServerOverview
            ORDER BY CollectedAtUtc, Name;

            SELECT TOP (6)
                Id, ServerName, Name, Environment, Status, SizeGb, LogUsedPercent,
                LastFullBackupAtUtc, CollectedAtUtc
            FROM portal.vDatabaseOverview
            WHERE Status <> N'Gesund'
            ORDER BY
                CASE Status WHEN N'Kritisch' THEN 0 ELSE 1 END,
                LogUsedPercent DESC,
                Name;

            SELECT OldestCollectedAtUtc = MIN(CollectedAtUtc)
            FROM portal.vServerOverview;
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
        var orderBy = GetSortColumn(ServerSortColumns, query.SortBy, "Name");
        var direction = query.SortDescending ? "DESC" : "ASC";
        var sql = $$"""
            SELECT
                Id, Name, InstanceName, Environment, Version, Edition,
                DatabaseCount, MemoryGb, CollectedAtUtc,
                Description = CAST(NULL AS nvarchar(255))
            FROM portal.vServerOverview
            WHERE (@SearchPattern IS NULL OR Name LIKE @SearchPattern OR InstanceName LIKE @SearchPattern
                OR Version LIKE @SearchPattern OR Edition LIKE @SearchPattern)
              AND (@Environment IS NULL OR Environment = @Environment)
              AND (@Version IS NULL OR Version = @Version)
              AND (@Edition IS NULL OR Edition = @Edition)
            ORDER BY {{orderBy}} {{direction}}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(1)
            FROM portal.vServerOverview
            WHERE (@SearchPattern IS NULL OR Name LIKE @SearchPattern OR InstanceName LIKE @SearchPattern
                OR Version LIKE @SearchPattern OR Edition LIKE @SearchPattern)
              AND (@Environment IS NULL OR Environment = @Environment)
              AND (@Version IS NULL OR Version = @Version)
              AND (@Edition IS NULL OR Edition = @Edition);
            """;

        var parameters = CreateParameters(query);
        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var items = (await grid.ReadAsync<PortalServer>()).AsList();
        var total = await grid.ReadSingleAsync<int>();
        return new PagedResult<PortalServer>(items, query.Page, query.PageSize, total);
    }

    public async Task<ServerFilterOptions> GetServerFilterOptionsAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT Environment
            FROM portal.vServerOverview
            WHERE Environment IS NOT NULL
            ORDER BY Environment;

            SELECT DISTINCT Version
            FROM portal.vServerOverview
            WHERE Version IS NOT NULL
            ORDER BY Version;

            SELECT DISTINCT Edition
            FROM portal.vServerOverview
            WHERE Edition IS NOT NULL
            ORDER BY Edition;
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        return new ServerFilterOptions(
            (await grid.ReadAsync<string>()).AsList(),
            (await grid.ReadAsync<string>()).AsList(),
            (await grid.ReadAsync<string>()).AsList());
    }

    public async Task<ServerDetail?> GetServerDetailAsync(
        int serverId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                Id, Name, InstanceName, Environment, Version, Edition,
                DatabaseCount, MemoryGb, CollectedAtUtc,
                Description = CAST(NULL AS nvarchar(255))
            FROM portal.vServerOverview
            WHERE Id = @ServerId;

            SELECT
                Id, ServerId, ServerName, Name, Environment, Status, State, RecoveryModel,
                CompatibilityLevel, SizeGb, DataUsedPercent, LogUsedPercent,
                LastFullBackupAtUtc, LastLogBackupAtUtc, Owner, CollectedAtUtc
            FROM portal.vDatabaseOverview
            WHERE ServerId = @ServerId
            ORDER BY Name;

            SELECT
                Id, ServerId, ServerName, Environment, Category, ParameterName,
                ExpectedValue, ActualValue, Unit, Status, Deviation, CollectedAtUtc,
                InstanceName = CAST(NULL AS nvarchar(255)),
                DatabaseName = CAST(NULL AS nvarchar(255)),
                ObjectName = CAST(NULL AS nvarchar(255)),
                ConfiguredValue = ActualValue,
                RunningValue = ActualValue,
                IsCompliant = CAST(CASE WHEN Status = N'Konform' THEN 1 ELSE 0 END AS bit),
                ComplianceState = Status,
                Description = Deviation
            FROM portal.vConfigurationCheck
            WHERE ServerId = @ServerId
            ORDER BY Category, ParameterName;
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, new { ServerId = serverId }, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var server = await grid.ReadSingleOrDefaultAsync<PortalServer>();
        var databases = (await grid.ReadAsync<PortalDatabase>()).AsList();
        var checks = (await grid.ReadAsync<ConfigurationCheck>()).AsList();
        if (server is null)
        {
            return null;
        }

        ServerParameter[] parameters =
        [
            new("Name", server.Name),
            new("InstanceName", server.InstanceName),
            new("Environment", server.Environment),
            new("Version", server.Version),
            new("Edition", server.Edition),
            new("DatabaseCount", server.DatabaseCount.ToString(CultureInfo.InvariantCulture)),
            new("CollectedAtUtc", server.CollectedAtUtc?.ToString("O"))
        ];
        return new ServerDetail(server, parameters, databases, checks);
    }

    public async Task<PagedResult<PortalDatabase>> GetDatabasesAsync(
        PortalQuery query,
        CancellationToken cancellationToken)
    {
        var orderBy = GetSortColumn(DatabaseSortColumns, query.SortBy, "Name");
        var direction = query.SortDescending ? "DESC" : "ASC";
        var sql = $$"""
            SELECT
                Id, ServerId, ServerName, Name, Environment, Status, State, RecoveryModel,
                CompatibilityLevel, SizeGb, DataUsedPercent, LogUsedPercent,
                LastFullBackupAtUtc, LastLogBackupAtUtc, Owner, CollectedAtUtc
            FROM portal.vDatabaseOverview
            WHERE (@SearchPattern IS NULL OR Name LIKE @SearchPattern OR ServerName LIKE @SearchPattern
                OR Owner LIKE @SearchPattern)
              AND (@Status IS NULL OR Status = @Status)
              AND (@Environment IS NULL OR Environment = @Environment)
            ORDER BY {{orderBy}} {{direction}}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(1)
            FROM portal.vDatabaseOverview
            WHERE (@SearchPattern IS NULL OR Name LIKE @SearchPattern OR ServerName LIKE @SearchPattern
                OR Owner LIKE @SearchPattern)
              AND (@Status IS NULL OR Status = @Status)
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

    public async Task<DatabaseDetail?> GetDatabaseDetailAsync(
        int databaseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                Id, ServerId, ServerName, Name, Environment, Status, State, RecoveryModel,
                CompatibilityLevel, SizeGb, DataUsedPercent, LogUsedPercent,
                LastFullBackupAtUtc, LastLogBackupAtUtc, Owner, CollectedAtUtc
            FROM portal.vDatabaseOverview
            WHERE Id = @DatabaseId;

            SELECT
                Id, ServerId, ServerName, Environment, Category, ParameterName,
                ExpectedValue, ActualValue, Unit, Status, Deviation, CollectedAtUtc,
                InstanceName = CAST(NULL AS nvarchar(255)),
                DatabaseName = CAST(NULL AS nvarchar(255)),
                ObjectName = CAST(NULL AS nvarchar(255)),
                ConfiguredValue = ActualValue,
                RunningValue = ActualValue,
                IsCompliant = CAST(CASE WHEN Status = N'Konform' THEN 1 ELSE 0 END AS bit),
                ComplianceState = Status,
                Description = Deviation
            FROM portal.vConfigurationCheck
            WHERE ServerId = (SELECT ServerId FROM portal.vDatabaseOverview WHERE Id = @DatabaseId)
            ORDER BY Category, ParameterName;
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, new { DatabaseId = databaseId }, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var database = await grid.ReadSingleOrDefaultAsync<PortalDatabase>();
        var checks = (await grid.ReadAsync<ConfigurationCheck>()).AsList();
        if (database is null)
        {
            return null;
        }

        ServerParameter[] parameters =
        [
            new("Id", database.Id.ToString(CultureInfo.InvariantCulture)),
            new("ServerId", database.ServerId.ToString(CultureInfo.InvariantCulture)),
            new("ServerName", database.ServerName),
            new("Name", database.Name),
            new("Environment", database.Environment),
            new("Status", database.Status),
            new("State", database.State),
            new("RecoveryModel", database.RecoveryModel),
            new("CompatibilityLevel", database.CompatibilityLevel?.ToString(CultureInfo.InvariantCulture)),
            new("SizeGb", database.SizeGb?.ToString(CultureInfo.InvariantCulture)),
            new("DataUsedPercent", database.DataUsedPercent?.ToString(CultureInfo.InvariantCulture)),
            new("LogUsedPercent", database.LogUsedPercent?.ToString(CultureInfo.InvariantCulture)),
            new("LastFullBackupAtUtc", database.LastFullBackupAtUtc?.ToString("O")),
            new("LastLogBackupAtUtc", database.LastLogBackupAtUtc?.ToString("O")),
            new("Owner", database.Owner),
            new("CollectedAtUtc", database.CollectedAtUtc?.ToString("O"))
        ];
        return new DatabaseDetail(database, parameters, checks);
    }

    public async Task<ConfigurationOverview> GetConfigurationOverviewAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                TotalChecks = COUNT(1),
                DefinedChecks = COUNT(DISTINCT ParameterName),
                CompliantChecks = COALESCE(SUM(CASE WHEN Status = N'Konform' THEN 1 ELSE 0 END), 0),
                DriftedChecks = COALESCE(SUM(CASE WHEN Status = N'Abweichung' THEN 1 ELSE 0 END), 0),
                UnknownChecks = COALESCE(SUM(CASE WHEN Status = N'Nicht ermittelt' THEN 1 ELSE 0 END), 0)
            FROM portal.vConfigurationCheck;

            SELECT DISTINCT Category
            FROM portal.vConfigurationCheck
            ORDER BY Category;

            SELECT
                ServerId,
                ServerName,
                Environment,
                TotalChecks = COUNT(1),
                CompliantChecks = COALESCE(SUM(CASE WHEN Status = N'Konform' THEN 1 ELSE 0 END), 0),
                DriftedChecks = COALESCE(SUM(CASE WHEN Status = N'Abweichung' THEN 1 ELSE 0 END), 0),
                UnknownChecks = COALESCE(SUM(CASE WHEN Status = N'Nicht ermittelt' THEN 1 ELSE 0 END), 0),
                CompliancePercent = CAST(
                    CASE WHEN COUNT(1) = 0 THEN 0
                         ELSE SUM(CASE WHEN Status = N'Konform' THEN 1.0 ELSE 0 END) * 100.0 / COUNT(1)
                    END AS decimal(5,1)),
                CollectedAtUtc = MAX(CollectedAtUtc)
            FROM portal.vConfigurationCheck
            GROUP BY ServerId, ServerName, Environment
            ORDER BY DriftedChecks DESC, UnknownChecks DESC, ServerName;
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var row = await grid.ReadSingleAsync<ConfigurationMetricsRow>();
        var categories = (await grid.ReadAsync<string>()).AsList();
        var servers = (await grid.ReadAsync<ServerConfigurationSummary>()).AsList();

        return new ConfigurationOverview(
            "SqlServer",
            _timeProvider.GetUtcNow(),
            new ConfigurationMetrics(
                row.TotalChecks,
                row.DefinedChecks,
                row.CompliantChecks,
                row.DriftedChecks,
                row.UnknownChecks,
                Percentage(row.CompliantChecks, row.TotalChecks)),
            categories,
            servers);
    }

    public async Task<PagedResult<ConfigurationCheck>> GetConfigurationChecksAsync(
        ConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        var orderBy = GetSortColumn(ConfigurationSortColumns, query.SortBy,
            "CASE Status WHEN N'Abweichung' THEN 0 WHEN N'Nicht ermittelt' THEN 1 ELSE 2 END");
        var direction = query.SortDescending ? "DESC" : "ASC";
        var sql = $$"""
            SELECT
                Id, ServerId, ServerName, Environment, Category, ParameterName,
                ExpectedValue, ActualValue, Unit, Status, Deviation, CollectedAtUtc,
                InstanceName = CAST(NULL AS nvarchar(255)),
                DatabaseName = CAST(NULL AS nvarchar(255)),
                ObjectName = CAST(NULL AS nvarchar(255)),
                ConfiguredValue = ActualValue,
                RunningValue = ActualValue,
                IsCompliant = CAST(CASE WHEN Status = N'Konform' THEN 1 ELSE 0 END AS bit),
                ComplianceState = Status,
                Description = Deviation
            FROM portal.vConfigurationCheck
            WHERE (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR ParameterName LIKE @SearchPattern OR Category LIKE @SearchPattern
                OR ExpectedValue LIKE @SearchPattern OR ActualValue LIKE @SearchPattern)
              AND (@Status IS NULL OR Status = @Status)
              AND (@Environment IS NULL OR Environment = @Environment)
              AND (@Category IS NULL OR Category = @Category)
            ORDER BY {{orderBy}} {{direction}}, ServerName, ParameterName
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(1)
            FROM portal.vConfigurationCheck
            WHERE (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR ParameterName LIKE @SearchPattern OR Category LIKE @SearchPattern
                OR ExpectedValue LIKE @SearchPattern OR ActualValue LIKE @SearchPattern)
              AND (@Status IS NULL OR Status = @Status)
              AND (@Environment IS NULL OR Environment = @Environment)
              AND (@Category IS NULL OR Category = @Category);
            """;

        var parameters = new
        {
            SearchPattern = query.Search is null ? null : $"%{query.Search}%",
            query.Status,
            query.Environment,
            query.Category,
            Offset = (query.Page - 1) * query.PageSize,
            query.PageSize
        };
        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var items = (await grid.ReadAsync<ConfigurationCheck>()).AsList();
        var total = await grid.ReadSingleAsync<int>();
        return new PagedResult<ConfigurationCheck>(items, query.Page, query.PageSize, total);
    }

    private SqlConnection CreateConnection() => new(_connectionString);

    private static object CreateParameters(PortalQuery query) => new
    {
        SearchPattern = query.Search is null ? null : $"%{query.Search}%",
        query.Status,
        query.Environment,
        query.Version,
        query.Edition,
        Offset = (query.Page - 1) * query.PageSize,
        query.PageSize
    };

    private static string GetSortColumn(
        IReadOnlyDictionary<string, string> allowedColumns,
        string requestedColumn,
        string defaultColumn) =>
        allowedColumns.TryGetValue(requestedColumn, out var column) ? column : defaultColumn;

    private static decimal Percentage(int value, int total) =>
        total == 0 ? 0 : Math.Round(value * 100m / total, 1);

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

    private sealed class ConfigurationMetricsRow
    {
        public int TotalChecks { get; init; }
        public int DefinedChecks { get; init; }
        public int CompliantChecks { get; init; }
        public int DriftedChecks { get; init; }
        public int UnknownChecks { get; init; }
    }
}

