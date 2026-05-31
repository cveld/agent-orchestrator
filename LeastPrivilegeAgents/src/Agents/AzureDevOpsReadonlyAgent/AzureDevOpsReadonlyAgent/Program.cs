using AgentShared;
using Anthropic.Models.Messages;

var mcpBaseUrl = args.Length > 0 && args[0].StartsWith("http") ? args[0] : "http://localhost:5104";
var task = args.Length > 1 ? string.Join(" ", args[1..]) : ReadTaskFromStdin();

using var http = new HttpClient();
var client = ClaudeAuthHelper.CreateClient();

var tools = AgentLoop.BuildTools(
[
    ("list_projects", "List all Azure DevOps projects in the configured organization.",
     """{"type":"object","properties":{}}"""),
    ("list_repositories", "List all Git repositories in an Azure DevOps project.",
     """{"type":"object","properties":{"projectName":{"type":"string","description":"ADO project name"}},"required":["projectName"]}"""),
    ("get_repository", "Get details of a specific Git repository.",
     """{"type":"object","properties":{"projectName":{"type":"string"},"repositoryName":{"type":"string"}},"required":["projectName","repositoryName"]}"""),
    ("list_pipelines", "List all build pipelines in a project.",
     """{"type":"object","properties":{"projectName":{"type":"string"}},"required":["projectName"]}"""),
    ("get_pipeline_runs", "Get recent runs of a specific pipeline.",
     """{"type":"object","properties":{"projectName":{"type":"string"},"pipelineId":{"type":"integer"},"top":{"type":"integer"}},"required":["projectName","pipelineId"]}"""),
    ("list_work_items", "List work items in a project.",
     """{"type":"object","properties":{"projectName":{"type":"string"},"top":{"type":"integer"}},"required":["projectName"]}""")
]);

var messages = new List<MessageParam> { new() { Role = Role.User, Content = task } };

const string SystemPrompt = """
    You are a read-only Azure DevOps assistant. You may ONLY query projects, repos,
    pipelines, and work items. You cannot create, trigger, or modify anything. Refuse if asked.
    """;

while (true)
{
    Message response;
    try { response = await client.Messages.Create(new MessageCreateParams { Model = ClaudeModels.Default, MaxTokens = 4096, System = SystemPrompt, Messages = messages, Tools = tools }); }
    catch (Exception ex) { Console.Error.WriteLine($"API error: {ex.Message}"); return; }

    if (response.StopReason == "end_turn")
    {
        foreach (var b in response.Content)
            if (b.TryPickText(out var t)) Console.WriteLine(t.Text);
        break;
    }

    if (response.StopReason == "tool_use")
    {
        AgentLoop.AddAssistantMessage(messages, response);
        foreach (var b in response.Content)
            if (b.TryPickToolUse(out var tu))
                AgentLoop.AddToolResult(messages, tu.ID, await AgentLoop.CallMcpTool(http, mcpBaseUrl, tu.Name, tu.Input));
    }
}

static string ReadTaskFromStdin() { Console.Write("Task: "); return Console.ReadLine() ?? string.Empty; }
