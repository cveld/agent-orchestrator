using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var orgUrl = builder.Configuration["AzureDevOps:OrganizationUrl"]
    ?? throw new InvalidOperationException("AzureDevOps:OrganizationUrl not configured");
var pat = builder.Configuration["AzureDevOps:PersonalAccessToken"]
    ?? throw new InvalidOperationException("AzureDevOps:PersonalAccessToken not configured");

var connection = new VssConnection(new Uri(orgUrl), new VssBasicCredential(string.Empty, pat));

app.MapGet("/.well-known/mcp", () => new
{
    name = "azure-devops-readonly-mcp",
    version = "1.0.0",
    tools = new[] { "list_projects", "list_repositories", "get_repository", "list_pipelines", "get_pipeline_runs", "list_work_items" }
});

app.MapPost("/tools/list_projects", async () =>
{
    var projectClient = await connection.GetClientAsync<ProjectHttpClient>();
    var projects = await projectClient.GetProjects();
    var result = projects.Select(p => new { id = p.Id, name = p.Name, state = p.State.ToString() });
    return Results.Ok(new { projects = result });
});

app.MapPost("/tools/list_repositories", async (ListReposRequest req) =>
{
    var gitClient = await connection.GetClientAsync<GitHttpClient>();
    var repos = await gitClient.GetRepositoriesAsync(req.ProjectName);
    var result = repos.Select(r => new { id = r.Id, name = r.Name, remoteUrl = r.RemoteUrl, defaultBranch = r.DefaultBranch });
    return Results.Ok(new { repositories = result });
});

app.MapPost("/tools/get_repository", async (GetRepoRequest req) =>
{
    var gitClient = await connection.GetClientAsync<GitHttpClient>();
    var repo = await gitClient.GetRepositoryAsync(req.ProjectName, req.RepositoryName);
    return Results.Ok(new { id = repo.Id, name = repo.Name, remoteUrl = repo.RemoteUrl, defaultBranch = repo.DefaultBranch, size = repo.Size });
});

app.MapPost("/tools/list_pipelines", async (ListPipelinesRequest req) =>
{
    var buildClient = await connection.GetClientAsync<BuildHttpClient>();
    var definitions = await buildClient.GetDefinitionsAsync(req.ProjectName);
    var result = definitions.Select(d => new { id = d.Id, name = d.Name, path = d.Path, type = d.Type.ToString() });
    return Results.Ok(new { pipelines = result });
});

app.MapPost("/tools/get_pipeline_runs", async (GetPipelineRunsRequest req) =>
{
    var buildClient = await connection.GetClientAsync<BuildHttpClient>();
    var builds = await buildClient.GetBuildsAsync(req.ProjectName, definitions: [req.PipelineId], top: req.Top ?? 10);
    var result = builds.Select(b => new { id = b.Id, buildNumber = b.BuildNumber, status = b.Status.ToString(), result = b.Result?.ToString(), startTime = b.StartTime, finishTime = b.FinishTime });
    return Results.Ok(new { runs = result });
});

app.MapPost("/tools/list_work_items", async (ListWorkItemsRequest req) =>
{
    var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>();
    var wiql = new Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.Wiql
    {
        Query = $"SELECT [System.Id],[System.Title],[System.State],[System.WorkItemType] FROM WorkItems WHERE [System.TeamProject] = '{req.ProjectName}' AND [System.WorkItemType] IN ('Bug','Task','User Story','Feature') ORDER BY [System.ChangedDate] DESC"
    };
    var queryResult = await witClient.QueryByWiqlAsync(wiql, top: req.Top ?? 20);
    var ids = queryResult.WorkItems.Select(wi => wi.Id).ToArray();
    if (ids.Length == 0)
        return Results.Ok(new { workItems = Array.Empty<object>() });

    var workItems = await witClient.GetWorkItemsAsync(ids, fields: ["System.Id", "System.Title", "System.State", "System.WorkItemType"]);
    var result = workItems.Select(wi => new
    {
        id = wi.Id,
        title = wi.Fields.GetValueOrDefault("System.Title"),
        state = wi.Fields.GetValueOrDefault("System.State"),
        type = wi.Fields.GetValueOrDefault("System.WorkItemType")
    });
    return Results.Ok(new { workItems = result });
});

app.Run();

record ListReposRequest(string ProjectName);
record GetRepoRequest(string ProjectName, string RepositoryName);
record ListPipelinesRequest(string ProjectName);
record GetPipelineRunsRequest(string ProjectName, int PipelineId, int? Top);
record ListWorkItemsRequest(string ProjectName, int? Top);
