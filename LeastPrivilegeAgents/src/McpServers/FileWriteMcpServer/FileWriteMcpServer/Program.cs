var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var allowedOutputPath = builder.Configuration["AllowedOutputPath"]
    ?? throw new InvalidOperationException("AllowedOutputPath not configured");

app.MapGet("/.well-known/mcp", () => new
{
    name = "file-write-mcp",
    version = "1.0.0",
    tools = new[] { "write_file", "create_directory", "append_file" }
});

app.MapPost("/tools/write_file", async (WriteFileRequest req) =>
{
    var fullPath = Path.GetFullPath(req.Path);
    if (!fullPath.StartsWith(Path.GetFullPath(allowedOutputPath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    var dir = Path.GetDirectoryName(fullPath)!;
    Directory.CreateDirectory(dir);

    await File.WriteAllTextAsync(fullPath, req.Content);
    return Results.Ok(new { path = fullPath, written = req.Content.Length });
});

app.MapPost("/tools/create_directory", (CreateDirRequest req) =>
{
    var fullPath = Path.GetFullPath(req.Path);
    if (!fullPath.StartsWith(Path.GetFullPath(allowedOutputPath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    Directory.CreateDirectory(fullPath);
    return Results.Ok(new { path = fullPath });
});

app.MapPost("/tools/append_file", async (AppendFileRequest req) =>
{
    var fullPath = Path.GetFullPath(req.Path);
    if (!fullPath.StartsWith(Path.GetFullPath(allowedOutputPath), StringComparison.OrdinalIgnoreCase))
        return Results.Forbid();

    var dir = Path.GetDirectoryName(fullPath)!;
    Directory.CreateDirectory(dir);

    await File.AppendAllTextAsync(fullPath, req.Content);
    return Results.Ok(new { path = fullPath, appended = req.Content.Length });
});

app.Run();

record WriteFileRequest(string Path, string Content);
record CreateDirRequest(string Path);
record AppendFileRequest(string Path, string Content);
