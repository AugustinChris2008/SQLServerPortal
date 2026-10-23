using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public sealed class DemoPortalRepository : IPortalRepository
{
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<PortalServer> _servers;
    private readonly IReadOnlyList<PortalDatabase> _databases;
    private readonly List<ConfigurationCheck> _configurationChecks;

    public DemoPortalRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        var now = timeProvider.GetUtcNow();

        _servers =
        [
            new(1, "SQL-PROD-01", "SQL-PROD-01\\MSSQLSERVER", "Produktion", "SQL Server 2025", "Enterprise", 38, 128, now.AddHours(-26)),
            new(2, "SQL-PROD-02", "SQL-PROD-02\\MSSQLSERVER", "Produktion", "SQL Server 2022", "Enterprise", 26, 128, now.AddDays(-3)),
            new(3, "SQL-AG-DR01", "SQL-AG-DR01\\MSSQLSERVER", "Produktion", "SQL Server 2025", "Enterprise", 42, 128, now.AddDays(-6)),
            new(4, "SQL-UAT-01", "SQL-UAT-01\\MSSQLSERVER", "Abnahme", "SQL Server 2022", "Standard", 19, 64, now.AddDays(-9)),
            new(5, "SQL-DEV-01", "SQL-DEV-01\\DEV", "Entwicklung", "SQL Server 2022", "Developer", 31, 48, now.AddHours(-18)),
            new(6, "SQL-LEGACY-01", "SQL-LEGACY-01\\MSSQLSERVER", "Produktion", "SQL Server 2019", "Standard", 11, 64, now.AddDays(-21))
        ];

        _databases =
        [
            new(101, 1, "SQL-PROD-01", "ERP_Core", "Produktion", "Gesund", "ONLINE", "FULL", 170, 842, 73, 31, now.AddDays(-2), now.AddHours(-27), "svc_erp", now.AddHours(-26)),
            new(102, 1, "SQL-PROD-01", "ERP_Archive", "Produktion", "Warnung", "ONLINE", "FULL", 170, 91, 88, 22, now.AddDays(-8), now.AddDays(-2), "svc_erp", now.AddHours(-26)),
            new(103, 2, "SQL-PROD-02", "DWH_Facts", "Produktion", "Kritisch", "ONLINE", "FULL", 160, 2418, 92, 89, now.AddDays(-4), now.AddDays(-3), "svc_dwh", now.AddDays(-3)),
            new(104, 2, "SQL-PROD-02", "DWH_Stage", "Produktion", "Warnung", "ONLINE", "SIMPLE", 160, 530, 84, 68, now.AddDays(-4), null, "svc_dwh", now.AddDays(-3)),
            new(105, 3, "SQL-AG-DR01", "Customer360", "Produktion", "Gesund", "ONLINE", "FULL", 170, 684, 64, 17, now.AddDays(-7), now.AddDays(-6), "svc_crm", now.AddDays(-6)),
            new(106, 3, "SQL-AG-DR01", "OrderHub", "Produktion", "Gesund", "ONLINE", "FULL", 170, 389, 58, 14, now.AddDays(-7), now.AddDays(-6), "svc_orders", now.AddDays(-6)),
            new(107, 4, "SQL-UAT-01", "ERP_UAT", "Abnahme", "Gesund", "ONLINE", "SIMPLE", 160, 196, 52, 12, now.AddDays(-10), null, "svc_erp", now.AddDays(-9)),
            new(108, 4, "SQL-UAT-01", "Integration_UAT", "Abnahme", "Warnung", "RECOVERY_PENDING", "FULL", 160, 48, 71, 44, now.AddDays(-10), now.AddDays(-9), "svc_integration", now.AddDays(-9)),
            new(109, 5, "SQL-DEV-01", "DevPlayground", "Entwicklung", "Gesund", "ONLINE", "SIMPLE", 160, 72, 38, 9, now.AddDays(-3), null, "dev_team", now.AddHours(-18)),
            new(110, 6, "SQL-LEGACY-01", "LegacyBilling", "Produktion", "Kritisch", "UNKNOWN", "FULL", 150, 218, null, null, now.AddDays(-28), now.AddDays(-28), "sa", now.AddDays(-21))
        ];

        var checks = new List<ConfigurationCheck>();
        var nextCheckId = 1;
        foreach (var server in _servers)
        {
            var expectedMemory = server.Id switch
            {
                4 => "49152",
                5 => "40960",
                6 => "49152",
                _ => "110592"
            };

            AddCheck(server, "Arbeitsspeicher", "max server memory (MB)", expectedMemory,
                ActualValue(server.Id, "memory", expectedMemory), "MB");
            AddCheck(server, "Parallelität", "cost threshold for parallelism", "50",
                ActualValue(server.Id, "cost", "50"), null);
            AddCheck(server, "Parallelität", "max degree of parallelism", "8",
                ActualValue(server.Id, "maxdop", "8"), null);
            AddCheck(server, "Datenbankstandard", "backup compression default", "1",
                ActualValue(server.Id, "compression", "1"), null);
            AddCheck(server, "Sicherheit", "remote admin connections", "1",
                ActualValue(server.Id, "remote-admin", "1"), null);
            AddCheck(server, "Datenbankstandard", "optimize for ad hoc workloads", "1",
                ActualValue(server.Id, "ad-hoc", "1"), null);
            AddCheck(server, "Sicherheit", "xp_cmdshell", "0",
                ActualValue(server.Id, "xp-cmdshell", "0"), null);
            AddCheck(server, "TempDB", "TempDB data files", "8",
                ActualValue(server.Id, "tempdb-files", "8"), "Dateien");
        }

        _configurationChecks = checks;

        void AddCheck(
            PortalServer server,
            string category,
            string parameterName,
            string expectedValue,
            string? actualValue,
            string? unit)
        {
            var status = actualValue is null
                ? "Nicht ermittelt"
                : string.Equals(actualValue, expectedValue, StringComparison.OrdinalIgnoreCase)
                    ? "Konform"
                    : "Abweichung";
            var deviation = status switch
            {
                "Abweichung" => $"IST {actualValue}, erwartet {expectedValue}",
                "Nicht ermittelt" => "Kein aktueller IST-Wert vorhanden",
                _ => null
            };

            checks.Add(new ConfigurationCheck(
                nextCheckId++,
                server.Id,
                server.Name,
                server.Environment,
                category,
                parameterName,
                expectedValue,
                actualValue,
                unit,
                status,
                deviation,
                server.CollectedAtUtc));
        }

        static string? ActualValue(int serverId, string key, string expectedValue)
        {
            if (serverId == 6)
            {
                return null;
            }

            return (serverId, key) switch
            {
                (2, "memory") => "98304",
                (2, "cost") => "5",
                (2, "remote-admin") => "0",
                (3, "ad-hoc") => "0",
                (4, "maxdop") => "4",
                (4, "tempdb-files") => "4",
                (5, "xp-cmdshell") => "1",
                (5, "compression") => "0",
                _ => expectedValue
            };
        }
    }

    public Task<PortalOverview> GetOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();

        var environments = _servers
            .GroupBy(server => server.Environment)
            .Select(group => new EnvironmentSummary(
                group.Key,
                group.Count(),
                _databases.Count(database => database.Environment == group.Key)))
            .OrderByDescending(item => item.ServerCount)
            .ToArray();

        var alerts = _databases
            .Where(database => database.Status != "Gesund")
            .OrderBy(database => database.Status == "Kritisch" ? 0 : 1)
            .ThenByDescending(database => database.LogUsedPercent)
            .Take(6)
            .Select(database => new DatabaseAlert(
                database.Id,
                database.ServerName,
                database.Name,
                database.Environment,
                database.Status,
                database.SizeGb,
                database.LogUsedPercent,
                database.LastFullBackupAtUtc,
                database.CollectedAtUtc))
            .ToArray();

        var metrics = new OverviewMetrics(
            _servers.Count,
            _servers.Count(server => server.CollectedAtUtc >= now.AddDays(-1)),
            _servers.Count(server => server.CollectedAtUtc < now.AddDays(-1) && server.CollectedAtUtc >= now.AddDays(-7)),
            _servers.Count(server => server.CollectedAtUtc < now.AddDays(-7)),
            _databases.Count,
            _databases.Count(database => database.Status == "Gesund"),
            _databases.Count(database => database.Status != "Gesund"),
            _databases.Sum(database => database.SizeGb),
            _servers.Sum(server => server.MemoryGb ?? 0));

        var overview = new PortalOverview(
            "Demo",
            _timeProvider.GetUtcNow(),
            _servers.Min(server => server.CollectedAtUtc),
            metrics,
            environments,
            _servers.OrderBy(server => server.CollectedAtUtc).Take(6).ToArray(),
            alerts);

        return Task.FromResult(overview);
    }

    public Task<PagedResult<PortalServer>> GetServersAsync(
        PortalQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<PortalServer> result = _servers;

        if (query.Search is not null)
        {
            result = result.Where(server =>
                $"{server.Name} {server.InstanceName} {server.Version} {server.Edition}"
                    .Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Environment is not null)
        {
            result = result.Where(server =>
                string.Equals(server.Environment, query.Environment, StringComparison.OrdinalIgnoreCase));
        }

        result = (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("databasecount", false) => result.OrderBy(server => server.DatabaseCount),
            ("databasecount", true) => result.OrderByDescending(server => server.DatabaseCount),
            ("memorygb", false) => result.OrderBy(server => server.MemoryGb),
            ("memorygb", true) => result.OrderByDescending(server => server.MemoryGb),
            ("collectedatutc", false) => result.OrderBy(server => server.CollectedAtUtc),
            ("collectedatutc", true) => result.OrderByDescending(server => server.CollectedAtUtc),
            (_, true) => result.OrderByDescending(server => server.Name),
            _ => result.OrderBy(server => server.Name)
        };

        return Task.FromResult(ToPage(result, query));
    }

    public Task<PagedResult<PortalDatabase>> GetDatabasesAsync(
        PortalQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<PortalDatabase> result = _databases;
        result = ApplyFilters(result, query,
            database => $"{database.ServerName} {database.Name} {database.Owner}",
            database => database.Status,
            database => database.Environment);

        result = (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("status", false) => result.OrderBy(database => StatusOrder(database.Status)),
            ("status", true) => result.OrderByDescending(database => StatusOrder(database.Status)),
            ("sizegb", false) => result.OrderBy(database => database.SizeGb),
            ("sizegb", true) => result.OrderByDescending(database => database.SizeGb),
            ("logusedpercent", false) => result.OrderBy(database => database.LogUsedPercent),
            ("logusedpercent", true) => result.OrderByDescending(database => database.LogUsedPercent),
            (_, true) => result.OrderByDescending(database => database.Name),
            _ => result.OrderBy(database => database.Name)
        };

        return Task.FromResult(ToPage(result, query));
    }

    public Task<ConfigurationOverview> GetConfigurationOverviewAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var total = _configurationChecks.Count;
        var compliant = _configurationChecks.Count(check => check.Status == "Konform");
        var drifted = _configurationChecks.Count(check => check.Status == "Abweichung");
        var unknown = _configurationChecks.Count(check => check.Status == "Nicht ermittelt");
        var metrics = new ConfigurationMetrics(
            total,
            compliant,
            drifted,
            unknown,
            Percentage(compliant, total));

        var servers = _configurationChecks
            .GroupBy(check => new { check.ServerId, check.ServerName, check.Environment })
            .Select(group => new ServerConfigurationSummary(
                group.Key.ServerId,
                group.Key.ServerName,
                group.Key.Environment,
                group.Count(),
                group.Count(check => check.Status == "Konform"),
                group.Count(check => check.Status == "Abweichung"),
                group.Count(check => check.Status == "Nicht ermittelt"),
                Percentage(group.Count(check => check.Status == "Konform"), group.Count()),
                group.Max(check => check.CollectedAtUtc)))
            .OrderByDescending(server => server.DriftedChecks)
            .ThenByDescending(server => server.UnknownChecks)
            .ThenBy(server => server.ServerName)
            .ToArray();

        var overview = new ConfigurationOverview(
            "Demo",
            _timeProvider.GetUtcNow(),
            metrics,
            _configurationChecks.Select(check => check.Category).Distinct().Order().ToArray(),
            servers);

        return Task.FromResult(overview);
    }

    public Task<PagedResult<ConfigurationCheck>> GetConfigurationChecksAsync(
        ConfigurationQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<ConfigurationCheck> result = _configurationChecks;

        if (query.Search is not null)
        {
            result = result.Where(check =>
                $"{check.ServerName} {check.ParameterName} {check.Category} {check.ExpectedValue} {check.ActualValue}"
                    .Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Status is not null)
        {
            result = result.Where(check =>
                string.Equals(check.Status, query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Environment is not null)
        {
            result = result.Where(check =>
                string.Equals(check.Environment, query.Environment, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Category is not null)
        {
            result = result.Where(check =>
                string.Equals(check.Category, query.Category, StringComparison.OrdinalIgnoreCase));
        }

        result = (query.SortBy.ToLowerInvariant(), query.SortDescending) switch
        {
            ("servername", false) => result.OrderBy(check => check.ServerName).ThenBy(check => check.ParameterName),
            ("servername", true) => result.OrderByDescending(check => check.ServerName).ThenBy(check => check.ParameterName),
            ("parametername", false) => result.OrderBy(check => check.ParameterName).ThenBy(check => check.ServerName),
            ("parametername", true) => result.OrderByDescending(check => check.ParameterName).ThenBy(check => check.ServerName),
            ("category", false) => result.OrderBy(check => check.Category).ThenBy(check => check.ParameterName),
            ("category", true) => result.OrderByDescending(check => check.Category).ThenBy(check => check.ParameterName),
            ("status", true) => result.OrderByDescending(check => ConfigurationStatusOrder(check.Status)).ThenBy(check => check.ServerName),
            _ => result.OrderBy(check => ConfigurationStatusOrder(check.Status)).ThenBy(check => check.ServerName)
        };

        var materialized = result.ToArray();
        var items = materialized
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();
        return Task.FromResult(new PagedResult<ConfigurationCheck>(
            items,
            query.Page,
            query.PageSize,
            materialized.Length));
    }

    private static IEnumerable<T> ApplyFilters<T>(
        IEnumerable<T> source,
        PortalQuery query,
        Func<T, string> searchValue,
        Func<T, string> statusValue,
        Func<T, string> environmentValue)
    {
        if (query.Search is not null)
        {
            source = source.Where(item =>
                searchValue(item).Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Status is not null)
        {
            source = source.Where(item =>
                string.Equals(statusValue(item), query.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Environment is not null)
        {
            source = source.Where(item =>
                string.Equals(environmentValue(item), query.Environment, StringComparison.OrdinalIgnoreCase));
        }

        return source;
    }

    private static PagedResult<T> ToPage<T>(IEnumerable<T> source, PortalQuery query)
    {
        var materialized = source.ToArray();
        var items = materialized
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();
        return new PagedResult<T>(items, query.Page, query.PageSize, materialized.Length);
    }

    private static int StatusOrder(string status) => status switch
    {
        "Kritisch" or "Offline" => 0,
        "Warnung" => 1,
        _ => 2
    };

    private static int ConfigurationStatusOrder(string status) => status switch
    {
        "Abweichung" => 0,
        "Nicht ermittelt" => 1,
        _ => 2
    };

    private static decimal Percentage(int value, int total) =>
        total == 0 ? 0 : Math.Round(value * 100m / total, 1);
}

