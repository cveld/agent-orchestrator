# Architecture

## System overview

```
                  ┌─────────────────────────────────────┐
                  │   Aspire AppHost (LeastPrivilegeAgents.AppHost)  │
                  │   Manages lifecycle + injects MCP_*_URL env vars │
                  └──────────┬──────────────────────────┘
                             │ spawns & monitors
          ┌──────────────────┼──────────────────────┐
          ▼                  ▼                      ▼
  MCP:5101–5105      Orchestrator (Blazor)    Aspire dashboard
  (fixed ports)      (dynamic port)

User (browser)
      │  HTTP / SignalR (Blazor circuit)
      ▼
┌──────────────────────────────────────────┐
│          Orchestrator (Blazor Server)    │
│  ┌───────────────────────────────────┐   │
│  │  OrchestratorService              │   │
│  │  - Claude API message loop        │   │
│  │  - Approval gate (TCS)            │   │
│  │  - StateChanged event → UI        │   │
│  └───────────────────────────────────┘   │
└─────────────┬────────────────────────────┘
              │  ProcessStartInfo (dotnet run)
    ┌─────────┼─────────────────────────┐
    │  stdout / stderr stream           │
    ▼         ▼          ▼             ▼
FileReader  FileWriter  AzureRO   GitClone
  Agent      Agent      Agent      Agent
    │           │           │          │
    │  HTTP POST /tools/*   │          │
    ▼           ▼           ▼          ▼
 MCP:5101   MCP:5102   MCP:5103   MCP:5105
```

## Layers

### 0. Aspire AppHost (`LeastPrivilegeAgents.AppHost`)

Manages the lifecycle of the Orchestrator and all five MCP servers. Injects MCP URLs into the Orchestrator via `MCP_*_URL` environment variables so port changes don't require code edits. The Orchestrator's port is fully managed by Aspire (dynamic); MCP server ports are fixed at 5101–5105 via each server's `launchSettings.json`.

Agents are **not** managed by Aspire — they are ephemeral console apps spawned on demand by `OrchestratorService`.

### 1. Web UI — Blazor Server (`Home.razor`)
- Single page with tabs per agent + orchestrator chat
- Connected via Blazor's built-in SignalR circuit (no manual SignalR setup needed)
- `OrchestratorService` is a singleton; the page subscribes to `StateChanged`
- `ApprovalArrived` event automatically switches to the relevant agent tab

### 2. Orchestration layer — `OrchestratorService`
- Manages the Claude message loop (`while stop_reason == tool_use`)
- Spawns agents as child processes via `ProcessStartInfo`
- Intercepts delegations to write agents and waits for user approval (`TaskCompletionSource`)
- Streams agent stdout/stderr to the UI in real time via events

### 3. Agents (console apps)
- Each is a standalone .NET console app
- Receive MCP URL + task description as command-line arguments
- Run their own Claude message loop with a small, fixed tool allowlist
- Write final result to stdout; intermediate logs to stderr

### 4. MCP Servers (minimal ASP.NET Core apps)
- One server per agent type, bound to `localhost` only
- Expose tools as HTTP POST endpoints (`/tools/{toolName}`)
- Perform security checks themselves (path traversal, scope enforcement)

## Data flow for a single task

```
1. User types task in chat (browser)
2. Blazor calls OrchestratorService.RunAsync()
3. Service sends task to Claude API
4. Claude selects a delegation tool (e.g. delegate_to_file_writer)
5. Service emits ApprovalRequest event (for write agents)
6. User approves → Service spawns FileWriteAgent child process
7. Agent runs its own Claude loop, calls MCP:5102 for each tool use
8. stdout lines stream to the UI via ProcessOutputDataReceived
9. Agent exits → result fed back into the orchestrator loop as a tool result
10. Claude processes the result and produces a final answer
```

## Concurrency model

| Mechanism | Purpose |
|-----------|---------|
| `SemaphoreSlim(1,1)` | Prevents concurrent orchestration runs |
| `lock(_lock)` | Protects shared lists (messages, approvals) from race conditions |
| `InvokeAsync(StateHasChanged)` | Thread-safe UI updates from background threads |
| `TaskCompletionSource` | Non-blocking wait during approval; the orchestrator loop suspends without blocking a thread pool thread |
| `WaitAsync(TimeSpan)` | 10-minute approval timeout; auto-denies on expiry |
