using AgentShared;
using Anthropic.Models.Messages;

var mcpBaseUrl = args.Length > 0 && args[0].StartsWith("http") ? args[0] : "http://localhost:5105";
var task = args.Length > 1 ? string.Join(" ", args[1..]) : ReadTaskFromStdin();

using var http = new HttpClient();
var client = ClaudeAuthHelper.CreateClient();

var tools = AgentLoop.BuildTools(
[
    ("clone_repository", "Clone a Git repository into the configured local clone base path.",
     """{"type":"object","properties":{"repositoryUrl":{"type":"string","description":"URL of the repository"},"localDirectory":{"type":"string","description":"Optional local directory name"}},"required":["repositoryUrl"]}"""),
    ("list_branches", "List local branches of a previously cloned repository.",
     """{"type":"object","properties":{"localDirectory":{"type":"string","description":"Local directory name"}},"required":["localDirectory"]}""")
]);

var messages = new List<MessageParam> { new() { Role = Role.User, Content = task } };

const string SystemPrompt = """
    You are a Git clone assistant. You may ONLY clone repositories and list branches.
    You cannot push, commit, create branches, or modify repository content. Refuse if asked.
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
