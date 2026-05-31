# Approval Flow (Human-in-the-Loop)

Write operations require explicit user approval before the corresponding agent is started. This applies to `FileWriteAgent` and `GitCloneAgent`.

## Sequence diagram

```
OrchestratorService          Browser (Blazor UI)
        │                            │
        │── Claude picks tool ──────▶│
        │   (delegate_to_file_writer)│
        │                            │
        │── ApprovalRequest event ──▶│
        │   + TCS registered         │── tab auto-switches to agent
        │                            │── approval card rendered
        │                            │
        │◀── Respond(approved=true) ─│   user clicks Approve
        │    TCS.SetResult(true)     │
        │                            │
        │── spawns FileWriteAgent ──▶│
        │                            │── stdout lines stream live
        │── AgentCompleted event ───▶│
        │                            │
```

## Implementation

```csharp
// OrchestratorService.cs — request side
private async Task<(bool Approved, string? Reason)> RequestApprovalAsync(AgentInfo agent, string task)
{
    var requestId = Guid.NewGuid().ToString("N");
    var tcs = new TaskCompletionSource<(bool, string?)>();

    lock (_lock)
    {
        _pending[requestId] = tcs;
        PendingApprovals.Add(new ApprovalRequest(requestId, agent.Id, ...));
    }

    SetAgentState(agent.Id, AgentState.PendingApproval);
    Notify();                          // → StateChanged → UI re-renders
    ApprovalArrived?.Invoke(agent.Id); // → UI switches to this agent's tab

    // Non-blocking wait; the async state machine suspends here
    return await tcs.Task.WaitAsync(TimeSpan.FromMinutes(10));
}

// OrchestratorService.cs — response side
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
```

## Approval card UI elements

| Element | Purpose |
|---------|---------|
| Agent name + icon | Identifies which agent is requesting permission |
| Description | Plain-language explanation of the operation type |
| Task box (monospace) | Exact task string passed by the orchestrator |
| Denial reason input | Optional; forwarded to Claude as context |
| Approve button | Green; triggers `Respond(approved: true)` |
| Deny button | Red outline; triggers `Respond(approved: false, reason: ...)` |

## On denial

The orchestrator loop receives the denial string as the tool result:

```
"Denied by user: <reason or 'No reason given'>"
```

Claude then decides how to proceed: find an alternative, ask for clarification, or abort the task.

## Timeout

After 10 minutes with no response the approval is automatically denied with reason `"Timeout: no response within 10 minutes."` The agent is never started.

## Potential extensions

- **Per-tool approval** within an agent (requires agent→orchestrator HTTP callback)
- **Role-based approval** — certain users can only view, others can approve
- **Audit log** — persist all approvals/denials with timestamps and task context
- **Pre-filled descriptions** — let Claude generate a human-readable summary of what it intends to do before triggering the approval
