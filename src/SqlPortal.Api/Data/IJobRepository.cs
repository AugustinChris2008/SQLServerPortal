using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public interface IJobRepository
{
    Task<IReadOnlyList<JobServerSummary>> GetServersAsync(int? historyDays, bool includeInactive, CancellationToken cancellationToken);
    Task<PagedResult<JobRun>> GetRunsAsync(JobQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobMonitoringResult>> GetMonitoringAsync(string? server, CancellationToken cancellationToken);
}
