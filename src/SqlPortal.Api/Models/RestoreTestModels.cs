namespace SqlPortal.Api.Models;

public sealed record RestoreTestOverview(
    string SourceMode,
    DateTimeOffset GeneratedAtUtc,
    string Period,
    DateTimeOffset? WindowStartUtc,
    RestoreTestMetrics Metrics,
    IReadOnlyList<RestoreFailureCategory> FailureCategories);

public sealed record RestoreTestMetrics(
    int TotalDatabases,
    int SuccessfullyTestedDatabases,
    int RemainingDatabases,
    int CurrentFailureDatabases,
    int SkippedDatabases,
    int NotTestedDatabases,
    int TotalAttemptsWithinPeriod,
    int NeverSelectedDatabases,
    int SelectedOnceDatabases,
    int SelectedMultipleTimesDatabases,
    int RepeatedAttempts,
    decimal CoveragePercent);

public sealed record RestoreFailureCategory(
    string Category,
    int DatabaseCount);

public sealed record RestoreTestDatabase(
    int Id,
    string SourceServer,
    string DatabaseName,
    string PeriodStatus,
    bool CoveredWithinPeriod,
    string SelectionStatus,
    int AttemptCountWithinPeriod,
    int SuccessfulAttemptCountWithinPeriod,
    int UndatedAttemptCount,
    string LatestAttemptStatus,
    string? FailureCategory,
    string? TestServer,
    decimal? SizeGb,
    bool? BackupFileExists,
    string? RestoreResult,
    string? DbccResult,
    DateTimeOffset? LatestAttemptAtUtc,
    DateTimeOffset? LastSuccessfulAtUtc,
    string? RestoreElapsed,
    string? DbccElapsed,
    DateTimeOffset? FullDateUtc,
    string? FullPath,
    DateTimeOffset? DiffDateUtc,
    string? DiffPath,
    string? LogDates,
    string? LogPaths);

public sealed record RestoreTestHistoryDetail(
    RestoreTestDatabase Database,
    IReadOnlyList<RestoreTestHistoryEntry> History);

public sealed record RestoreTestHistoryEntry(
    int Sequence,
    string Status,
    string? FailureCategory,
    bool WithinPeriod,
    string? TestServer,
    decimal? SizeGb,
    bool? BackupFileExists,
    string? RestoreResult,
    string? DbccResult,
    DateTimeOffset? AttemptAtUtc,
    string? RestoreElapsed,
    string? DbccElapsed,
    DateTimeOffset? FullDateUtc,
    string? FullPath,
    DateTimeOffset? DiffDateUtc,
    string? DiffPath,
    string? LogDates,
    string? LogPaths);

public sealed record RestoreTestQuery(
    string? Search,
    string? Status,
    string? Selection,
    string Period,
    int Page,
    int PageSize,
    string SortBy,
    bool SortDescending)
{
    public static RestoreTestQuery Create(
        string? search,
        string? status,
        string? selection,
        string? period,
        int page,
        int pageSize,
        string? sortBy,
        string? sortDirection) =>
        new(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            string.IsNullOrWhiteSpace(selection) ? null : selection.Trim(),
            NormalizePeriod(period),
            Math.Max(1, page),
            Math.Clamp(pageSize, 10, 100),
            string.IsNullOrWhiteSpace(sortBy) ? "periodStatus" : sortBy.Trim(),
            string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase));

    public static string NormalizePeriod(string? period) => period switch
    {
        "730" => "730",
        "1095" => "1095",
        "1825" => "1825",
        "all" => "all",
        _ => "365"
    };
}
