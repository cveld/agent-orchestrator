using LibGit2Sharp;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var cloneBasePath = builder.Configuration["CloneBasePath"]
    ?? throw new InvalidOperationException("CloneBasePath not configured");
var pat = builder.Configuration["Git:PersonalAccessToken"] ?? string.Empty;

app.MapGet("/.well-known/mcp", () => new
{
    name = "git-mcp",
    version = "1.0.0",
    tools = new[] { "clone_repository", "list_branches" }
});

app.MapPost("/tools/clone_repository", (CloneRepositoryRequest req) =>
{
    var repoName = req.LocalDirectory ?? Path.GetFileNameWithoutExtension(req.RepositoryUrl.TrimEnd('/'));
    var targetPath = Path.GetFullPath(Path.Combine(cloneBasePath, repoName));

    if (!targetPath.StartsWith(Path.GetFullPath(cloneBasePath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    if (Directory.Exists(targetPath))
        return Results.Conflict(new { error = $"Directory already exists: {targetPath}" });

    var options = new CloneOptions();
    if (!string.IsNullOrEmpty(pat))
    {
        options.FetchOptions.CredentialsProvider = (_, _, _) =>
            new UsernamePasswordCredentials { Username = "pat", Password = pat };
    }

    Repository.Clone(req.RepositoryUrl, targetPath, options);
    return Results.Ok(new { path = targetPath, repositoryUrl = req.RepositoryUrl });
});

app.MapPost("/tools/list_branches", (ListBranchesRequest req) =>
{
    var repoPath = Path.GetFullPath(Path.Combine(cloneBasePath, req.LocalDirectory));

    if (!repoPath.StartsWith(Path.GetFullPath(cloneBasePath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    if (!Repository.IsValid(repoPath))
        return Results.NotFound(new { error = $"Not a git repository: {repoPath}" });

    using var repo = new Repository(repoPath);
    var branches = repo.Branches
        .Where(b => !b.IsRemote)
        .Select(b => new { name = b.FriendlyName, isCurrentRepositoryHead = b.IsCurrentRepositoryHead, tip = b.Tip?.Sha });
    return Results.Ok(new { branches });
});

app.Run();

record CloneRepositoryRequest(string RepositoryUrl, string? LocalDirectory);
record ListBranchesRequest(string LocalDirectory);
