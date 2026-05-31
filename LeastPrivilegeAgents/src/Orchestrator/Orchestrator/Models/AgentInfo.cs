namespace Orchestrator.Models;

public record AgentInfo(
    string Id,
    string Label,
    string Icon,
    string ProjectDir,
    string McpUrl,
    bool RequiresApproval,
    string ApprovalDescription);

public static class AgentConfig
{
    public static AgentInfo[] Agents =>
    [
        new("delegate_to_file_reader",  "File Reader",  "📂",
            "src/Agents/FileReadonlyAgent/FileReadonlyAgent",
            Environment.GetEnvironmentVariable("MCP_FILE_READONLY_URL") ?? "http://localhost:5101",
            false, ""),
        new("delegate_to_file_writer",  "File Writer",  "✏️",
            "src/Agents/FileWriteAgent/FileWriteAgent",
            Environment.GetEnvironmentVariable("MCP_FILE_WRITE_URL") ?? "http://localhost:5102",
            true, "Dit agent wil bestanden aanmaken of bewerken op het lokale bestandssysteem."),
        new("delegate_to_azure",        "Azure",        "☁️",
            "src/Agents/AzureReadonlyAgent/AzureReadonlyAgent",
            Environment.GetEnvironmentVariable("MCP_AZURE_URL") ?? "http://localhost:5103",
            false, ""),
        new("delegate_to_azure_devops", "Azure DevOps", "🔧",
            "src/Agents/AzureDevOpsReadonlyAgent/AzureDevOpsReadonlyAgent",
            Environment.GetEnvironmentVariable("MCP_AZURE_DEVOPS_URL") ?? "http://localhost:5104",
            false, ""),
        new("delegate_to_git",          "Git",          "🔀",
            "src/Agents/GitCloneAgent/GitCloneAgent",
            Environment.GetEnvironmentVariable("MCP_GIT_URL") ?? "http://localhost:5105",
            true, "Dit agent wil een Git-repository klonen naar het lokale bestandssysteem."),
    ];

    public static AgentInfo Get(string id) => Agents.First(a => a.Id == id);
}
