using System.Diagnostics;
using AgentShared;
using Anthropic.Models.Messages;
using Orchestrator.Models;

namespace Orchestrator.Services;

public class OrchestratorService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, TaskCompletionSource<(bool Approved, string? Reason)>> _pending = [];
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly string _solutionRoot;

    public List<OrchestratorMessage> OrchestratorMessages { get; } = [];
    public Dictionary<string, List<AgentMessage>> AgentMessages { get; } = [];
    public Dictionary<string, AgentState> AgentStates { get; } = [];
    public Dictionary<string, string?> AgentCurrentTask { get; } = [];
    public List<ApprovalRequest> PendingApprovals { get; } = [];
    public bool IsBusy { get; private set; }

    public event Action? StateChanged;
    public event Action<string>? ApprovalArrived;

    public OrchestratorService()
    {
        _solutionRoot = FindSolutionRoot();
        foreach (var agent in AgentConfig.Agents)
            AgentStates[agent.Id] = AgentState.Idle;
    }

    public async Task RunAsync(string userMessage)
    {
        if (!await _runLock.WaitAsync(0))
        {
            AddOrchMsg("Er loopt al een taak. Wacht even...", "error");
            return;
        }
        try
        {
            SetBusy(true);
            AddOrchMsg(userMessage, "user");
            await RunLoopAsync(userMessage);
        }
        catch (Exception ex)
        {
            AddOrchMsg($"Fout: {ex.Message}", "error");
        }
        finally
        {
            SetBusy(false);
            _runLock.Release();
        }
    }

    public Task Respond(string requestId, bool approved, string? reason = null)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(requestId, out var tcs))
            {
                _pending.Remove(requestId);
                PendingApprovals.RemoveAll(a => a.RequestId == requestId);
                tcs.TrySetResult((approved, reason));
            }
        }
        Notify();
        return Task.CompletedTask;
    }

    private const string SystemPrompt = """
        You coordinate specialized agents. Each agent has strictly limited capabilities:
        - delegate_to_file_reader: read files and list directories (read-only)
        - delegate_to_file_writer: write and create files (scoped output path only)
        - delegate_to_azure: query Azure resources (read-only, no modifications)
        - delegate_to_azure_devops: query Azure DevOps (read-only, no triggers)
        - delegate_to_git: clone repositories only (no push/commit)

        Always use the most restrictive agent that can complete the task.
        Break complex tasks into subtasks and delegate each to the appropriate agent.
        """;

    private async Task RunLoopAsync(string userMessage)
    {
        var client = ClaudeAuthHelper.CreateClient(verbose: false);
        var tools = AgentLoop.BuildTools(
        [
            ("delegate_to_file_reader",  "Delegate a file reading task to the FileReadonlyAgent.",
             """{"type":"object","properties":{"task":{"type":"string"}},"required":["task"]}"""),
            ("delegate_to_file_writer",  "Delegate a file writing task to the FileWriteAgent.",
             """{"type":"object","properties":{"task":{"type":"string"}},"required":["task"]}"""),
            ("delegate_to_azure",        "Delegate an Azure query to the AzureReadonlyAgent.",
             """{"type":"object","properties":{"task":{"type":"string"}},"required":["task"]}"""),
            ("delegate_to_azure_devops", "Delegate an Azure DevOps query to the AzureDevOpsReadonlyAgent.",
             """{"type":"object","properties":{"task":{"type":"string"}},"required":["task"]}"""),
            ("delegate_to_git",          "Delegate a Git task to the GitCloneAgent.",
             """{"type":"object","properties":{"task":{"type":"string"}},"required":["task"]}"""),
        ]);

        var messages = new List<MessageParam> { new() { Role = Role.User, Content = userMessage } };

        while (true)
        {
            Message response;
            try
            {
                response = await client.Messages.Create(new MessageCreateParams
                {
                    Model = ClaudeModels.Default,
                    MaxTokens = 4096,
                    System = SystemPrompt,
                    Messages = messages,
                    Tools = tools
                });
            }
            catch (Exception ex)
            {
                AddOrchMsg($"API-fout: {ex.Message}", "error");
                return;
            }

            foreach (var block in response.Content)
                if (block.TryPickText(out var t) && !string.IsNullOrWhiteSpace(t.Text))
                    AddOrchMsg(t.Text, response.StopReason == "end_turn" ? "assistant" : "thinking");

            if (response.StopReason == "end_turn") break;

            if (response.StopReason == "tool_use")
            {
                AgentLoop.AddAssistantMessage(messages, response);

                foreach (var block in response.Content)
                {
                    if (!block.TryPickToolUse(out var tu)) continue;

                    var task = tu.Input.TryGetValue("task", out var taskEl) ? taskEl.GetString() ?? string.Empty : string.Empty;
                    var agent = AgentConfig.Get(tu.Name);

                    AddOrchMsg($"→ Delegeren naar {agent.Icon} {agent.Label}: \"{task}\"", "delegation");
                    SetAgentTask(agent.Id, task);

                    if (agent.RequiresApproval)
                    {
                        var approvalResult = await RequestApprovalAsync(agent, task);
                        if (!approvalResult.Approved)
                        {
                            var denial = $"Geweigerd door gebruiker: {approvalResult.Reason ?? "Geen reden opgegeven"}";
                            AddAgentMsg(agent.Id, denial, "denied");
                            SetAgentState(agent.Id, AgentState.Error);
                            AgentLoop.AddToolResult(messages, tu.ID, denial);
                            continue;
                        }
                    }

                    SetAgentState(agent.Id, AgentState.Running);
                    var result = await RunAgentProcessAsync(agent, task);
                    SetAgentState(agent.Id, AgentState.Done);
                    AgentLoop.AddToolResult(messages, tu.ID, result);
                }
            }
        }
    }

    private async Task<(bool Approved, string? Reason)> RequestApprovalAsync(AgentInfo agent, string task)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<(bool, string?)>();
        var approval = new ApprovalRequest(requestId, agent.Id, agent.Label, agent.Icon, task, agent.ApprovalDescription);

        lock (_lock)
        {
            _pending[requestId] = tcs;
            PendingApprovals.Add(approval);
        }

        SetAgentState(agent.Id, AgentState.PendingApproval);
        Notify();
        ApprovalArrived?.Invoke(agent.Id);

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromMinutes(10));
        }
        catch (TimeoutException)
        {
            lock (_lock)
            {
                _pending.Remove(requestId);
                PendingApprovals.RemoveAll(a => a.RequestId == requestId);
            }
            return (false, "Timeout: geen reactie binnen 10 minuten.");
        }
    }

    private async Task<string> RunAgentProcessAsync(AgentInfo agent, string task)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _solutionRoot
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(agent.ProjectDir);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(agent.McpUrl);
        psi.ArgumentList.Add(task);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Kon agent {agent.Label} niet starten.");

        var outputLines = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            outputLines.Add(e.Data);
            AddAgentMsg(agent.Id, e.Data, "output");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                AddAgentMsg(agent.Id, e.Data, "log");
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        var output = string.Join("\n", outputLines);
        return string.IsNullOrWhiteSpace(output) ? "(geen uitvoer)" : output.Trim();
    }

    private void SetBusy(bool busy) { IsBusy = busy; Notify(); }

    private void AddOrchMsg(string text, string type)
    {
        lock (_lock) OrchestratorMessages.Add(new OrchestratorMessage(text, type));
        Notify();
    }

    private void AddAgentMsg(string agentId, string text, string type)
    {
        lock (_lock)
        {
            if (!AgentMessages.ContainsKey(agentId)) AgentMessages[agentId] = [];
            AgentMessages[agentId].Add(new AgentMessage(agentId, text, type));
        }
        Notify();
    }

    private void SetAgentState(string agentId, AgentState state)
    {
        lock (_lock) AgentStates[agentId] = state;
        Notify();
    }

    private void SetAgentTask(string agentId, string task)
    {
        lock (_lock)
        {
            AgentCurrentTask[agentId] = task;
            if (!AgentMessages.ContainsKey(agentId)) AgentMessages[agentId] = [];
            else AgentMessages[agentId].Clear();
        }
        Notify();
    }

    private void Notify() => StateChanged?.Invoke();

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (dir.GetFiles("*.slnx", SearchOption.TopDirectoryOnly).Length > 0
                || dir.GetFiles("*.sln", SearchOption.TopDirectoryOnly).Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
