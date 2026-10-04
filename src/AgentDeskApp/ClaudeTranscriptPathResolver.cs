using System.IO;

namespace AgentDeskApp;

/// <summary>
/// セッションの作業フォルダ(cwd)とセッションIDから、Claude Codeの会話ログファイル(.jsonl)の
/// 絶対パスを組み立てるクラス(design.md §6参照、非公式の観察に基づく)。
/// </summary>
public static class ClaudeTranscriptPathResolver
{
    /// <summary>
    /// 会話ログファイルの絶対パスを返す(<c>~/.claude/projects/&lt;エンコードされたcwd&gt;/&lt;セッションID&gt;.jsonl</c>)。
    /// フォルダ名のエンコード規則(実機観察): cwd文字列中の"\"と":"をそのまま"-"に置換するだけ。
    /// 大文字/小文字はcwdに書かれている通りをそのまま使う(セッションごとに揺れがあるため正規化しない)。
    /// </summary>
    /// <param name="cwd">セッションの作業フォルダ(agents --jsonのcwdフィールド)。</param>
    /// <param name="sessionId">フルセッションID。</param>
    public static string Resolve(string cwd, string sessionId)
    {
        var encoded = cwd.Replace('\\', '-').Replace(':', '-').Replace('/', '-');
        var claudeHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        return Path.Combine(claudeHome, encoded, $"{sessionId}.jsonl");
    }
}
