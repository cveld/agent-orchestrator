using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Credentials;

namespace AgentShared;

public static class ClaudeAuthHelper
{
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    /// <summary>
    /// Creates an AnthropicClient (official Anthropic SDK) using (priority order):
    /// 1. CLAUDE_SETUP_TOKEN env var — output from `claude setup-token`
    /// 2. OAuth token from ~/.claude/.credentials.json (claudeAiOauth.accessToken)
    ///    NOTE: team subscription tokens give access to Haiku 4.5 via direct API.
    ///    For Sonnet/Opus via direct API, use ANTHROPIC_API_KEY instead.
    /// 3. ANTHROPIC_API_KEY env var — developer key from console.anthropic.com
    /// </summary>
    public static AnthropicClient CreateClient(bool verbose = true)
    {
        var setupToken = Environment.GetEnvironmentVariable("CLAUDE_SETUP_TOKEN");
        if (setupToken is not null)
        {
            if (verbose) Console.Error.WriteLine("[auth] Using CLAUDE_SETUP_TOKEN");
            return BuildClient(setupToken);
        }

        var oauthToken = TryReadOAuthToken(verbose);
        if (oauthToken is not null)
        {
            if (verbose) Console.Error.WriteLine("[auth] Using OAuth token from ~/.claude/.credentials.json");
            return BuildClient(oauthToken);
        }

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (apiKey is not null)
        {
            if (verbose) Console.Error.WriteLine("[auth] Using ANTHROPIC_API_KEY");
            return new AnthropicClient(new ClientOptions { AuthToken = apiKey });
        }

        throw new InvalidOperationException(
            "No authentication found. Options:\n" +
            "  1. Run `claude` to ensure ~/.claude/.credentials.json is fresh\n" +
            "  2. Set CLAUDE_SETUP_TOKEN=<token> after running `claude setup-token`\n" +
            "  3. Set ANTHROPIC_API_KEY=<key> (from console.anthropic.com, enables all models)");
    }

    private static AnthropicClient BuildClient(string token)
    {
        // StaticTokenCredentials: SDK sets Authorization: Bearer header and
        // correct anthropic-beta headers automatically
        var creds = new StaticTokenCredentials(token);
        return new AnthropicClient(new ClientOptions { Credentials = creds });
    }

    private static string? TryReadOAuthToken(bool verbose)
    {
        if (!File.Exists(CredentialsPath))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsPath));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
                return null;
            if (!oauth.TryGetProperty("accessToken", out var tokenEl))
                return null;

            var token = tokenEl.GetString();
            if (string.IsNullOrEmpty(token))
                return null;

            if (oauth.TryGetProperty("expiresAt", out var expiresEl))
            {
                var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresEl.GetInt64());
                if (expiresAt < DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    if (verbose)
                        Console.Error.WriteLine("[auth] Warning: OAuth token expired. Start Claude to refresh.");
                    return null;
                }
            }

            return token;
        }
        catch (Exception ex)
        {
            if (verbose)
                Console.Error.WriteLine($"[auth] Warning: could not read credentials: {ex.Message}");
            return null;
        }
    }
}
