# Least Privilege Agent Framework

Multi-agent system in .NET 10 where each agent has only the minimum permissions needed for its task. The orchestrator runs as a Blazor Server web app.

## Running

```bash
# All services at once (preferred)
cd LeastPrivilegeAgents
aspire start
```

Dashboard URL with login token is printed on startup.

```bash
# Manual (without Aspire) — start each in a separate terminal
cd LeastPrivilegeAgents
dotnet run --project src/Orchestrator/Orchestrator          # web UI on :5000
dotnet run --project src/McpServers/FileReadonlyMcpServer/FileReadonlyMcpServer
dotnet run --project src/McpServers/FileWriteMcpServer/FileWriteMcpServer
dotnet run --project src/McpServers/AzureMcpServer/AzureMcpServer
dotnet run --project src/McpServers/AzureDevOpsMcpServer/AzureDevOpsMcpServer
dotnet run --project src/McpServers/GitMcpServer/GitMcpServer
```

## Tech stack

- **Runtime:** .NET 10, C# 12
- **Web UI:** Blazor Server (real-time via SignalR)
- **AI:** Anthropic SDK (`Anthropic` NuGet), auth via OAuth token or `ANTHROPIC_API_KEY`
- **MCP servers:** Minimal ASP.NET Core HTTP servers, one per agent type

## Project layout

```
LeastPrivilegeAgents/
├── LeastPrivilegeAgents.AppHost/        # Aspire AppHost (entry point)
├── src/
│   ├── Orchestrator/Orchestrator/       # Blazor Server (port managed by Aspire)
│   │   ├── Components/Pages/Home.razor  # Main UI: tabs + approval cards
│   │   ├── Services/OrchestratorService.cs  # Agent loop, approval mechanism
│   │   └── Models/                      # AgentInfo, Messages
│   ├── Agents/                          # 5 specialised agents (console apps, spawned on demand)
│   ├── McpServers/                      # 5 HTTP tool servers (ports 5101–5105)
│   └── Shared/AgentShared/              # AgentLoop, ClaudeAuthHelper, ClaudeModels
└── config/
    └── credentials.json.example
```

## Agents & ports

| Agent | Tool name | MCP port | Approval required |
|-------|-----------|----------|------------------|
| FileReadonlyAgent | `delegate_to_file_reader` | 5101 | no |
| FileWriteAgent | `delegate_to_file_writer` | 5102 | **yes** |
| AzureReadonlyAgent | `delegate_to_azure` | 5103 | no |
| AzureDevOpsReadonlyAgent | `delegate_to_azure_devops` | 5104 | no |
| GitCloneAgent | `delegate_to_git` | 5105 | **yes** |

MCP ports are fixed in each server's `launchSettings.json`. The Orchestrator port is dynamic (assigned by Aspire). MCP URLs are injected into the Orchestrator via `MCP_*_URL` environment variables; agent code falls back to the hardcoded port defaults when running without Aspire.

## Authentication

`ClaudeAuthHelper` tries in order: `CLAUDE_SETUP_TOKEN` → `~/.claude/.credentials.json` (OAuth) → `ANTHROPIC_API_KEY`.

## Design documents

See [docs/](docs/) for architecture, security model, approval flow, and UI design.
