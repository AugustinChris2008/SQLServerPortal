namespace SqlPortal.Api.Models;

public sealed record JobRun(
    string ServerName,
    string SqlInstance,
    string JobName,
    DateTime? StartedAt,
    DateTime? EndedAt,
    string? Duration,
    string Status,
    string? Message);

public sealed record JobServerSummary(
    string ServerName,
    int TotalJobs,
    int SuccessfulJobs,
    bool HasUnsuccessfulHistory,
    string? Environment,
    string? ServerType,
    DateTimeOffset? LastScanAtUtc);

public sealed record JobMonitoringResult(
    string ServerName,
    string SqlInstance,
    string JobName,
    string? Environment,
    string? ServerType,
    string RunCountCheckType,
    int? ExpectedRuns,
    int MonitoringWindowHours,
    bool CheckSuccess,
    int ActualRuns,
    int SuccessfulRuns,
    int FailedRuns,
    string? Description)
{
    private const string Ok = "OK";
    private const string Failed = "Failed";
    private const string NotChecked = "NotChecked";

    public string RunCountStatus => RunCountCheckType switch
    {
        "EXACT" => ActualRuns == ExpectedRuns ? Ok : Failed,
        "MINIMUM" => ExpectedRuns is int expected && ActualRuns >= expected ? Ok : Failed,
        _ => NotChecked
    };

    public string SuccessStatus => !CheckSuccess ? NotChecked : FailedRuns > 0 ? Failed : Ok;
    public string OverallStatus => RunCountStatus == Failed || SuccessStatus == Failed ? Failed : Ok;
    public bool IsMissingRun => RunCountStatus == Failed && ActualRuns == 0 && ExpectedRuns > 0;
}

public sealed record JobQuery(
    string? Search,
    string? Server,
    string? Status,
    bool History,
    int Page,
    int PageSize,
    string? SqlInstance,
    string? JobName,
    bool IncludeMessage,
    int? HistoryDays)
{
    public const string Successful = "successful";
    public const string NotSuccessful = "not-successful";

    public static JobQuery Create(
        string? search,
        string? server,
        string? status,
        bool history,
        int page,
        int pageSize,
        string? sqlInstance = null,
        string? jobName = null,
        bool includeMessage = true,
        int? historyDays = null) =>
        new(
            Normalize(search),
            Normalize(server),
            status is Successful or NotSuccessful ? status : null,
            history,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 100),
            Normalize(sqlInstance),
            Normalize(jobName),
            includeMessage,
            historyDays);

    public static bool IsValidHistoryDays(int? historyDays) => historyDays is null or (>= 1 and <= 90);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
