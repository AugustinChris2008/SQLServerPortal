using Dapper;
using Microsoft.Data.SqlClient;
using SqlPortal.Api.Models;
using System.Globalization;

namespace SqlPortal.Api.Data;

/// <summary>
/// Read-only projection of the ZOL inventory and Backupparameter compliance tables
/// onto the existing portal API.
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
                Classification = CASE
                    WHEN classificationMarker.FirstHash > 0
                     AND classificationEnd.SecondHash > classificationMarker.FirstHash
                    THEN NULLIF(LTRIM(RTRIM(SUBSTRING(
                        description,
                        classificationMarker.FirstHash + 1,
                        classificationEnd.SecondHash - classificationMarker.FirstHash - 1))), '')
                    ELSE NULL
                END,
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
            CROSS APPLY
            (
                SELECT FirstHash = CHARINDEX('#', COALESCE(description, ''))
            ) AS classificationMarker
            CROSS APPLY
            (
                SELECT SecondHash = CHARINDEX(
                    '#', COALESCE(description, ''), classificationMarker.FirstHash + 1)
            ) AS classificationEnd
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
                    WHEN UPPER(COALESCE(source.Classification, '')) = 'PROD' THEN N'PROD'
                    WHEN UPPER(COALESCE(source.Classification, '')) = 'CONS' THEN N'CONS'
                    WHEN UPPER(COALESCE(source.Classification, '')) = 'DEV' THEN N'DEV'
                    WHEN UPPER(COALESCE(source.Classification, '')) = 'TEST' THEN N'TEST'
                    ELSE N'Nicht klassifiziert'
                END,
                NormalizedVersion = CASE
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2025%'
                      OR source.SqlServerVersion LIKE '17.%' THEN N'SQL Server 2025'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2022%'
                      OR source.SqlServerVersion LIKE '16.%' THEN N'SQL Server 2022'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2019%'
                      OR source.SqlServerVersion LIKE '15.%' THEN N'SQL Server 2019'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2017%'
                      OR source.SqlServerVersion LIKE '14.%' THEN N'SQL Server 2017'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2016%'
                      OR source.SqlServerVersion LIKE '13.%' THEN N'SQL Server 2016'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2014%'
                      OR source.SqlServerVersion LIKE '12.%' THEN N'SQL Server 2014'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2012%'
                      OR source.SqlServerVersion LIKE '11.%' THEN N'SQL Server 2012'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2008 R2%'
                      OR source.SqlServerVersion LIKE '10.5%' THEN N'SQL Server 2008 R2'
                    WHEN UPPER(source.SqlServerVersion) LIKE '%SQL SERVER 2008%'
                      OR source.SqlServerVersion LIKE '10.%' THEN N'SQL Server 2008'
                    ELSE source.SqlServerVersion
                END,
                NormalizedEdition = CASE
                    WHEN UPPER(source.SqlServerEdition) LIKE '%ENTERPRISE%' THEN N'Enterprise'
                    WHEN UPPER(source.SqlServerEdition) LIKE '%STANDARD%' THEN N'Standard'
                    WHEN UPPER(source.SqlServerEdition) LIKE '%EXPRESS%' THEN N'Express'
                    WHEN UPPER(source.SqlServerEdition) LIKE '%DEVELOPER%' THEN N'Developer'
                    WHEN UPPER(source.SqlServerEdition) LIKE '%WEB%' THEN N'Web'
                    WHEN UPPER(source.SqlServerEdition) LIKE '%EVALUATION%' THEN N'Evaluation'
                    ELSE source.SqlServerEdition
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
                END AS decimal(18, 6)),
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

    private const string CurrentConfigurationCte = """
        ;WITH RankedComplianceResults AS
        (
            SELECT
                compliance.*,
                ResultRank = ROW_NUMBER() OVER
                (
                    PARTITION BY
                        compliance.SqlServer,
                        COALESCE(compliance.SqlInstance, N''),
                        COALESCE(compliance.DatabaseName, N''),
                        COALESCE(compliance.ObjectName, N''),
                        compliance.CheckName
                    ORDER BY compliance.EvaluatedAt DESC
                )
            FROM [Backupparameter].[dbo].[ComplianceResults] AS compliance
        ),
        CurrentComplianceResults AS
        (
            SELECT *
            FROM RankedComplianceResults
            WHERE ResultRank = 1
        ),
        ConfiguredResults AS
        (
            SELECT
                Id = CAST(CONVERT(bigint, CHECKSUM(CONCAT(
                    currentResult.SqlServer, N'|', COALESCE(currentResult.SqlInstance, N''), N'|',
                    COALESCE(currentResult.DatabaseName, N''), N'|', COALESCE(currentResult.ObjectName, N''), N'|',
                    currentResult.CheckName, N'|', CONVERT(nvarchar(30), currentResult.EvaluatedAt, 126))))
                    & 2147483647 AS int),
                ServerId = CAST(CONVERT(bigint, CHECKSUM(CONCAT(
                    currentResult.SqlServer, N'|', COALESCE(currentResult.SqlInstance, N''))))
                    & 2147483647 AS int),
                ServerName = CONCAT(
                    COALESCE(NULLIF(LTRIM(RTRIM(currentResult.SqlServer)), N''), N'Unbekannt'),
                    CASE
                        WHEN NULLIF(LTRIM(RTRIM(currentResult.SqlInstance)), N'') IS NULL
                          OR UPPER(LTRIM(RTRIM(currentResult.SqlInstance))) = N'MSSQLSERVER'
                          OR LTRIM(RTRIM(currentResult.SqlInstance)) = LTRIM(RTRIM(currentResult.SqlServer))
                            THEN N''
                        ELSE CONCAT(N'\', LTRIM(RTRIM(currentResult.SqlInstance)))
                    END),
                SqlServer = COALESCE(NULLIF(LTRIM(RTRIM(currentResult.SqlServer)), N''), N'Unbekannt'),
                InstanceName = NULLIF(LTRIM(RTRIM(currentResult.SqlInstance)), N''),
                Environment = COALESCE(NULLIF(LTRIM(RTRIM(currentResult.Environment)), N''), N'Nicht zugeordnet'),
                Category = COALESCE(NULLIF(LTRIM(RTRIM(currentResult.ServerType)), N''), N'Nicht zugeordnet'),
                ParameterName = COALESCE(NULLIF(LTRIM(RTRIM(currentResult.CheckName)), N''), N'Unbekannte Prüfung'),
                ExpectedValue = COALESCE(ruleValue.ExpectedValue,
                    NULLIF(LTRIM(RTRIM(currentResult.ExpectedValue)), N''), N'Nicht definiert'),
                ActualValue = NULLIF(LTRIM(RTRIM(currentResult.ActualValue)), N''),
                Unit = CAST(NULL AS nvarchar(50)),
                DatabaseName = NULLIF(LTRIM(RTRIM(currentResult.DatabaseName)), N''),
                ObjectName = NULLIF(LTRIM(RTRIM(currentResult.ObjectName)), N''),
                ConfiguredValue = NULLIF(LTRIM(RTRIM(currentResult.ConfiguredValue)), N''),
                RunningValue = NULLIF(LTRIM(RTRIM(currentResult.RunningValue)), N''),
                currentResult.IsCompliant,
                ComplianceState = NULLIF(LTRIM(RTRIM(currentResult.ComplianceState)), N''),
                Description = COALESCE(ruleValue.Description,
                    NULLIF(LTRIM(RTRIM(currentResult.Description)), N'')),
                CollectedAtUtc = SWITCHOFFSET(
                    currentResult.EvaluatedAt AT TIME ZONE 'Central European Standard Time', '+00:00')
            FROM CurrentComplianceResults AS currentResult
            OUTER APPLY
            (
                SELECT TOP (1)
                    ExpectedValue = NULLIF(LTRIM(RTRIM(complianceRule.ExpectedValue)), N''),
                    Description = NULLIF(LTRIM(RTRIM(complianceRule.Description)), N'')
                FROM [Backupparameter].[dbo].[ComplianceRules] AS complianceRule
                WHERE LTRIM(RTRIM(complianceRule.CheckName)) = LTRIM(RTRIM(currentResult.CheckName))
                  AND (NULLIF(LTRIM(RTRIM(complianceRule.Environment)), '') IS NULL
                    OR LTRIM(RTRIM(complianceRule.Environment)) = LTRIM(RTRIM(currentResult.Environment)))
                  AND (NULLIF(LTRIM(RTRIM(complianceRule.ServerType)), '') IS NULL
                    OR LTRIM(RTRIM(complianceRule.ServerType)) = LTRIM(RTRIM(currentResult.ServerType)))
                ORDER BY
                    CASE WHEN LTRIM(RTRIM(complianceRule.Environment)) = LTRIM(RTRIM(currentResult.Environment))
                        THEN 1 ELSE 0 END DESC,
                    CASE WHEN LTRIM(RTRIM(complianceRule.ServerType)) = LTRIM(RTRIM(currentResult.ServerType))
                        THEN 1 ELSE 0 END DESC,
                    complianceRule.CheckName
            ) AS ruleValue
        ),
        EvaluatedConfiguration AS
        (
            SELECT
                configuredResult.*,
                Status = CASE
                    WHEN UPPER(COALESCE(configuredResult.ComplianceState, N'')) IN
                        (N'UNKNOWN', N'NOT_EVALUATED', N'NOT EVALUATED', N'NOT_APPLICABLE', N'NOT APPLICABLE')
                        THEN N'Nicht ermittelt'
                    WHEN configuredResult.IsCompliant = 1
                      OR UPPER(COALESCE(configuredResult.ComplianceState, N'')) IN
                        (N'COMPLIANT', N'CONFORM', N'OK')
                        THEN N'Konform'
                    WHEN UPPER(COALESCE(configuredResult.ComplianceState, N'')) IN
                        (N'NON_COMPLIANT', N'NON-COMPLIANT', N'NOT_COMPLIANT')
                        THEN N'Abweichung'
                    WHEN configuredResult.ActualValue IS NULL
                     AND configuredResult.ConfiguredValue IS NULL
                     AND configuredResult.RunningValue IS NULL
                        THEN N'Nicht ermittelt'
                    ELSE N'Abweichung'
                END
            FROM ConfiguredResults AS configuredResult
        ),
        CurrentConfiguration AS
        (
            SELECT
                evaluatedResult.*,
                Deviation = CASE evaluatedResult.Status
                    WHEN N'Abweichung' THEN N'SOLL- und IST-Wert weichen voneinander ab'
                    WHEN N'Nicht ermittelt' THEN N'Kein auswertbarer IST-Wert vorhanden'
                    ELSE NULL
                END
            FROM EvaluatedConfiguration AS evaluatedResult
        )
        """;

    private static readonly IReadOnlyDictionary<string, string> ServerSortColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "ServerName",
            ["version"] = "NormalizedVersion",
            ["edition"] = "NormalizedEdition",
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
                Version = NormalizedVersion,
                Edition = NormalizedEdition,
                DatabaseCount,
                MemoryGb = CAST(NULL AS decimal(18, 2)),
                CollectedAtUtc,
                Description
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
                Version = NormalizedVersion,
                Edition = NormalizedEdition,
                DatabaseCount,
                MemoryGb = CAST(NULL AS decimal(18, 2)),
                CollectedAtUtc,
                Description
            FROM ServerRows
            WHERE ServerRank = 1
              AND (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR InstanceName LIKE @SearchPattern OR SqlServerVersion LIKE @SearchPattern
                OR SqlServerEdition LIKE @SearchPattern OR Description LIKE @SearchPattern)
              AND (@Environment IS NULL OR Environment = @Environment)
              AND (@Version IS NULL OR NormalizedVersion = @Version)
              AND (@Edition IS NULL OR NormalizedEdition = @Edition)
            ORDER BY {{orderBy}} {{direction}}{{(orderBy == "ServerName" ? "" : ", ServerName")}}
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """ + ServerRowsCte + """
            SELECT COUNT(1)
            FROM ServerRows
            WHERE ServerRank = 1
              AND (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR InstanceName LIKE @SearchPattern OR SqlServerVersion LIKE @SearchPattern
                OR SqlServerEdition LIKE @SearchPattern OR Description LIKE @SearchPattern)
              AND (@Environment IS NULL OR Environment = @Environment)
              AND (@Version IS NULL OR NormalizedVersion = @Version)
              AND (@Edition IS NULL OR NormalizedEdition = @Edition);
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
        var sql = ServerRowsCte + """
            SELECT DISTINCT NormalizedVersion
            FROM ServerRows
            WHERE ServerRank = 1
            ORDER BY NormalizedVersion;
            """ + ServerRowsCte + """
            SELECT DISTINCT NormalizedEdition
            FROM ServerRows
            WHERE ServerRank = 1
            ORDER BY NormalizedEdition;
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        return new ServerFilterOptions(
            ["PROD", "CONS", "DEV", "TEST"],
            (await grid.ReadAsync<string>()).AsList(),
            (await grid.ReadAsync<string>()).AsList());
    }

    public async Task<ServerDetail?> GetServerDetailAsync(
        int serverId,
        CancellationToken cancellationToken)
    {
        var inventorySql = ServerRowsCte + """
            SELECT
                Id = ServerId,
                Name = ServerName,
                InstanceName,
                Environment,
                Version = NormalizedVersion,
                Edition = NormalizedEdition,
                DatabaseCount,
                MemoryGb = CAST(NULL AS decimal(18, 2)),
                CollectedAtUtc,
                Description
            FROM ServerRows
            WHERE ServerRank = 1 AND ServerId = @ServerId;
            """ + """
            SELECT TOP (1) source.*
            FROM [ZZImportTest].[dbo].[zol_ready4import] AS source
            CROSS APPLY
            (
                SELECT ServerKey = CONCAT(
                    COALESCE(NULLIF(LTRIM(RTRIM(source.hardwareid)), ''),
                        NULLIF(LTRIM(RTRIM(source.hostname)), ''), 'Unbekannt'),
                    '|',
                    COALESCE(NULLIF(LTRIM(RTRIM(source.sqlserverinstance)), ''), 'MSSQLSERVER'))
            ) AS serverKey
            WHERE CAST(CONVERT(bigint, CHECKSUM(serverKey.ServerKey)) & 2147483647 AS int) = @ServerId
            ORDER BY CASE WHEN source.scan_date IS NULL THEN 1 ELSE 0 END, source.scan_date DESC;
            """ + CurrentInventoryCte + """
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
            WHERE ServerId = @ServerId
            ORDER BY DatabaseName;
            """;

        PortalServer? server;
        IReadOnlyList<ServerParameter> parameters;
        IReadOnlyList<PortalDatabase> databases;
        await using (var connection = CreateConnection())
        {
            var command = new CommandDefinition(
                inventorySql,
                new { ServerId = serverId },
                cancellationToken: cancellationToken);
            using var grid = await connection.QueryMultipleAsync(command);
            server = await grid.ReadSingleOrDefaultAsync<PortalServer>();
            var sourceRow = (await grid.ReadAsync()).FirstOrDefault();
            parameters = sourceRow is IDictionary<string, object> sourceValues
                ? sourceValues.Select(parameter => new ServerParameter(
                    parameter.Key,
                    FormatParameterValue(parameter.Value))).ToArray()
                : [];
            databases = (await grid.ReadAsync<PortalDatabase>()).AsList();
        }

        if (server is null)
        {
            return null;
        }

        var configurationSql = CurrentConfigurationCte + """
            SELECT
                Id, ServerId, ServerName, Environment, Category, ParameterName,
                ExpectedValue, ActualValue, Unit, Status, Deviation, CollectedAtUtc,
                InstanceName, DatabaseName, ObjectName, ConfiguredValue, RunningValue,
                IsCompliant, ComplianceState, Description
            FROM CurrentConfiguration
            WHERE SqlServer = @ServerName
              AND (@InstanceName IS NULL OR InstanceName = @InstanceName
                OR @InstanceName LIKE CONCAT(N'%\', InstanceName)
                OR InstanceName LIKE CONCAT(N'%\', @InstanceName))
            ORDER BY
                CASE Status WHEN N'Abweichung' THEN 0 WHEN N'Nicht ermittelt' THEN 1 ELSE 2 END,
                Category,
                ParameterName;
            """;
        await using var configurationConnection = CreateConnection();
        var configurationCommand = new CommandDefinition(
            configurationSql,
            new { ServerName = server.Name, server.InstanceName },
            cancellationToken: cancellationToken);
        var checks = (await configurationConnection.QueryAsync<ConfigurationCheck>(configurationCommand)).AsList();

        return new ServerDetail(server, parameters, databases, checks);
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
            """ + CurrentInventoryCte + """
            SELECT COUNT(1)
            FROM CurrentRows
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

    public async Task<DatabaseDetail?> GetDatabaseDetailAsync(
        int databaseId,
        CancellationToken cancellationToken)
    {
        var detailSql = CurrentInventoryCte + """
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
            WHERE DatabaseId = @DatabaseId;

            SELECT TOP (1) source.*
            FROM [ZZImportTest].[dbo].[zol_ready4import] AS source
            CROSS APPLY
            (
                SELECT
                    ServerKey = CONCAT(
                        COALESCE(NULLIF(LTRIM(RTRIM(source.hardwareid)), ''),
                            NULLIF(LTRIM(RTRIM(source.hostname)), ''), 'Unbekannt'),
                        '|',
                        COALESCE(NULLIF(LTRIM(RTRIM(source.sqlserverinstance)), ''), 'MSSQLSERVER')),
                    DatabaseName = COALESCE(NULLIF(LTRIM(RTRIM(source.sqldb_name)), ''), 'Unbekannt')
            ) AS sourceKey
            WHERE CAST(CONVERT(bigint, CHECKSUM(CONCAT(
                    sourceKey.ServerKey, '|', COALESCE(CONVERT(varchar(20), source.sql_dbid), ''),
                    '|', sourceKey.DatabaseName))) & 2147483647 AS int) = @DatabaseId
            ORDER BY CASE WHEN source.scan_date IS NULL THEN 1 ELSE 0 END, source.scan_date DESC;
            """;

        PortalDatabase? database;
        IReadOnlyList<ServerParameter> parameters;
        string? instanceName = null;
        await using (var connection = CreateConnection())
        {
            var command = new CommandDefinition(
                detailSql,
                new { DatabaseId = databaseId },
                cancellationToken: cancellationToken);
            using var grid = await connection.QueryMultipleAsync(command);
            database = await grid.ReadSingleOrDefaultAsync<PortalDatabase>();
            var sourceRow = (await grid.ReadAsync()).FirstOrDefault();
            if (sourceRow is IDictionary<string, object> sourceValues)
            {
                parameters = sourceValues.Select(parameter => new ServerParameter(
                    parameter.Key,
                    FormatParameterValue(parameter.Value))).ToArray();
                if (sourceValues.TryGetValue("sqlserverinstance", out var instanceValue))
                {
                    instanceName = FormatParameterValue(instanceValue);
                }
            }
            else
            {
                parameters = [];
            }
        }

        if (database is null)
        {
            return null;
        }

        var configurationSql = CurrentConfigurationCte + """
            SELECT
                Id, ServerId, ServerName, Environment, Category, ParameterName,
                ExpectedValue, ActualValue, Unit, Status, Deviation, CollectedAtUtc,
                InstanceName, DatabaseName, ObjectName, ConfiguredValue, RunningValue,
                IsCompliant, ComplianceState, Description
            FROM CurrentConfiguration
            WHERE SqlServer = @ServerName
              AND DatabaseName = @DatabaseName
              AND (@InstanceName IS NULL OR InstanceName = @InstanceName
                OR @InstanceName LIKE CONCAT(N'%\', InstanceName)
                OR InstanceName LIKE CONCAT(N'%\', @InstanceName))
            ORDER BY
                CASE Status WHEN N'Abweichung' THEN 0 WHEN N'Nicht ermittelt' THEN 1 ELSE 2 END,
                Category,
                ParameterName;
            """;
        await using var configurationConnection = CreateConnection();
        var configurationCommand = new CommandDefinition(
            configurationSql,
            new { ServerName = database.ServerName, DatabaseName = database.Name, InstanceName = instanceName },
            cancellationToken: cancellationToken);
        var checks = (await configurationConnection.QueryAsync<ConfigurationCheck>(configurationCommand)).AsList();
        return new DatabaseDetail(database, parameters, checks);
    }

    public async Task<ConfigurationOverview> GetConfigurationOverviewAsync(
        CancellationToken cancellationToken)
    {
        var sql = CurrentConfigurationCte + """
            SELECT
                TotalChecks = COUNT(1),
                DefinedChecks = (SELECT COUNT(1) FROM [Backupparameter].[dbo].[ComplianceRules]),
                CompliantChecks = COALESCE(SUM(CASE WHEN Status = N'Konform' THEN 1 ELSE 0 END), 0),
                DriftedChecks = COALESCE(SUM(CASE WHEN Status = N'Abweichung' THEN 1 ELSE 0 END), 0),
                UnknownChecks = COALESCE(SUM(CASE WHEN Status = N'Nicht ermittelt' THEN 1 ELSE 0 END), 0)
            FROM CurrentConfiguration;
            """ + CurrentConfigurationCte + """
            SELECT DISTINCT Category
            FROM CurrentConfiguration
            ORDER BY Category;
            """ + CurrentConfigurationCte + """
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
                    END AS decimal(5, 1)),
                CollectedAtUtc = MAX(CollectedAtUtc)
            FROM CurrentConfiguration
            GROUP BY ServerId, ServerName, Environment
            ORDER BY DriftedChecks DESC, UnknownChecks DESC, ServerName;
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        using var grid = await connection.QueryMultipleAsync(command);
        var metrics = await grid.ReadSingleAsync<ConfigurationMetricsRow>();
        var categories = (await grid.ReadAsync<string>()).AsList();
        var servers = (await grid.ReadAsync<ServerConfigurationSummary>()).AsList();

        return new ConfigurationOverview(
            "SqlServer",
            _timeProvider.GetUtcNow(),
            new ConfigurationMetrics(
                metrics.TotalChecks,
                metrics.DefinedChecks,
                metrics.CompliantChecks,
                metrics.DriftedChecks,
                metrics.UnknownChecks,
                Percentage(metrics.CompliantChecks, metrics.TotalChecks)),
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
        var sql = CurrentConfigurationCte + $$"""
            SELECT
                Id, ServerId, ServerName, Environment, Category, ParameterName,
                ExpectedValue, ActualValue, Unit, Status, Deviation, CollectedAtUtc,
                InstanceName, DatabaseName, ObjectName, ConfiguredValue, RunningValue,
                IsCompliant, ComplianceState, Description
            FROM CurrentConfiguration
            WHERE (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR InstanceName LIKE @SearchPattern OR ParameterName LIKE @SearchPattern
                OR Category LIKE @SearchPattern OR DatabaseName LIKE @SearchPattern
                OR ObjectName LIKE @SearchPattern OR ExpectedValue LIKE @SearchPattern
                OR ActualValue LIKE @SearchPattern OR ConfiguredValue LIKE @SearchPattern
                OR RunningValue LIKE @SearchPattern OR Description LIKE @SearchPattern)
              AND (@Status IS NULL OR Status = @Status)
              AND (@Environment IS NULL OR Environment = @Environment)
              AND (@Category IS NULL OR Category = @Category)
            ORDER BY {{orderBy}} {{direction}}, ServerName, ParameterName
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """ + CurrentConfigurationCte + """
            SELECT COUNT(1)
            FROM CurrentConfiguration
            WHERE (@SearchPattern IS NULL OR ServerName LIKE @SearchPattern
                OR InstanceName LIKE @SearchPattern OR ParameterName LIKE @SearchPattern
                OR Category LIKE @SearchPattern OR DatabaseName LIKE @SearchPattern
                OR ObjectName LIKE @SearchPattern OR ExpectedValue LIKE @SearchPattern
                OR ActualValue LIKE @SearchPattern OR ConfiguredValue LIKE @SearchPattern
                OR RunningValue LIKE @SearchPattern OR Description LIKE @SearchPattern)
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

    private static string? FormatParameterValue(object? value) => value switch
    {
        null or DBNull => null,
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

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
