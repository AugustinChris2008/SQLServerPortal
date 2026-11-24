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

        api.MapGet("/analytics/overview", GetAnalyticsOverview)
            .WithName("GetAnalyticsOverview")
            .WithSummary("Returns CPU licensing and SQL Server distribution analytics.");

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

        api.MapGet("/restore-tests/overview", GetRestoreTestOverview)
            .WithName("GetRestoreTestOverview")
            .WithSummary("Returns the rolling one-year automatic restore-test coverage.");

        api.MapGet("/restore-tests/databases", GetRestoreTestDatabases)
            .WithName("GetRestoreTestDatabases")
            .WithSummary("Returns restore-test coverage and results for inventory databases.");

        api.MapGet("/restore-tests/databases/{databaseId:int}/history", GetRestoreTestHistory)
            .WithName("GetRestoreTestHistory")
            .WithSummary("Returns the complete restore-test history for one inventory database.");

        var jobs = api.MapGroup("/jobs");
        jobs.MapGet("/servers", GetJobServers)
            .WithName("GetJobServers")
            .WithSummary("Returns job outcome counts in the requested history period and inventory metadata per server.");

        jobs.MapGet("/runs", GetJobRuns)
            .WithName("GetJobRuns")
            .WithSummary("Returns filtered job outcomes, optionally including previous runs in the requested history period.");

        jobs.MapGet("/monitoring", GetJobMonitoring)
            .WithName("GetJobMonitoring")
            .WithSummary("Returns rule evaluations based on each rule's monitoring window.");

        jobs.MapGet("/violations", GetJobViolations)
            .WithName("GetJobViolations")
            .WithSummary("Returns failed rule evaluations based on each rule's monitoring window.");

        return app;
    }

    private static async Task<IResult> GetJobServers(
        int? historyDays,
        bool? includeInactive,
        IJobRepository repository,
        CancellationToken cancellationToken) =>
        JobQuery.IsValidHistoryDays(historyDays)
            ? Results.Ok(await repository.GetServersAsync(historyDays, includeInactive ?? false, cancellationToken))
            : InvalidJobHistoryDays();

    private static async Task<IResult> GetJobRuns(
        string? search,
        string? server,
        string? status,
        bool? history,
        int? page,
        int? pageSize,
        string? sqlInstance,
        string? jobName,
        bool? includeMessage,
        int? historyDays,
        IJobRepository repository,
        CancellationToken cancellationToken) =>
        JobQuery.IsValidHistoryDays(historyDays)
            ? Results.Ok(await repository.GetRunsAsync(
                JobQuery.Create(search, server, status, history ?? false, page ?? 1, pageSize ?? 20,
                    sqlInstance, jobName, includeMessage ?? true, historyDays),
                cancellationToken))
            : InvalidJobHistoryDays();

    private static IResult InvalidJobHistoryDays() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["historyDays"] = ["The history period must be between 1 and 90 days."]
    });

    private static async Task<IResult> GetJobMonitoring(
        string? server,
        IJobRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetMonitoringAsync(
            string.IsNullOrWhiteSpace(server) ? null : server.Trim(),
            cancellationToken));

    private static async Task<IResult> GetJobViolations(
        string? server,
        IJobRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok((await repository.GetMonitoringAsync(
            string.IsNullOrWhiteSpace(server) ? null : server.Trim(),
            cancellationToken)).Where(result => result.OverallStatus == "Failed"));

    private static async Task<IResult> GetOverview(
        IPortalRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetOverviewAsync(cancellationToken));

    private static async Task<IResult> GetAnalyticsOverview(
        IPortalRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetAnalyticsOverviewAsync(cancellationToken));

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

    private static async Task<IResult> GetRestoreTestOverview(
        string? period,
        IRestoreTestRepository repository,
        CancellationToken cancellationToken) =>
        Results.Ok(await repository.GetOverviewAsync(
            RestoreTestQuery.NormalizePeriod(period),
            cancellationToken));

    private static async Task<IResult> GetRestoreTestDatabases(
        string? search,
        string? status,
        string? selection,
        string? period,
        int? page,
        int? pageSize,
        string? sortBy,
        string? sortDirection,
        IRestoreTestRepository repository,
        CancellationToken cancellationToken)
    {
        var query = RestoreTestQuery.Create(
            search,
            status,
            selection,
            period,
            page ?? 1,
            pageSize ?? 20,
            sortBy,
            sortDirection);
        return Results.Ok(await repository.GetDatabasesAsync(query, cancellationToken));
    }

    private static async Task<IResult> GetRestoreTestHistory(
        int databaseId,
        string? period,
        IRestoreTestRepository repository,
        CancellationToken cancellationToken)
    {
        var detail = await repository.GetHistoryAsync(
            databaseId,
            RestoreTestQuery.NormalizePeriod(period),
            cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }
}

