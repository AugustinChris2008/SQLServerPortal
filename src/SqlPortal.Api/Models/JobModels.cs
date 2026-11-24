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
    bool HasUnsuccessfulHistory);

public sealed record JobRuleViolation(
    string ServerName,
    string SqlInstance,
    string JobName,
    string? Environment,
    string? ServerType,
    string RunCountCheckType,
    int? ExpectedRuns24h,
    int ActualRuns,
    int UnsuccessfulRuns,
    bool RunCountViolated,
    bool SuccessViolated,
    string? Description);

public sealed record JobQuery(
    string? Search,
    string? Server,
    string? Status,
    bool History,
    int Page,
    int PageSize,
    string? SqlInstance,
    string? JobName,
    bool IncludeMessage)
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
        bool includeMessage = true) =>
        new(
            Normalize(search),
            Normalize(server),
            status is Successful or NotSuccessful ? status : null,
            history,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 100),
            Normalize(sqlInstance),
            Normalize(jobName),
            includeMessage);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
