using SqlPortal.Api.Data;
using SqlPortal.Api.Models;

namespace SqlPortal.Api;

public static class PortalApi
{
    public static WebApplication MapPortalApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1")
            .WithTags("SQL Portal");

        api.MapGet("/overview", GetOverview)
            .WithName("GetOverview")
            .WithSummary("Returns the portal dashboard summary.");

        api.MapGet("/servers", GetServers)
            .WithName("GetServers")
            .WithSummary("Returns a filtered and paged server list.");

        api.MapGet("/servers/filters", GetServerFilterOptions)
            .WithName("GetServerFilterOptions")
            .WithSummary("Returns available SQL Server filters.");

        api.MapGet("/servers/{serverId:int}", GetServerDetail)
            .WithName("GetServerDetail")
            .WithSummary("Returns inventory and compliance details for one SQL Server.");

        api.MapGet("/databases", GetDatabases)
            .WithName("GetDatabases")
            .WithSummary("Returns a filtered and paged database list.");

        api.MapGet("/databases/{databaseId:int}", GetDatabaseDetail)
            .WithName("GetDatabaseDetail")
            .WithSummary("Returns all captured information for one database.");

        api.MapGet("/configuration/overview", GetConfigurationOverview)
            .WithName("GetConfigurationOverview")
            .WithSummary("Returns SQL Server configuration compliance metrics.");

        api.MapGet("/configuration/checks", GetConfigurationChecks)
            .WithName("GetConfigurationChecks")
            .WithSummary("Returns filtered and paged desired/actual configuration checks.");

        return app;
    }

    private static async Task<IResult> GetOverview(
        IPortalRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetOverviewAsync(cancellationToken));

    private static async Task<IResult> GetServers(
        string? search,
        string? environment,
        string? version,
        string? edition,
        int? page,
        int? pageSize,
        string? sortBy,
        string? sortDirection,
        IPortalRepository repository,
        CancellationToken cancellationToken)
    {
        var query = PortalQuery.Create(
            search,
            null,
            environment,
            page ?? 1,
            pageSize ?? 20,
            sortBy,
            sortDirection,
            "name",
            version,
            edition);
        return Results.Ok(await repository.GetServersAsync(query, cancellationToken));
    }

    private static async Task<IResult> GetServerFilterOptions(
        IPortalRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetServerFilterOptionsAsync(cancellationToken));

    private static async Task<IResult> GetServerDetail(
        int serverId,
        IPortalRepository repository,
        CancellationToken cancellationToken)
    {
        var detail = await repository.GetServerDetailAsync(serverId, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> GetDatabases(
        string? search,
        string? status,
        string? environment,
        int? page,
        int? pageSize,
        string? sortBy,
        string? sortDirection,
        IPortalRepository repository,
        CancellationToken cancellationToken)
    {
        var query = PortalQuery.Create(
            search,
            status,
            environment,
            page ?? 1,
            pageSize ?? 20,
            sortBy,
            sortDirection,
            "name");
        return Results.Ok(await repository.GetDatabasesAsync(query, cancellationToken));
    }

    private static async Task<IResult> GetDatabaseDetail(
        int databaseId,
        IPortalRepository repository,
        CancellationToken cancellationToken)
    {
        var detail = await repository.GetDatabaseDetailAsync(databaseId, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> GetConfigurationOverview(
        IPortalRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetConfigurationOverviewAsync(cancellationToken));

    private static async Task<IResult> GetConfigurationChecks(
        string? search,
        string? status,
        string? environment,
        string? category,
        int? page,
        int? pageSize,
        string? sortBy,
        string? sortDirection,
        IPortalRepository repository,
        CancellationToken cancellationToken)
    {
        var query = ConfigurationQuery.Create(
            search,
            status,
            environment,
            category,
            page ?? 1,
            pageSize ?? 20,
            sortBy,
            sortDirection);
        return Results.Ok(await repository.GetConfigurationChecksAsync(query, cancellationToken));
    }
}

