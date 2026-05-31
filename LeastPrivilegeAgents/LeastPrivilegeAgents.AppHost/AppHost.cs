var builder = DistributedApplication.CreateBuilder(args);

var fileReadonlyMcp = builder.AddProject<Projects.FileReadonlyMcpServer>("file-readonly-mcp");
var fileWriteMcp    = builder.AddProject<Projects.FileWriteMcpServer>("file-write-mcp");
var azureMcp        = builder.AddProject<Projects.AzureMcpServer>("azure-mcp");
var azureDevOpsMcp  = builder.AddProject<Projects.AzureDevOpsMcpServer>("azure-devops-mcp");
var gitMcp          = builder.AddProject<Projects.GitMcpServer>("git-mcp");

builder.AddProject<Projects.Orchestrator>("orchestrator")
    .WithHttpEndpoint(name: "http")
    .WithExternalHttpEndpoints()
    .WithEnvironment("MCP_FILE_READONLY_URL", fileReadonlyMcp.GetEndpoint("http"))
    .WithEnvironment("MCP_FILE_WRITE_URL",    fileWriteMcp.GetEndpoint("http"))
    .WithEnvironment("MCP_AZURE_URL",         azureMcp.GetEndpoint("http"))
    .WithEnvironment("MCP_AZURE_DEVOPS_URL",  azureDevOpsMcp.GetEndpoint("http"))
    .WithEnvironment("MCP_GIT_URL",           gitMcp.GetEndpoint("http"))
    .WaitFor(fileReadonlyMcp)
    .WaitFor(fileWriteMcp)
    .WaitFor(azureMcp)
    .WaitFor(azureDevOpsMcp)
    .WaitFor(gitMcp);

builder.Build().Run();
