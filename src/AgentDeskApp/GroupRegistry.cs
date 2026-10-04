using System.IO;

namespace AgentDeskApp;

/// <summary>
/// グループ(ユーザーが登録した作業ルート)の一覧を、部のグローバルCLAUDE.md内の
/// 「## グループ」セクションに保持するための読み書きクラス(design.md §3/§5.4参照)。
/// 他の設定ファイルは持たず、CLAUDE.md自体を台帳として使う。
/// </summary>
public static class GroupRegistry
{
    private const string SectionHeading = "## グループ";

    /// <summary>
    /// 指定したCLAUDE.mdから「## グループ」セクション配下の箇条書き(- パス)を読み取る。
    /// 見出しは「## グループ」と(前後の空白を除いて)完全一致する行のみを対象とし、「## グループ会社の方針」等は対象外。
    /// ファイルが無い、またはセクションが無い場合は空の一覧を返す。
    /// </summary>
    /// <param name="claudeMdPath">部のグローバルCLAUDE.mdの絶対パス。</param>
    public static IReadOnlyList<string> LoadGroups(string claudeMdPath)
    {
        if (!File.Exists(claudeMdPath))
        {
            return [];
        }

        var lines = File.ReadAllLines(claudeMdPath);
        var groups = new List<string>();
        var inSection = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed == SectionHeading)
            {
                inSection = true;
                continue;
            }

            if (inSection)
            {
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    var path = trimmed[2..].Trim();
                    if (path.Length > 0)
                    {
                        groups.Add(path);
                    }
                }
                else if (trimmed.StartsWith('#') || trimmed.Length == 0)
                {
                    // 次の見出し、または空行が続いたらセクション終了とみなす。
                    if (trimmed.StartsWith('#'))
                    {
                        break;
                    }
                }
                else
                {
                    // 箇条書きでも空行でも見出しでもない行が来たらセクション終了。
                    break;
                }
            }
        }

        return groups;
    }

    /// <summary>
    /// グループ一覧を指定したCLAUDE.mdの「## グループ」セクションに書き込む(無ければ末尾に新設)。
    /// 既存のセクションがある場合はその中身だけを置き換え、それ以外の内容は変更しない。
    /// </summary>
    /// <param name="claudeMdPath">部のグローバルCLAUDE.mdの絶対パス。</param>
    /// <param name="groups">保存するグループのパス一覧。</param>
    public static void SaveGroups(string claudeMdPath, IReadOnlyList<string> groups)
    {
        var existingLines = File.Exists(claudeMdPath)
            ? File.ReadAllLines(claudeMdPath).ToList()
            : [];

        var sectionStart = existingLines.FindIndex(l =>
        {
            var t = l.Trim();
            return t == SectionHeading;
        });

        var newSectionLines = new List<string> { SectionHeading };
        foreach (var group in groups)
        {
            newSectionLines.Add($"- {group}");
        }

        if (sectionStart < 0)
        {
            if (existingLines.Count > 0 && existingLines[^1].Trim().Length != 0)
            {
                existingLines.Add(string.Empty);
            }

            existingLines.AddRange(newSectionLines);
        }
        else
        {
            var sectionEnd = sectionStart + 1;
            while (sectionEnd < existingLines.Count)
            {
                var trimmed = existingLines[sectionEnd].Trim();
                if (trimmed.StartsWith('#'))
                {
                    break;
                }

                if (trimmed.Length != 0 && !trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    break;
                }

                sectionEnd++;
            }

            existingLines.RemoveRange(sectionStart, sectionEnd - sectionStart);
            existingLines.InsertRange(sectionStart, newSectionLines);
        }

        var directory = Path.GetDirectoryName(claudeMdPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        AtomicFile.WriteAllLines(claudeMdPath, existingLines);
    }

    /// <summary>
    /// 指定したCLAUDE.mdの「## グループ」セクションから、特定のグループパスを除外して保存する。
    /// </summary>
    public static void RemoveGroup(string claudeMdPath, string groupPath)
    {
        var groups = LoadGroups(claudeMdPath).ToList();
        var normalizedTarget = groupPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        groups.RemoveAll(g => string.Equals(
            g.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            normalizedTarget,
            StringComparison.OrdinalIgnoreCase));
        SaveGroups(claudeMdPath, groups);
    }

    /// <summary>
    /// ワークスペース一覧を取得する。settings.json を正本(SSOT)として読み込み、
    /// 未設定の場合は CLAUDE.md から自動移行する。
    /// </summary>
    public static IReadOnlyList<string> LoadWorkspaces(string? claudeMdPath = null, string? settingsPath = null)
    {
        var settings = settingsPath != null ? AppSettingsLoader.Load(settingsPath) : AppSettingsLoader.Load();
        if (settings.Workspaces != null && settings.Workspaces.Count > 0)
        {
            return settings.Workspaces;
        }

        if (!string.IsNullOrEmpty(claudeMdPath) && File.Exists(claudeMdPath))
        {
            var fromMd = LoadGroups(claudeMdPath);
            if (fromMd.Count > 0)
            {
                var updated = settings with { Workspaces = fromMd.ToList() };
                if (settingsPath != null)
                {
                    AppSettingsLoader.Save(settingsPath, updated);
                }
                else
                {
                    AppSettingsLoader.Save(updated);
                }
                return fromMd;
            }
        }

        return [];
    }

    /// <summary>
    /// ワークスペース一覧を settings.json に保存し、必要に応じて CLAUDE.md / GEMINI.md にも同期する。
    /// </summary>
    public static void SaveWorkspaces(IReadOnlyList<string> workspaces, string? claudeMdPath = null, string? settingsPath = null)
    {
        var settings = settingsPath != null ? AppSettingsLoader.Load(settingsPath) : AppSettingsLoader.Load();
        var updated = settings with { Workspaces = workspaces.ToList() };
        if (settingsPath != null)
        {
            AppSettingsLoader.Save(settingsPath, updated);
        }
        else
        {
            AppSettingsLoader.Save(updated);
        }

        if (!string.IsNullOrEmpty(claudeMdPath) && File.Exists(claudeMdPath))
        {
            SaveGroups(claudeMdPath, workspaces);
        }

        var geminiMdPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "GEMINI.md");
        if (File.Exists(geminiMdPath))
        {
            SaveGroups(geminiMdPath, workspaces);
        }
    }

    /// <summary>
    /// 指定したワークスペースを除外して settings.json およびマークダウンに保存する。
    /// </summary>
    public static void RemoveWorkspace(string workspacePath, string? claudeMdPath = null, string? settingsPath = null)
    {
        var current = LoadWorkspaces(claudeMdPath, settingsPath).ToList();
        var normalizedTarget = workspacePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        current.RemoveAll(g => string.Equals(
            g.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            normalizedTarget,
            StringComparison.OrdinalIgnoreCase));
        SaveWorkspaces(current, claudeMdPath, settingsPath);
    }
}

