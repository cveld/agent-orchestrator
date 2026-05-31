using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Messages;

namespace AgentShared;

public static class AgentLoop
{
    public static List<ToolUnion> BuildTools(IEnumerable<(string name, string desc, string schema)> defs) =>
        defs.Select(d => (ToolUnion)new Tool
        {
            Name = d.name,
            Description = d.desc,
            InputSchema = InputSchema.FromRawUnchecked(
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(d.schema)!)
        }).ToList();

    public static void AddAssistantMessage(List<MessageParam> messages, Message response)
    {
        var roleEl = JsonSerializer.Deserialize<JsonElement>("\"assistant\"");
        var contentEl = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(response.Content));
        messages.Add(MessageParam.FromRawUnchecked(new Dictionary<string, JsonElement>
        {
            ["role"] = roleEl,
            ["content"] = contentEl
        }));
    }

    public static void AddToolResult(List<MessageParam> messages, string toolUseId, string result) =>
        messages.Add(new MessageParam
        {
            Role = Role.User,
            Content = new List<ContentBlockParam>
            {
                new(new ToolResultBlockParam { ToolUseID = toolUseId, Content = result })
            }
        });

    public static async Task<string> CallMcpTool(HttpClient http, string baseUrl, string toolName, IReadOnlyDictionary<string, JsonElement> input)
    {
        // Convert input dictionary to a JsonObject for the HTTP call
        var body = new JsonObject();
        foreach (var (k, v) in input)
            body[k] = JsonNode.Parse(v.GetRawText());

        var response = await http.PostAsJsonAsync($"{baseUrl}/tools/{toolName}", body);
        if (!response.IsSuccessStatusCode)
            return $"Error {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}";
        return await response.Content.ReadAsStringAsync();
    }
}
