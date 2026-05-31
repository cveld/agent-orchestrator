namespace AgentShared;

public static class ClaudeModels
{
    // Claude 4 — requires ANTHROPIC_API_KEY or a higher subscription tier
    public const string Sonnet4 = "claude-sonnet-4-6";
    public const string Opus4 = "claude-opus-4-8";

    // Claude Haiku 4.5 — works with Claude Code OAuth subscription AND API keys
    public const string Haiku45 = "claude-haiku-4-5-20251001";

    /// <summary>
    /// Default model. Haiku 4.5 is available with both the Claude subscription
    /// OAuth token (~/.claude/.credentials.json) and developer API keys.
    /// Set ANTHROPIC_API_KEY and use Sonnet4/Opus4 for more capable models.
    /// </summary>
    public const string Default = Haiku45;
}
