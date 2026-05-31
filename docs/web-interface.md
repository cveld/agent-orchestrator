# Web Interface

The orchestrator exposes a Blazor Server web app at `http://localhost:5000`. Blazor Server uses a persistent SignalR connection per browser tab; all rendering happens server-side, so the UI reflects live service state without manual SignalR wiring.

## Layout

```
┌─────────────────────────────────────────────────────────┐
│ ● Least Privilege Agent Framework              Idle      │  ← header
├──────────────────────────────────────────────────────────┤
│ [🎯 Orchestrator] [📂 File Reader ●] [✏️ File Writer ⚠] │  ← tab bar
├──────────────────────────────────────────────────────────┤
│                                                          │
│   ┌──────────────────────────────────────────────────┐  │
│   │ ⚠️ Approval required                             │  │
│   │ ✏️ File Writer                                   │  │
│   │ This agent wants to create/modify files on disk. │  │
│   │ ┌──────────────────────────────────────────────┐ │  │
│   │ │ Write a summary report to output/report.txt  │ │  │
│   │ └──────────────────────────────────────────────┘ │  │
│   │ [Denial reason (optional)] [❌ Deny] [✅ Approve] │  │
│   └──────────────────────────────────────────────────┘  │
│                                                          │
│  [auth] Using OAuth token from ~/.claude/.credentials   │
│  Task received; writing report...                        │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

## Tabs

### Orchestrator tab

| Element | Description |
|---------|-------------|
| Chat bubbles (right, blue) | User messages |
| Chat bubbles (left, dark) | Claude's final answers |
| Italic bubbles with blue bar | Claude's thinking text during tool selection |
| Delegation lines | `→ Delegating to ✏️ File Writer: "..."` |
| Textarea + Send button | New task input; Enter sends, Shift+Enter inserts newline |

### Agent tabs

| Element | Description |
|---------|-------------|
| Task banner | Current task string at the top |
| Approval card | Yellow-bordered card when approval is pending |
| Green monospace lines | Agent stdout (final answers, progress) |
| Grey monospace lines | Agent stderr (auth messages, debug logs) |
| Red lines | Denial messages |

### Tab badges

- **Number badge (red):** unread message count for a non-active tab
- **`!` badge (yellow):** pending approval — tab also auto-activates when an approval arrives

## State management

`OrchestratorService` is a singleton that holds all observable state. `Home.razor` subscribes to two events:

```csharp
Svc.StateChanged += OnStateChanged;   // any mutation → StateHasChanged()
Svc.ApprovalArrived += OnApprovalArrived; // switches active tab
```

State is mutated exclusively inside `OrchestratorService`, always under `lock(_lock)`, always followed by `Notify()`. The Blazor circuit thread reads state only after `InvokeAsync(StateHasChanged)` triggers a re-render.

## Auto-scroll

After each re-render, `OnAfterRenderAsync` calls a small JS helper:

```js
window.scrollToBottom = (id) => {
    const el = document.getElementById(id);
    if (el) el.scrollTop = el.scrollHeight;
};
```

This keeps the active message list scrolled to the bottom as output streams in.

## Design system

Colors follow GitHub's dark theme palette:

| Token | Hex | Usage |
|-------|-----|-------|
| Background | `#0d1117` | Page, agent message area |
| Surface | `#161b22` | Header, tab bar, input area |
| Border | `#30363d` | Dividers, input borders |
| Text muted | `#8b949e` | Placeholder, log lines, labels |
| Blue | `#1f6feb` / `#388bfd` | User bubbles, active tab indicator, focus rings |
| Green | `#238636` / `#3fb950` | Approve button, agent stdout, done state dot |
| Yellow | `#d29922` | Approval card border, pending state dot, badge |
| Red | `#f85149` | Deny button, error state, unread badge |

## File structure

```
Components/
├── App.razor                — HTML shell, loads app.css + scripts.js + blazor.server.js
├── Routes.razor             — Router component
├── _Imports.razor           — Global @using directives and @namespace
├── Layout/MainLayout.razor  — Minimal layout (full-height <main>)
└── Pages/Home.razor         — All UI logic: tabs, chat, agent panels, approval cards

wwwroot/
├── app.css                  — GitHub dark theme, all component styles
└── scripts.js               — scrollToBottom helper
```
