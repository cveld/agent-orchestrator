namespace Orchestrator.Models;

public enum AgentState { Idle, Running, Done, Error, PendingApproval }

// Type values: user | assistant | thinking | error | delegation
public record OrchestratorMessage(string Text, string Type);

// Type values: output | log | denied
public record AgentMessage(string AgentId, string Text, string Type);

public record ApprovalRequest(
    string RequestId,
    string AgentId,
    string Label,
    string Icon,
    string Task,
    string Description);
