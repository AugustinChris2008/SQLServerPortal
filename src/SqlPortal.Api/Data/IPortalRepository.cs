using SqlPortal.Api.Models;

namespace SqlPortal.Api.Data;

public interface IPortalRepository
{
    Task<PortalOverview> GetOverviewAsync(CancellationToken cancellationToken);

    Task<PagedResult<PortalServer>> GetServersAsync(
        PortalQuery query,
        CancellationToken cancellationToken);

    Task<ServerFilterOptions> GetServerFilterOptionsAsync(
        CancellationToken cancellationToken);

    Task<ServerDetail?> GetServerDetailAsync(
        int serverId,
        CancellationToken cancellationToken);

    Task<PagedResult<PortalDatabase>> GetDatabasesAsync(
        PortalQuery query,
        CancellationToken cancellationToken);

    Task<DatabaseDetail?> GetDatabaseDetailAsync(
        int databaseId,
        CancellationToken cancellationToken);

    Task<ConfigurationOverview> GetConfigurationOverviewAsync(
        CancellationToken cancellationToken);

    Task<PagedResult<ConfigurationCheck>> GetConfigurationChecksAsync(
        ConfigurationQuery query,
        CancellationToken cancellationToken);
}

