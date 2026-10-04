using System.IO;

namespace AgentDeskApp;

/// <summary>
/// .claude/agents/配下の.mdファイル(frontmatter付きMarkdown)を読み込み、
/// <see cref="AgentDefinition"/>に変換するローダー。
/// v1では公式frontmatter仕様のうちname/description/tools/model/colorのみを解釈し、
/// それ以外の未対応フィールド(permissionMode等)は無視する(読み飛ばして問題ない仕様であることは
/// 公式ドキュメントで検証済み)。
/// </summary>
public static class AgentDefinitionLoader
{
    private const string FrontmatterDelimiter = "---";

    /// <summary>
    /// Gemini専用で保存したとき、非表示になるClaude側モデルをSKILL.mdのfrontmatterへ退避するための補助キー名。
    /// Claude Code / Geminiは未知のfrontmatterキーを無視するため、動作には影響しない(displayName等の前例と同じ)。
    /// </summary>
    public const string ClaudeModelAuxKey = "claudeModel";

    /// <summary>
    /// Claude専用で保存したとき、非表示になるGemini側モデルを.mdのfrontmatterへ退避するための補助キー名。
    /// </summary>
    public const string GeminiModelAuxKey = "geminiModel";

    /// <summary>
    /// 指定フォルダ直下の*.mdファイルをすべて読み込む。フォルダが存在しない場合は空の一覧を返す。
    /// </summary>
    /// <param name="directoryPath">走査対象フォルダ。</param>
    /// <param name="scope">読み込んだ定義に付与するスコープ区分。</param>
    public static IReadOnlyList<AgentDefinition> LoadDirectory(string directoryPath, AgentScope scope)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var definitions = new List<AgentDefinition>();
        IEnumerable<string> filePaths;
        try
        {
            filePaths = Directory.EnumerateFiles(directoryPath, "*.md").OrderBy(p => p).ToList();
        }
        catch (IOException)
        {
            // Exists確認直後にフォルダごと削除された場合 (TOCTOUレース) も落ちずに空扱いにする (G-3)
            return [];
        }

        foreach (var filePath in filePaths)
        {
            try
            {
                definitions.Add(ParseFile(filePath, scope));
            }
            catch (IOException)
            {
                // 列挙後にアプリ外でファイルが削除・移動された場合はスキップして継続する (G-3)
            }
            catch (FormatException)
            {
                // frontmatterが壊れているファイルはスキップして他の読み込みは継続する (G-3)
            }
        }

        return definitions;
    }

    /// <summary>
    /// エージェント定義を.mdファイルとして書き出す(新規作成・編集どちらでも使う)。
    /// 既存ファイルへの上書き保存にも新規作成にも対応する。
    /// </summary>
    /// <param name="filePath">書き出し先の絶対パス。</param>
    /// <param name="name">frontmatterのname。</param>
    /// <param name="description">frontmatterのdescription。</param>
    /// <param name="tools">frontmatterのtools(省略時はnullまたは空文字)。</param>
    /// <param name="model">frontmatterのmodel(省略時はnullまたは空文字)。</param>
    /// <param name="color">frontmatterのcolor(省略時はnullまたは空文字)。</param>
    /// <param name="body">本文(個性・行動ルール)。</param>
    /// <param name="displayName">
    /// frontmatterのdisplayName(AgentDeskApp独自項目、省略時はnullまたは空文字)。<see cref="AgentDefinition.DisplayName"/>参照。
    /// </param>
    /// <param name="avatar">
    /// frontmatterのavatar(画像ファイル名、または「画像なし」の印 <see cref="NoAvatarMarker"/>。省略時はnullまたは空文字で書かない)。
    /// </param>
    /// <param name="auxGeminiModel">
    /// Claude専用で保存する場合に、非表示のGemini側モデルを補助キー(<see cref="GeminiModelAuxKey"/>)として保持する値。省略時は書かない。
    /// </param>
    public static void Save(
        string filePath, string name, string description, string? tools, string? model, string? color, string body,
        string? displayName = null, string? avatar = null, string? auxGeminiModel = null)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = new List<string> { FrontmatterDelimiter, $"name: {FormatYamlScalar(name)}", $"description: {FormatYamlScalar(description)}" };

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            lines.Add($"displayName: {FormatYamlScalar(displayName)}");
        }

        if (!string.IsNullOrWhiteSpace(avatar))
        {
            lines.Add($"avatar: {FormatYamlScalar(avatar)}");
        }

        if (!string.IsNullOrWhiteSpace(tools))
        {
            lines.Add($"tools: {FormatYamlScalar(tools)}");
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            lines.Add($"model: {FormatYamlScalar(model)}");
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            lines.Add($"color: {FormatYamlScalar(color)}");
        }

        if (!string.IsNullOrWhiteSpace(auxGeminiModel))
        {
            lines.Add($"{GeminiModelAuxKey}: {FormatYamlScalar(auxGeminiModel)}");
        }

        lines.Add(FrontmatterDelimiter);
        lines.Add(string.Empty);
        // 本文の改行はLFに統一して書く(ソースがCRLFでビルドされた既定テンプレート等でも、定義ファイルをLFで保つ)
        lines.Add(body.Replace("\r\n", "\n").Trim());
        lines.Add(string.Empty);

        AtomicFile.WriteAllText(filePath, string.Join('\n', lines));
    }

    /// <summary>
    /// Gemini(Antigravity)スキルとして .agents/skills/{name}/SKILL.md を書き出す。
    /// フォルダの作成や、MAPPING.md仕様に沿ったfrontmatter + 見出しの生成を行う。
    /// </summary>
    /// <param name="skillDir">書き出し先スキルフォルダ(.agents/skills/{name})の絶対パス。</param>
    /// <param name="name">frontmatterのname。</param>
    /// <param name="description">frontmatterのdescription。</param>
    /// <param name="body">本文(手順や行動ルール)。</param>
    /// <param name="displayName">表示名(本文冒頭の「# {displayName}」見出しとして活用)。</param>
    /// <param name="color">frontmatterのcolor(拡張メタデータ)。</param>
    /// <param name="avatar">frontmatterのavatar(画像ファイル名、または「画像なし」の印 <see cref="NoAvatarMarker"/>)。</param>
    /// <param name="model">
    /// frontmatterのmodel(Antigravityのinvoke_subagentで指定できる値: flash/pro/flash_lite/inherit。
    /// 省略時はnullまたは空文字) (U-23)。
    /// </param>
    /// <param name="auxClaudeModel">
    /// Gemini専用で保存する場合に、非表示のClaude側モデルを補助キー(<see cref="ClaudeModelAuxKey"/>)として保持する値。省略時は書かない。
    /// </param>
    /// <returns>保存されたSKILL.mdの絶対パス。</returns>
    public static string SaveGeminiSkill(
        string skillDir,
        string name,
        string description,
        string body,
        string? displayName = null,
        string? color = null,
        string? avatar = null,
        string? model = null,
        string? auxClaudeModel = null)
    {
        Directory.CreateDirectory(skillDir);
        var skillFile = Path.Combine(skillDir, "SKILL.md");

        var lines = new List<string>
        {
            FrontmatterDelimiter,
            $"name: {FormatYamlScalar(name)}",
            "description: >-",
            $"  {description.Replace("\r\n", " ").Replace("\n", " ").Trim()}",
        };

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            lines.Add($"displayName: {FormatYamlScalar(displayName)}");
        }

        if (!string.IsNullOrWhiteSpace(avatar))
        {
            lines.Add($"avatar: {FormatYamlScalar(avatar)}");
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            lines.Add($"model: {FormatYamlScalar(model)}");
        }

        if (!string.IsNullOrWhiteSpace(color))
        {
            lines.Add($"color: {FormatYamlScalar(color)}");
        }

        if (!string.IsNullOrWhiteSpace(auxClaudeModel))
        {
            lines.Add($"{ClaudeModelAuxKey}: {FormatYamlScalar(auxClaudeModel)}");
        }

        lines.Add(FrontmatterDelimiter);
        lines.Add(string.Empty);

        // 本文の改行はLFに統一して書く(CRLFのソースから生成された本文でも定義ファイルをLFで保つ)
        var trimmedBody = body.Replace("\r\n", "\n").Trim();
        var effectiveTitle = !string.IsNullOrWhiteSpace(displayName) ? displayName : name;

        // 本文先頭に # 見出しが無い場合はタイトル見出しを補完
        if (!trimmedBody.StartsWith('#'))
        {
            lines.Add($"# {effectiveTitle}");
            lines.Add(string.Empty);
        }

        lines.Add(trimmedBody);
        lines.Add(string.Empty);

        AtomicFile.WriteAllText(skillFile, string.Join('\n', lines));
        return skillFile;
    }

    /// <summary>
    /// エージェント定義(Claude側.mdおよびGeminiスキルフォルダ)を別チーム/グループへ移動する
    /// (F-3: メンバーカードのドラッグ&amp;ドロップによる異動)。
    /// 移動先に同名の定義がすでに存在する場合は上書きせず、何も移動せずに例外を投げる(呼び出し元でメッセージ表示すること)。
    /// 途中で失敗した場合は移動済みのファイルを元に戻す(BUG-10)。同位置へのドロップは何もせずfalseを返す。
    /// </summary>
    /// <param name="agent">移動対象のエージェント定義。</param>
    /// <param name="targetClaudeDir">移動先の.claude/agents/ディレクトリ。</param>
    /// <param name="targetGeminiDir">移動先の.agents/skills/ディレクトリ。</param>
    /// <returns>実際に何かを移動した場合はtrue。移動元と移動先が同じで何もしなかった場合はfalse。</returns>
    /// <exception cref="IOException">移動先に同名の定義がすでに存在する場合。</exception>
    /// <exception cref="ArgumentException">エージェント名にパス区切りや".."等の不正な文字が含まれる場合(BUG-11)。</exception>
    public static bool MoveAgent(AgentDefinition agent, string targetClaudeDir, string targetGeminiDir)
    {
        // ---- 1. 計画: 何をどこへ動かすかを先に決め、名前検証と衝突チェックを全て済ませる ----
        string? sourceClaudeDir = null;
        string? claudeSrc = null;
        string? claudeDest = null;

        if (agent.Engine is AgentEngineKind.Claude or AgentEngineKind.Shared && File.Exists(agent.FilePath))
        {
            sourceClaudeDir = Path.GetDirectoryName(agent.FilePath);
            var dest = Path.Combine(targetClaudeDir, Path.GetFileName(agent.FilePath));
            if (!IsSamePath(agent.FilePath, dest))
            {
                if (File.Exists(dest))
                {
                    throw new IOException($"移動先に同名のエージェント定義「{agent.Name}」がすでに存在します。");
                }

                claudeSrc = agent.FilePath;
                claudeDest = dest;
            }
        }

        // Gemini単独の場合はFilePath自体がSKILL.mdパス、Sharedの場合はGeminiSkillPathを見る。
        string? geminiSrcDir = null;
        string? geminiDestDir = null;
        var geminiSkillFile = agent.Engine == AgentEngineKind.Gemini ? agent.FilePath : agent.GeminiSkillPath;
        if (!string.IsNullOrWhiteSpace(geminiSkillFile) && File.Exists(geminiSkillFile))
        {
            var sourceSkillDir = Path.GetDirectoryName(geminiSkillFile)!;
            var destSkillDir = ResolveChildPath(targetGeminiDir, agent.Name);

            if (!IsSamePath(sourceSkillDir, destSkillDir))
            {
                if (Directory.Exists(destSkillDir))
                {
                    throw new IOException($"移動先に同名のGeminiスキル「{agent.Name}」がすでに存在します。");
                }

                geminiSrcDir = sourceSkillDir;
                geminiDestDir = destSkillDir;
            }
        }

        if (claudeSrc is null && geminiSrcDir is null)
        {
            return false;
        }

        // ---- 2. 実行: Claude側を動かした後にGemini側が失敗したら、Claude側を元に戻す ----
        if (claudeSrc is not null && claudeDest is not null)
        {
            Directory.CreateDirectory(targetClaudeDir);
            File.Move(claudeSrc, claudeDest);
        }

        try
        {
            if (geminiSrcDir is not null && geminiDestDir is not null)
            {
                Directory.CreateDirectory(targetGeminiDir);
                Directory.Move(geminiSrcDir, geminiDestDir);
            }
        }
        catch
        {
            if (claudeSrc is not null && claudeDest is not null && File.Exists(claudeDest) && !File.Exists(claudeSrc))
            {
                try { File.Move(claudeDest, claudeSrc); } catch (IOException) { /* ロールバック失敗時は元の例外を優先して通知する */ }
            }

            throw;
        }

        // Claude側のアバター画像は.mdと同じフォルダに個別配置されている場合があるため追従して移動する。
        // (Gemini側はスキルフォルダごとDirectory.Moveしているため自動的に追従済み)
        if (sourceClaudeDir is not null &&
            !string.IsNullOrWhiteSpace(agent.AvatarPath) &&
            File.Exists(agent.AvatarPath) &&
            string.Equals(Path.GetDirectoryName(agent.AvatarPath), sourceClaudeDir, StringComparison.OrdinalIgnoreCase))
        {
            var destAvatar = Path.Combine(targetClaudeDir, Path.GetFileName(agent.AvatarPath));
            if (!File.Exists(destAvatar))
            {
                try { File.Move(agent.AvatarPath, destAvatar); } catch (IOException) { /* アバター移動失敗は致命的ではないため無視 */ }
            }
        }

        return true;
    }

    /// <summary>2つのパスが(大文字小文字を無視して)同じ場所を指すか判定する。</summary>
    private static bool IsSamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 親ディレクトリ配下の子パスを組み立てる。子の名前にパス区切り・".."・不正なファイル名文字が含まれる場合や、
    /// 結果が親ディレクトリの外を指す場合は例外にする(BUG-11: パストラバーサル対策)。
    /// </summary>
    /// <param name="parentDir">親ディレクトリ。</param>
    /// <param name="childName">子の名前(エージェント名)。</param>
    /// <exception cref="ArgumentException">名前が不正な場合。</exception>
    private static string ResolveChildPath(string parentDir, string childName)
    {
        if (string.IsNullOrWhiteSpace(childName) ||
            childName is "." or ".." ||
            childName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"エージェント名「{childName}」にはパスとして使えない文字が含まれています。", nameof(childName));
        }

        var parentFull = Path.GetFullPath(parentDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childFull = Path.GetFullPath(Path.Combine(parentDir, childName));
        if (!childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"エージェント名「{childName}」は移動先フォルダの外を指しています。", nameof(childName));
        }

        return childFull;
    }

    /// <summary>
    /// エージェント定義ファイルを削除する。呼び出し元(UI)で削除確認を行ってから呼ぶこと。
    /// </summary>
    /// <param name="filePath">削除する.mdファイルの絶対パス。</param>
    public static void Delete(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    /// <summary>
    /// エージェント定義(および対応するGeminiスキル)を削除し、そのアバター画像が
    /// 同スコープ内の他のエージェントから参照されていなければ画像ファイルも削除する(G-5)。
    /// </summary>
    /// <param name="agent">削除対象のエージェント定義。</param>
    /// <param name="scopeAgents">
    /// アバター画像の孤立判定に使う、同スコープ内の全エージェント一覧(削除対象を含んでいてよい)。
    /// </param>
    /// <param name="bundledAvatarDirectory">同梱画像フォルダ。null なら <see cref="DefaultBundledAvatarDirectory"/>(テスト用の差し替え引数)。</param>
    public static void DeleteAgentAndOrphanedAvatar(AgentDefinition agent, IEnumerable<AgentDefinition> scopeAgents, string? bundledAvatarDirectory = null)
    {
        if (File.Exists(agent.FilePath))
        {
            File.Delete(agent.FilePath);
        }

        if (!string.IsNullOrWhiteSpace(agent.GeminiSkillPath) && File.Exists(agent.GeminiSkillPath))
        {
            File.Delete(agent.GeminiSkillPath);
        }

        if (string.IsNullOrWhiteSpace(agent.AvatarPath) || !File.Exists(agent.AvatarPath))
        {
            return;
        }

        // 同梱画像の代替表示はアプリ本体のファイルなので、メンバー削除時に消してはいけない
        if (IsBundledAvatarPath(agent.AvatarPath, bundledAvatarDirectory))
        {
            return;
        }

        var isReferencedByOthers = scopeAgents.Any(other =>
            other.Name != agent.Name &&
            !string.IsNullOrWhiteSpace(other.AvatarPath) &&
            string.Equals(other.AvatarPath, agent.AvatarPath, StringComparison.OrdinalIgnoreCase));

        if (isReferencedByOthers)
        {
            return;
        }

        try
        {
            File.Delete(agent.AvatarPath);
        }
        catch (IOException)
        {
            // 画像が使用中等で削除できなくても、本体の削除自体は完了扱いとする
        }
    }

    /// <summary>
    /// エージェント名からファイル名(拡張子無しの安全な文字列)を生成する。
    /// name自体が識別子として安全な形式(英数字・ハイフン)である前提だが、
    /// 念のため空白や記号をハイフンに置換して安全側に倒す。
    /// </summary>
    /// <param name="name">frontmatterのname。</param>
    public static string ToSafeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalidChars.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray();
        var safe = new string(chars).Trim('-');
        return safe.Length == 0 ? "agent" : safe;
    }

    /// <summary>
    /// 1件の.mdファイルをパースして<see cref="AgentDefinition"/>に変換する。
    /// </summary>
    /// <param name="filePath">読み込む.mdファイルの絶対パス。</param>
    /// <param name="scope">この定義に付与するスコープ区分。</param>
    /// <exception cref="FormatException">frontmatterの区切り(---)が見つからない場合。</exception>
    public static AgentDefinition ParseFile(string filePath, AgentScope scope)
    {
        var text = File.ReadAllText(filePath);
        return Parse(text, scope, filePath);
    }

    /// <summary>
    /// frontmatter付きMarkdownの文字列本体をパースする(テストから直接呼べるように公開)。
    /// </summary>
    /// <param name="text">.mdファイルの中身全体。</param>
    /// <param name="scope">この定義に付与するスコープ区分。</param>
    /// <param name="filePath">呼び出し元のファイルパス(表示・編集時の書き戻し先として保持するほか、アバター画像の探索基準フォルダにも使う)。</param>
    /// <param name="bundledAvatarDirectory">
    /// アプリ同梱アバター画像のフォルダ。null なら <see cref="DefaultBundledAvatarDirectory"/> を使う
    /// (単体テストで一時フォルダに差し替えるための引数)。
    /// </param>
    public static AgentDefinition Parse(string text, AgentScope scope, string filePath, string? bundledAvatarDirectory = null)
    {
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != FrontmatterDelimiter)
        {
            throw new FormatException($"frontmatterの開始区切り(---)が見つかりません: {filePath}");
        }

        var endIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            // 字下げされた「---」(ブロック値の中身など)は終了区切りとして扱わない
            if (lines[i].TrimEnd() == FrontmatterDelimiter)
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex < 0)
        {
            throw new FormatException($"frontmatterの終了区切り(---)が見つかりません: {filePath}");
        }

        var fields = ParseFrontmatterFields(lines[1..endIndex]);
        var body = string.Join('\n', lines[(endIndex + 1)..]).Trim();

        if (!fields.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException($"必須項目 name がありません: {filePath}");
        }

        if (!fields.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
        {
            throw new FormatException($"必須項目 description がありません: {filePath}");
        }

        fields.TryGetValue("tools", out var tools);
        fields.TryGetValue("model", out var model);
        fields.TryGetValue(GeminiModelAuxKey, out var auxGeminiModel);
        fields.TryGetValue("color", out var color);
        fields.TryGetValue("displayName", out var displayName);
        fields.TryGetValue("avatar", out var avatar);
        if (string.IsNullOrWhiteSpace(avatar))
        {
            fields.TryGetValue("icon", out avatar);
        }

        // 「画像なし」の印(avatar: none)があれば、同名画像も同梱画像も探さず頭文字表示にする
        if (IsNoAvatarMarker(avatar))
        {
            return new AgentDefinition(
                name, description, tools, model, color, body, scope, filePath, displayName, AvatarPath: null, AvatarDisabled: true,
                GeminiModel: string.IsNullOrWhiteSpace(auxGeminiModel) ? null : auxGeminiModel);
        }

        string? resolvedAvatarPath = null;
        if (!string.IsNullOrWhiteSpace(avatar))
        {
            var dir = Path.GetDirectoryName(filePath);
            var candidate = Path.IsPathRooted(avatar) ? avatar : Path.Combine(dir ?? "", avatar);
            if (File.Exists(candidate))
            {
                resolvedAvatarPath = candidate;
            }
        }

        if (resolvedAvatarPath is null && !string.IsNullOrWhiteSpace(filePath))
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is not null && Directory.Exists(dir))
            {
                var baseName = Path.GetFileNameWithoutExtension(filePath);
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                {
                    var candidate = Path.Combine(dir, baseName + ext);
                    if (File.Exists(candidate))
                    {
                        resolvedAvatarPath = candidate;
                        break;
                    }
                }
            }
        }

        // 1) frontmatter指定 2) 定義ファイルと同名の画像 のどちらも無い場合だけ、最後の候補として同梱画像を使う。
        // 同梱画像が入る前にテンプレートから取り込んだメンバーでも画像が出るようにするため(表示時に参照するだけでコピーはしない)。
        resolvedAvatarPath ??= FindBundledAvatar(name, bundledAvatarDirectory);

        return new AgentDefinition(
            name, description, tools, model, color, body, scope, filePath, displayName, resolvedAvatarPath,
            GeminiModel: string.IsNullOrWhiteSpace(auxGeminiModel) ? null : auxGeminiModel);
    }

    /// <summary>
    /// frontmatter の avatar に書く「画像なし」の印。編集画面の「×」で画像を解除して保存したときに書き込む。
    /// </summary>
    public const string NoAvatarMarker = "none";

    /// <summary>
    /// frontmatter の avatar(または icon)の値が「画像なし」の印(none、大文字小文字を区別しない)かどうかを判定する。
    /// </summary>
    /// <param name="avatarValue">frontmatter の avatar / icon の値。</param>
    public static bool IsNoAvatarMarker(string? avatarValue) =>
        string.Equals(avatarValue?.Trim(), NoAvatarMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 編集画面の保存時に frontmatter の avatar へ書く値を決める(ファイルのコピー・削除は呼び出し側で行う)。
    /// </summary>
    /// <param name="avatarRemoved">「×」で画像を解除した状態ならtrue。「画像なし」の印(none)を返す。</param>
    /// <param name="currentAvatarPath">編集画面で現在選ばれている画像のパス(同梱画像なら avatar を書かない)。</param>
    /// <param name="safeName">エージェント名(コピー先のファイル名 {safeName}{拡張子} に使う)。</param>
    /// <param name="existingAvatarPath">編集前に解決されていたアバター画像のパス(画像を変えずに保存する場合に引き継ぐ)。</param>
    /// <param name="bundledAvatarDirectory">同梱画像フォルダ。null なら <see cref="DefaultBundledAvatarDirectory"/>(テスト用の差し替え引数)。</param>
    /// <returns>avatar に書く値。書かない場合は null。</returns>
    public static string? ResolveAvatarFieldForSave(
        bool avatarRemoved,
        string? currentAvatarPath,
        string safeName,
        string? existingAvatarPath,
        string? bundledAvatarDirectory = null)
    {
        if (avatarRemoved)
        {
            return NoAvatarMarker;
        }

        // 同梱画像(「標準の画像に戻す」や代替表示のまま)は avatar を書かない → 読み込み時に同梱画像が表示される
        if (IsBundledAvatarPath(currentAvatarPath, bundledAvatarDirectory))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(currentAvatarPath) && File.Exists(currentAvatarPath))
        {
            var ext = Path.GetExtension(currentAvatarPath).ToLowerInvariant();
            return $"{safeName}{(string.IsNullOrEmpty(ext) ? ".png" : ext)}";
        }

        if (!string.IsNullOrWhiteSpace(existingAvatarPath) && File.Exists(existingAvatarPath) &&
            !IsBundledAvatarPath(existingAvatarPath, bundledAvatarDirectory))
        {
            return Path.GetFileName(existingAvatarPath);
        }

        return null;
    }

    /// <summary>
    /// アプリ同梱アバター画像の既定フォルダ(exe と同じ場所の Assets\Avatars)。
    /// </summary>
    public static string DefaultBundledAvatarDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Avatars");

    /// <summary>同梱アバター画像として探す拡張子(優先順)。</summary>
    private static readonly string[] BundledAvatarExtensions = [".jpg", ".png", ".webp"];

    /// <summary>
    /// エージェント名に対応する同梱アバター画像({name}.jpg/.png/.webp)を探す。
    /// </summary>
    /// <param name="name">エージェントの name(テンプレートの Id と同じ)。</param>
    /// <param name="bundledAvatarDirectory">同梱画像フォルダ。null なら <see cref="DefaultBundledAvatarDirectory"/>。</param>
    /// <returns>見つかった画像の絶対パス。無ければ null。</returns>
    public static string? FindBundledAvatar(string name, string? bundledAvatarDirectory = null)
    {
        var dir = bundledAvatarDirectory ?? DefaultBundledAvatarDirectory;
        if (string.IsNullOrWhiteSpace(name) || !Directory.Exists(dir))
        {
            return null;
        }

        var safeName = ToSafeFileName(name);
        foreach (var ext in BundledAvatarExtensions)
        {
            var candidate = Path.Combine(dir, safeName + ext);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// 指定パスが同梱アバターフォルダ内の画像(=ユーザーが設定した画像ではなく代替表示)かどうかを判定する。
    /// 同梱画像を削除・コピーしてしまわないためのガードに使う。
    /// </summary>
    /// <param name="avatarPath">判定するアバター画像のパス。</param>
    /// <param name="bundledAvatarDirectory">同梱画像フォルダ。null なら <see cref="DefaultBundledAvatarDirectory"/>。</param>
    public static bool IsBundledAvatarPath(string? avatarPath, string? bundledAvatarDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(avatarPath))
        {
            return false;
        }

        try
        {
            var dir = Path.GetFullPath(bundledAvatarDirectory ?? DefaultBundledAvatarDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Path.GetDirectoryName(Path.GetFullPath(avatarPath));
            return parent is not null && string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// .agents/skills/配下の各スキルフォルダ内のSKILL.mdをすべて読み込む。
    /// フォルダが存在しない場合は空の一覧を返す。
    /// </summary>
    /// <param name="skillsDir">.agents/skills/の絶対パス。</param>
    /// <param name="scope">付与するスコープ区分。</param>
    public static IReadOnlyList<AgentDefinition> LoadGeminiSkills(string skillsDir, AgentScope scope)
    {
        if (!Directory.Exists(skillsDir))
        {
            return [];
        }

        var list = new List<AgentDefinition>();
        foreach (var subDir in Directory.EnumerateDirectories(skillsDir).OrderBy(d => d))
        {
            var skillFile = Path.Combine(subDir, "SKILL.md");
            if (File.Exists(skillFile))
            {
                try
                {
                    list.Add(ParseSkillFile(skillFile, scope));
                }
                catch
                {
                    // 形式エラー等があっても他のスキルの読み込みは継続する
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Claudeエージェント(.claude/agents/)とGeminiスキル(.agents/skills/)の両方を読み込み、
    /// 同名のエージェントをSharedとしてマージした一覧を返す。
    /// </summary>
    public static IReadOnlyList<AgentDefinition> LoadScopeAgents(string? claudeDir, string? geminiSkillsDir, AgentScope scope)
    {
        var claudeAgents = !string.IsNullOrWhiteSpace(claudeDir) && Directory.Exists(claudeDir)
            ? LoadDirectory(claudeDir, scope)
            : [];

        var geminiSkills = !string.IsNullOrWhiteSpace(geminiSkillsDir) && Directory.Exists(geminiSkillsDir)
            ? LoadGeminiSkills(geminiSkillsDir, scope)
            : [];

        if (geminiSkills.Count == 0)
        {
            return claudeAgents;
        }

        if (claudeAgents.Count == 0)
        {
            return geminiSkills;
        }

        var result = new List<AgentDefinition>();
        var geminiDict = geminiSkills.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var claude in claudeAgents)
        {
            if (geminiDict.TryGetValue(claude.Name, out var gemini))
            {
                // 両方に存在するため Shared としてマージ
                result.Add(claude with
                {
                    Engine = AgentEngineKind.Shared,
                    GeminiSkillPath = gemini.FilePath,
                    GeminiModel = gemini.Model,
                    // Claude側に「画像なし」の印があれば、Gemini側の画像も使わず頭文字表示にする。
                    // Claude側が同梱画像の代替表示なら、Gemini側でユーザーが設定した画像を優先する
                    AvatarPath = claude.AvatarDisabled
                        ? null
                        : claude.AvatarPath is not null && !IsBundledAvatarPath(claude.AvatarPath)
                            ? claude.AvatarPath
                            : gemini.AvatarPath ?? claude.AvatarPath,
                    AvatarDisabled = claude.AvatarDisabled,
                });
                geminiDict.Remove(claude.Name);
            }
            else
            {
                result.Add(claude);
            }
        }

        // Geminiのみに存在するものを追加
        result.AddRange(geminiDict.Values);

        return result.OrderBy(a => a.Name).ToList();
    }

    /// <summary>
    /// 1件のSKILL.mdファイルをパースして<see cref="AgentDefinition"/>(Engine=Gemini)に変換する。
    /// </summary>
    /// <param name="filePath">SKILL.mdのパス。</param>
    /// <param name="scope">スコープ区分。</param>
    /// <param name="bundledAvatarDirectory">同梱画像フォルダ。null なら既定(テスト用の差し替え引数)。</param>
    public static AgentDefinition ParseSkillFile(string filePath, AgentScope scope, string? bundledAvatarDirectory = null)
    {
        var text = File.ReadAllText(filePath);
        return ParseSkill(text, scope, filePath, bundledAvatarDirectory);
    }

    /// <summary>
    /// SKILL.mdの文字列本体をパースする。
    /// </summary>
    /// <param name="text">SKILL.mdの中身全体。</param>
    /// <param name="scope">スコープ区分。</param>
    /// <param name="filePath">SKILL.mdのパス(アバター画像の探索基準フォルダにも使う)。</param>
    /// <param name="bundledAvatarDirectory">同梱画像フォルダ。null なら既定(テスト用の差し替え引数)。</param>
    public static AgentDefinition ParseSkill(string text, AgentScope scope, string filePath, string? bundledAvatarDirectory = null)
    {
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0].Trim() != FrontmatterDelimiter)
        {
            throw new FormatException($"frontmatterの開始区切り(---)が見つかりません: {filePath}");
        }

        var endIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            // 字下げされた「---」(ブロック値の中身など)は終了区切りとして扱わない
            if (lines[i].TrimEnd() == FrontmatterDelimiter)
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex < 0)
        {
            throw new FormatException($"frontmatterの終了区切り(---)が見つかりません: {filePath}");
        }

        var fields = ParseFrontmatterFields(lines[1..endIndex]);
        var body = string.Join('\n', lines[(endIndex + 1)..]).Trim();

        if (!fields.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            throw new FormatException($"必須項目 name がありません: {filePath}");
        }

        if (!fields.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
        {
            throw new FormatException($"必須項目 description がありません: {filePath}");
        }

        fields.TryGetValue("displayName", out var displayName);
        fields.TryGetValue("color", out var color);
        fields.TryGetValue("model", out var model);
        fields.TryGetValue(ClaudeModelAuxKey, out var auxClaudeModel);

        // displayNameが未指定の場合、本文冒頭の「# タイトル」があればそれを表示名として活用
        if (string.IsNullOrWhiteSpace(displayName))
        {
            var titleLine = lines.Skip(endIndex + 1).FirstOrDefault(l => l.TrimStart().StartsWith('#'));
            if (!string.IsNullOrWhiteSpace(titleLine))
            {
                displayName = titleLine.TrimStart('#', ' ').Trim();
            }
        }

        // 「画像なし」の印(avatar: none)があれば、フォルダ内の画像を探さず頭文字表示にする(Claude側と同じ扱い)
        fields.TryGetValue("avatar", out var avatarValue);
        var avatarDisabled = IsNoAvatarMarker(avatarValue);

        // アバター画像の探索 (同フォルダ内)。1) frontmatterのavatar 2) 標準名の画像 3) 同梱画像(Claude側と同じ最後の候補)
        string? resolvedAvatarPath = null;
        var dir = Path.GetDirectoryName(filePath);
        if (!avatarDisabled && dir is not null && Directory.Exists(dir))
        {
            if (!string.IsNullOrWhiteSpace(avatarValue))
            {
                var specified = Path.IsPathRooted(avatarValue) ? avatarValue : Path.Combine(dir, avatarValue);
                if (File.Exists(specified))
                {
                    resolvedAvatarPath = specified;
                }
            }

            if (resolvedAvatarPath is null)
            {
                var candidates = new List<string>();
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                {
                    candidates.Add("avatar" + ext);
                    candidates.Add("icon" + ext);
                }

                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                {
                    candidates.Add(name + ext);
                }

                foreach (var c in candidates)
                {
                    var candidatePath = Path.Combine(dir, c);
                    if (File.Exists(candidatePath))
                    {
                        resolvedAvatarPath = candidatePath;
                        break;
                    }
                }
            }
        }

        // 画像が無くても、アプリ同梱画像(Assets/Avatars/{name}.*)があれば代替表示する(Gemini専用でも画像が消えないように)
        if (!avatarDisabled)
        {
            resolvedAvatarPath ??= FindBundledAvatar(name, bundledAvatarDirectory);
        }

        return new AgentDefinition(
            Name: name,
            Description: description,
            Tools: null,
            Model: model,
            Color: color,
            Body: body,
            Scope: scope,
            FilePath: filePath,
            DisplayName: displayName,
            AvatarPath: resolvedAvatarPath,
            Engine: AgentEngineKind.Gemini,
            AvatarDisabled: avatarDisabled,
            ClaudeModel: string.IsNullOrWhiteSpace(auxClaudeModel) ? null : auxClaudeModel);
    }

    /// <summary>
    /// frontmatterに書く1行分の値を、YAMLとして壊れない形に整形する。
    /// 安全な文字列はそのまま(プレーン)、特殊文字(": "・" #"・先頭の記号・改行・前後の空白・クォートなど)を
    /// 含む場合はYAMLのダブルクォート形式(バックスラッシュ・ダブルクォート・改行・タブをエスケープ)で返す。
    /// 読み込み側の <see cref="TrimQuotes"/> で元の値に戻せる。
    /// </summary>
    /// <param name="value">書き出す値。</param>
    public static string FormatYamlScalar(string value)
    {
        if (!NeedsYamlQuoting(value))
        {
            return value;
        }

        var sb = new System.Text.StringBuilder("\"");
        foreach (var c in value.Replace("\r\n", "\n").Replace('\r', '\n'))
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.Append('"').ToString();
    }

    /// <summary>プレーンスカラーのままではYAMLとして曖昧・不正になる値かどうか。</summary>
    /// <param name="value">判定する値。</param>
    private static bool NeedsYamlQuoting(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        if (value.Any(c => c is '\n' or '\r' or '\t') || value != value.Trim())
        {
            return true;
        }

        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0]))
        {
            return true;
        }

        if (value.Contains(": ") || value.Contains(" #") || value.EndsWith(':'))
        {
            return true;
        }

        // true/false/null/数値などは、YAMLパーサーに文字列以外として解釈されないよう引用する
        return value.ToLowerInvariant() is "true" or "false" or "yes" or "no" or "on" or "off" or "null" or "~"
            || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// ダブルクォート内のエスケープ(\\ \" \n \t \r)を元に戻す。未知のエスケープ(Windowsパス等)はそのまま残す。
    /// </summary>
    /// <param name="inner">前後のダブルクォートを除いた中身。</param>
    private static string UnescapeDoubleQuoted(string inner)
    {
        if (!inner.Contains('\\'))
        {
            return inner;
        }

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length && inner[i + 1] is '\\' or '"' or 'n' or 't' or 'r')
            {
                sb.Append(inner[++i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var c => c });
            }
            else
            {
                sb.Append(inner[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// frontmatter内の各行を「key: value」形式としてパースする。
    /// 折返し行(インデントされた複数行)や「>-」も結合して解釈する。
    /// </summary>
    private static Dictionary<string, string> ParseFrontmatterFields(IEnumerable<string> frontmatterLines)
    {
        var fields = new Dictionary<string, string>();
        string? currentKey = null;
        var currentValues = new List<string>();
        var currentIsBlock = false;

        foreach (var rawLine in frontmatterLines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            // 先頭が空白系(半角/タブ/全角スペースなど)ならインデント扱い。全角スペースで折り返し行を
            // 字下げしても、新しいキーとして誤認しないようにする (BUG-7)。
            var isIndented = char.IsWhiteSpace(rawLine[0]);
            if (isIndented && currentKey != null)
            {
                // 複数行フィールドの継続
                var trimmed = rawLine.Trim();
                if (trimmed.Length > 0)
                {
                    currentValues.Add(trimmed);
                }
                continue;
            }

            // 新しいキーの検出
            if (currentKey != null)
            {
                fields[currentKey] = currentIsBlock ? string.Join(" ", currentValues).Trim() : TrimQuotes(JoinFieldValues(currentValues).Trim());
                currentValues.Clear();
                currentKey = null;
            }

            var separatorIndex = rawLine.IndexOf(':');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = rawLine[..separatorIndex].Trim();
            var value = rawLine[(separatorIndex + 1)..].Trim();

            if (key.Length > 0)
            {
                currentKey = key;
                currentIsBlock = value is ">-" or ">" or "|";
                if (value == ">-" || value == ">" || value == "|")
                {
                    // 後続のインデント行を待つ
                }
                else if (value.Length > 0)
                {
                    currentValues.Add(value);
                }
            }
        }

        if (currentKey != null)
        {
            fields[currentKey] = currentIsBlock ? string.Join(" ", currentValues).Trim() : TrimQuotes(JoinFieldValues(currentValues).Trim());
        }

        return fields;
    }

    /// <summary>
    /// 複数行にまたがるfrontmatter値を1つの文字列に結合する。
    /// YAMLの箇条書き配列記法(<c>- Read</c>のような行)ならカンマ区切りに正規化し、
    /// それ以外(通常の折返し文)はスペース結合する (C-6)。
    /// 配列の場合、結合後ではなく各要素単位で前後の空白と引用符をトリムしてから結合する(C-6+)。
    /// これにより<c>- 'Read'</c>や<c>-   "Write"  </c>のような要素ごとの余分な
    /// クォート・空白混入にも対応できる。
    /// </summary>
    private static string JoinFieldValues(List<string> values)
    {
        if (values.Count > 0 && values.All(IsListItem))
        {
            return string.Join(", ", values.Select(v => TrimQuotes((v == "-" ? "" : v[1..]).Trim())));
        }

        return string.Join(" ", values);
    }

    /// <summary>YAML箇条書きの1要素(<c>-</c>単独、または<c>-</c>の直後に空白系文字。全角スペース含む)かどうか (BUG-7)。</summary>
    private static bool IsListItem(string value) =>
        value == "-" || (value.Length >= 2 && value[0] == '-' && char.IsWhiteSpace(value[1]));

    /// <summary>
    /// 値を囲む引用符を外す。前後が対になっていれば両方外す。
    /// 閉じ忘れ(先頭だけ、または末尾だけの引用符)は、YAML崩れとして値全体を壊さないよう
    /// 孤立した引用符1つだけを取り除いて読み込みを継続する (BUG-7)。
    /// </summary>
    private static string TrimQuotes(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var first = value[0];
        var last = value[^1];
        var startsWithQuote = first is '"' or '\'';

        if (value.Length >= 2 && startsWithQuote && first == last)
        {
            var inner = value[1..^1];
            return first == '"' ? UnescapeDoubleQuoted(inner) : inner.Replace("''", "'");
        }

        // 未閉じ引用符(開始側のみ)。末尾側だけの引用符は「5'」のような正常な値の可能性があるため触らない。
        if (startsWithQuote && !value[1..].Contains(first))
        {
            return value[1..];
        }

        return value;
    }
}
