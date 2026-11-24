using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public interface IJobRepository
{
    Task<IReadOnlyList<JobServerSummary>> GetServersAsync(CancellationToken cancellationToken);
    Task<PagedResult<JobRun>> GetRunsAsync(JobQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobRuleViolation>> GetViolationsAsync(string? server, CancellationToken cancellationToken);
}
