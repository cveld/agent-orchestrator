var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var allowedBasePath = builder.Configuration["AllowedBasePath"]
    ?? throw new InvalidOperationException("AllowedBasePath not configured");

app.MapGet("/.well-known/mcp", () => new
{
    name = "file-readonly-mcp",
    version = "1.0.0",
    tools = new[] { "read_file", "list_directory", "file_exists" }
});

app.MapPost("/tools/read_file", async (ReadFileRequest req) =>
{
    var fullPath = Path.GetFullPath(req.Path);
    if (!fullPath.StartsWith(Path.GetFullPath(allowedBasePath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    if (!File.Exists(fullPath))
        return Results.NotFound(new { error = $"File not found: {req.Path}" });

    var content = await File.ReadAllTextAsync(fullPath);
    return Results.Ok(new { content, path = fullPath });
});

app.MapPost("/tools/list_directory", (ListDirRequest req) =>
{
    var fullPath = Path.GetFullPath(req.Path);
    if (!fullPath.StartsWith(Path.GetFullPath(allowedBasePath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    if (!Directory.Exists(fullPath))
        return Results.NotFound(new { error = $"Directory not found: {req.Path}" });

    var entries = Directory.GetFileSystemEntries(fullPath)
        .Select(e => new
        {
            name = Path.GetFileName(e),
            type = Directory.Exists(e) ? "directory" : "file"
        });
    return Results.Ok(new { entries });
});

app.MapPost("/tools/file_exists", (FileExistsRequest req) =>
{
    var fullPath = Path.GetFullPath(req.Path);
    if (!fullPath.StartsWith(Path.GetFullPath(allowedBasePath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    return Results.Ok(new { exists = File.Exists(fullPath) || Directory.Exists(fullPath) });
});

app.Run();

record ReadFileRequest(string Path);
record ListDirRequest(string Path);
record FileExistsRequest(string Path);
