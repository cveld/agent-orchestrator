using AgentShared;
using Anthropic.Models.Messages;

var mcpBaseUrl = args.Length > 0 && args[0].StartsWith("http") ? args[0] : "http://localhost:5103";
var task = args.Length > 1 ? string.Join(" ", args[1..]) : ReadTaskFromStdin();

using var http = new HttpClient();
var client = ClaudeAuthHelper.CreateClient();

var tools = AgentLoop.BuildTools(
[
    ("list_subscriptions", "List all accessible Azure subscriptions.",
     """{"type":"object","properties":{}}"""),
    ("list_resource_groups", "List resource groups in a subscription.",
     """{"type":"object","properties":{"subscriptionId":{"type":"string","description":"Azure subscription ID"}},"required":["subscriptionId"]}"""),
    ("list_resources", "List resources in a resource group.",
     """{"type":"object","properties":{"subscriptionId":{"type":"string"},"resourceGroupName":{"type":"string"}},"required":["subscriptionId","resourceGroupName"]}"""),
    ("get_resource", "Get details of a specific Azure resource by its full resource ID.",
     """{"type":"object","properties":{"resourceId":{"type":"string","description":"Full Azure resource ID"}},"required":["resourceId"]}""")
]);

var messages = new List<MessageParam> { new() { Role = Role.User, Content = task } };

const string SystemPrompt = """
    You are a read-only Azure resource assistant. You may ONLY query Azure resources.
    You cannot create, modify, or delete anything. Refuse if asked.
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
