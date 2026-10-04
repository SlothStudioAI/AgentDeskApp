using System.IO;

namespace AgentDeskApp;

/// <summary>
/// CLAUDE.mdの「## リーダー」セクションから、部長/リーダーの名前・役割を読み取るクラス。
/// 「## グループ」セクション(<see cref="GroupRegistry"/>)と同じく、専用の設定ファイルは持たず
/// CLAUDE.md自体を台帳として使う。書式は「- 名前: 値」「- 役割: 値」の箇条書き。
/// </summary>
public static class LeaderInfoReader
{
    private const string SectionHeading = "## リーダー";

    /// <summary>
    /// 指定したCLAUDE.mdから「## リーダー」セクションの名前・役割を読み取る。
    /// ファイルが無い、またはセクション・項目が無い場合は該当フィールドがnullの<see cref="LeaderInfo"/>を返す。
    /// </summary>
    public static LeaderInfo Read(string claudeMdPath)
    {
        if (!File.Exists(claudeMdPath))
        {
            return new LeaderInfo(null, null);
        }

        string? name = null;
        string? role = null;
        var inSection = false;

        foreach (var line in File.ReadLines(claudeMdPath))
        {
            var trimmed = line.Trim();

            if (trimmed == SectionHeading)
            {
                inSection = true;
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                break;
            }

            if (TryExtractValue(trimmed, "名前", out var extractedName))
            {
                name = extractedName;
            }
            else if (TryExtractValue(trimmed, "役割", out var extractedRole))
            {
                role = extractedRole;
            }
        }

        return new LeaderInfo(name, role);
    }

    /// <summary>「- 名前: 値」形式の行から、指定したキーの値を取り出す(全角コロン「：」も許容)。</summary>
    private static bool TryExtractValue(string bulletLine, string key, out string value)
    {
        value = string.Empty;

        if (!bulletLine.StartsWith("- ", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = bulletLine[2..].TrimStart();
        if (!rest.StartsWith(key, StringComparison.Ordinal))
        {
            return false;
        }

        rest = rest[key.Length..].TrimStart();
        if (rest.Length == 0 || (rest[0] != ':' && rest[0] != '：'))
        {
            return false;
        }

        value = rest[1..].Trim();
        return value.Length > 0;
    }
}
