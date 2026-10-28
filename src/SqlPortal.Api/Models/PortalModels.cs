namespace SqlPortal.Api.Models;

public sealed record PortalServer(
    int Id,
    string Name,
    string InstanceName,
    string Environment,
    string Version,
    string Edition,
    int DatabaseCount,
    decimal? MemoryGb,
    DateTimeOffset? CollectedAtUtc,
    string? Description);

public sealed record ServerFilterOptions(
    IReadOnlyList<string> Environments,
    IReadOnlyList<string> Versions,
    IReadOnlyList<string> Editions);

public sealed record ServerParameter(
    string Name,
    string? Value);

public sealed record ServerDetail(
    PortalServer Server,
    IReadOnlyList<ServerParameter> Parameters,
    IReadOnlyList<PortalDatabase> Databases,
    IReadOnlyList<ConfigurationCheck> ConfigurationChecks);

public sealed record PortalDatabase(
    int Id,
    int ServerId,
    string ServerName,
    string Name,
    string Environment,
    string Status,
    string State,
    string RecoveryModel,
    int? CompatibilityLevel,
    decimal? SizeGb,
    decimal? DataUsedPercent,
    decimal? LogUsedPercent,
    DateTimeOffset? LastFullBackupAtUtc,
    DateTimeOffset? LastLogBackupAtUtc,
    string Owner,
    DateTimeOffset? CollectedAtUtc);

public sealed record DatabaseDetail(
    PortalDatabase Database,
    IReadOnlyList<ServerParameter> Parameters,
    IReadOnlyList<ConfigurationCheck> ConfigurationChecks);

public sealed record PortalOverview(
    string SourceMode,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? OldestCollectedAtUtc,
    OverviewMetrics Metrics,
    IReadOnlyList<EnvironmentSummary> Environments,
    IReadOnlyList<PortalServer> FocusServers,
    IReadOnlyList<DatabaseAlert> DatabaseAlerts);

public sealed record OverviewMetrics(
    int TotalServers,
    int RecentServers,
    int WeekOldServers,
    int OlderServers,
    int TotalDatabases,
    int HealthyDatabases,
    int AttentionDatabases,
    decimal TotalSizeGb,
    decimal TotalMemoryGb);

public sealed record EnvironmentSummary(
    string Environment,
    int ServerCount,
    int DatabaseCount);

public sealed record DatabaseAlert(
    int Id,
    string ServerName,
    string Name,
    string Environment,
    string Status,
    decimal? SizeGb,
    decimal? LogUsedPercent,
    DateTimeOffset? LastFullBackupAtUtc,
    DateTimeOffset? CollectedAtUtc);

public sealed record ConfigurationCheck(
    int Id,
    int ServerId,
    string ServerName,
    string Environment,
    string Category,
    string ParameterName,
    string ExpectedValue,
    string? ActualValue,
    string? Unit,
    string Status,
    string? Deviation,
    DateTimeOffset CollectedAtUtc,
    string? InstanceName,
    string? DatabaseName,
    string? ObjectName,
    string? ConfiguredValue,
    string? RunningValue,
    bool IsCompliant,
    string? ComplianceState,
    string? Description);

public sealed record ConfigurationOverview(
    string SourceMode,
    DateTimeOffset GeneratedAtUtc,
    ConfigurationMetrics Metrics,
    IReadOnlyList<string> Categories,
    IReadOnlyList<ServerConfigurationSummary> Servers);

public sealed record ConfigurationMetrics(
    int TotalChecks,
    int DefinedChecks,
    int CompliantChecks,
    int DriftedChecks,
    int UnknownChecks,
    decimal CompliancePercent);

public sealed record ServerConfigurationSummary(
    int ServerId,
    string ServerName,
    string Environment,
    int TotalChecks,
    int CompliantChecks,
    int DriftedChecks,
    int UnknownChecks,
    decimal CompliancePercent,
    DateTimeOffset CollectedAtUtc);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems)
{
    public int TotalPages => TotalItems == 0 ? 1 : (int)Math.Ceiling(TotalItems / (double)PageSize);
}

public sealed record PortalQuery(
    string? Search,
    string? Status,
    string? Environment,
    string? Version,
    string? Edition,
    int Page,
    int PageSize,
    string SortBy,
    bool SortDescending)
{
    public static PortalQuery Create(
        string? search,
        string? status,
        string? environment,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDirection,
        string defaultSort,
        string? version = null,
        string? edition = null) =>
        new(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            string.IsNullOrWhiteSpace(environment) ? null : environment.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
            string.IsNullOrWhiteSpace(edition) ? null : edition.Trim(),
            Math.Max(1, page),
            Math.Clamp(pageSize, 10, 100),
            string.IsNullOrWhiteSpace(sortBy) ? defaultSort : sortBy.Trim(),
            string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase));
}

public sealed record ConfigurationQuery(
    string? Search,
    string? Status,
    string? Environment,
    string? Category,
    int Page,
    int PageSize,
    string SortBy,
    bool SortDescending)
{
    public static ConfigurationQuery Create(
        string? search,
        string? status,
        string? environment,
        string? category,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDirection) =>
        new(
            Normalize(search),
            Normalize(status),
            Normalize(environment),
            Normalize(category),
            Math.Max(1, page),
            Math.Clamp(pageSize, 10, 100),
            string.IsNullOrWhiteSpace(sortBy) ? "status" : sortBy.Trim(),
            string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

