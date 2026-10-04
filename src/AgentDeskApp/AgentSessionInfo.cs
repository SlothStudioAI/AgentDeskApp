using System.Text.Json;

namespace AgentDeskApp;

/// <summary>
/// <c>claude agents --json</c>が返す1セッション分の情報。design.md §6参照。
/// </summary>
/// <param name="SessionId">フルセッションID(UUID)。会話ログファイル名にそのまま使われる。</param>
/// <param name="Cwd">そのセッションを起動したときの作業フォルダ。会話ログの保存先フォルダ名の元にもなる。</param>
/// <param name="Kind">"interactive"(対話中のセッション)または"background"(claude --bgで起動したセッション)。</param>
/// <param name="Status">対話セッションの状態("idle"/"busy"等)。backgroundセッションには無いことが多い。</param>
/// <param name="State">backgroundセッションの状態("blocked"/"stopped"等)。対話セッションには無いことが多い。</param>
/// <param name="Name">セッションの表示名(あれば)。</param>
public sealed record AgentSessionInfo(
    string SessionId,
    string Cwd,
    string Kind,
    string? Status,
    string? State,
    string? Name)
{
    /// <summary>
    /// このセッションが「busy(何か処理中)」かどうか。
    /// 対話セッション(kind=="interactive")のstatusフィールドのみで判定する
    /// (backgroundセッションはAgentDeskAppの監視対象外、design.md §6参照)。
    /// </summary>
    public bool IsBusy => Kind == "interactive" && string.Equals(Status, "busy", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// <c>claude agents --json</c>の出力を実行・解析するクラス。
/// </summary>
public static class AgentsJsonClient
{
    /// <summary>
    /// 与えられたJSON文字列(<c>claude agents --json</c>の標準出力)をパースする。
    /// 実行そのもの(プロセス起動)はテスト容易性のため呼び出し元(MemberActivityMonitor)に分離している。
    /// </summary>
    /// <param name="json">パースするJSON配列文字列。</param>
    public static IReadOnlyList<AgentSessionInfo> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<AgentSessionInfo>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (!element.TryGetProperty("sessionId", out var sessionIdProp) ||
                    !element.TryGetProperty("cwd", out var cwdProp) ||
                    !element.TryGetProperty("kind", out var kindProp))
                {
                    continue;
                }

                var status = element.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
                var state = element.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
                var name = element.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;

                results.Add(new AgentSessionInfo(
                    sessionIdProp.GetString()!,
                    cwdProp.GetString()!,
                    kindProp.GetString()!,
                    status,
                    state,
                    name));
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
