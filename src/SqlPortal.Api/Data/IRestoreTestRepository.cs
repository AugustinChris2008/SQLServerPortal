using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public interface IRestoreTestRepository
{
    Task<RestoreTestOverview> GetOverviewAsync(
        string period,
        CancellationToken cancellationToken);

    Task<PagedResult<RestoreTestDatabase>> GetDatabasesAsync(
        RestoreTestQuery query,
        CancellationToken cancellationToken);

    Task<RestoreTestHistoryDetail?> GetHistoryAsync(
        int databaseId,
        string period,
        CancellationToken cancellationToken);
}
