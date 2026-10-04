using System.IO;

namespace AgentDeskApp;

/// <summary>
/// 1件のエージェント定義の書き出し計画(どのファイル/フォルダに出力するか)。
/// </summary>
/// <param name="Engine">配置先(Claude / Gemini / Shared)。</param>
/// <param name="ClaudeFilePath">Claude側の.mdファイルパス。Claudeに配置しない場合はnull。</param>
/// <param name="GeminiSkillDir">Gemini側のスキルフォルダ(.agents/skills/{id})。Geminiに配置しない場合はnull。</param>
/// <param name="GeminiSkillFilePath">Gemini側のSKILL.mdパス。Geminiに配置しない場合はnull。</param>
/// <param name="ExistingPaths">書き出し先のうち既に存在するもの(上書き確認の対象)。</param>
public sealed record AgentDeployPlan(
    AgentEngineKind Engine,
    string? ClaudeFilePath,
    string? GeminiSkillDir,
    string? GeminiSkillFilePath,
    IReadOnlyList<string> ExistingPaths)
{
    /// <summary>書き出し先に既存のファイル/フォルダがあるか。</summary>
    public bool HasExisting => ExistingPaths.Count > 0;

    /// <summary>Claude側へ書き出すか。</summary>
    public bool DeployToClaude => ClaudeFilePath is not null;

    /// <summary>Gemini側へ書き出すか。</summary>
    public bool DeployToGemini => GeminiSkillDir is not null;
}

/// <summary>
/// エージェント定義(Claude側 .claude/agents/{id}.md と Gemini側 .agents/skills/{id}/SKILL.md)の
/// 書き出し先決定・書き出しを、UIから分離して共通化したクラス。
/// メンバー編集画面(<c>AgentEditWindow</c>)とテンプレート追加画面(<c>AgentTemplateImportWindow</c>)の両方から使う。
/// </summary>
public static class AgentDeployWriter
{
    /// <summary>Claude側のエージェント定義の拡張子付きファイル名の接尾辞。</summary>
    private const string ClaudeFileExtension = ".md";

    /// <summary>Gemini側スキル定義のファイル名。</summary>
    private const string GeminiSkillFileName = "SKILL.md";

    /// <summary>Gemini側スキルフォルダ内の標準アバター画像のベース名。</summary>
    private const string GeminiStandardAvatarBaseName = "avatar";

    /// <summary>テンプレート取り込み時に探すアバター画像の拡張子。</summary>
    private static readonly string[] TemplateAvatarExtensions = [".jpg", ".png", ".webp"];

    /// <summary>
    /// 配置先に応じた書き出し先を決める(ファイルは作らない)。
    /// </summary>
    /// <param name="claudeDir">.claude/agentsフォルダ。</param>
    /// <param name="geminiSkillsDir">.agents/skillsフォルダ。</param>
    /// <param name="id">エージェントの識別子(ファイル名・フォルダ名の元)。</param>
    /// <param name="engine">配置先。</param>
    /// <returns>書き出し計画(既存の書き出し先も含む)。</returns>
    public static AgentDeployPlan Plan(string claudeDir, string geminiSkillsDir, string id, AgentEngineKind engine)
    {
        var toClaude = engine is AgentEngineKind.Claude or AgentEngineKind.Shared;
        var toGemini = engine is AgentEngineKind.Gemini or AgentEngineKind.Shared;

        var claudeFile = toClaude ? Path.Combine(claudeDir, id + ClaudeFileExtension) : null;
        var skillDir = toGemini ? Path.Combine(geminiSkillsDir, id) : null;
        var skillFile = skillDir is null ? null : Path.Combine(skillDir, GeminiSkillFileName);

        var existing = new List<string>();
        if (claudeFile is not null && File.Exists(claudeFile))
        {
            existing.Add(claudeFile);
        }

        if (skillDir is not null && Directory.Exists(skillDir))
        {
            existing.Add(skillDir);
        }

        return new AgentDeployPlan(engine, claudeFile, skillDir, skillFile, existing);
    }

    /// <summary>
    /// 計画に従って定義を書き出す。配置先に含まれない側の既存定義は、
    /// <paramref name="removeUndeployedSide"/>がtrueのときだけ削除する(編集画面で配置先を変えた場合の整理)。
    /// </summary>
    /// <param name="plan">書き出し計画(<see cref="Plan"/>の結果)。</param>
    /// <param name="claudeDir">.claude/agentsフォルダ(計画外側の整理に使う)。</param>
    /// <param name="geminiSkillsDir">.agents/skillsフォルダ(計画外側の整理に使う)。</param>
    /// <param name="id">エージェントの識別子。</param>
    /// <param name="description">説明文。</param>
    /// <param name="tools">Claude側のtools(カンマ区切り)。</param>
    /// <param name="claudeModel">Claude側のモデル。</param>
    /// <param name="geminiModel">Gemini側のモデル(flash/pro等)。</param>
    /// <param name="color">色。</param>
    /// <param name="body">本文。</param>
    /// <param name="displayName">表示名。</param>
    /// <param name="avatarFileName">frontmatterのavatar値(書かない場合はnull)。</param>
    /// <param name="removeUndeployedSide">配置先に含まれない側の既存定義を削除するか。</param>
    public static void Write(
        AgentDeployPlan plan,
        string claudeDir,
        string geminiSkillsDir,
        string id,
        string description,
        string? tools,
        string? claudeModel,
        string? geminiModel,
        string? color,
        string body,
        string? displayName,
        string? avatarFileName,
        bool removeUndeployedSide)
    {
        if (plan.ClaudeFilePath is not null)
        {
            AgentDefinitionLoader.Save(
                plan.ClaudeFilePath, id, description, tools, claudeModel, color, body, displayName, avatarFileName,
                // Claude専用のときだけ、非表示になるGemini側モデルを補助キーで保持する
                auxGeminiModel: plan.GeminiSkillDir is null ? geminiModel : null);
        }
        else if (removeUndeployedSide)
        {
            var claudeFile = Path.Combine(claudeDir, id + ClaudeFileExtension);
            if (File.Exists(claudeFile))
            {
                File.Delete(claudeFile);
            }
        }

        if (plan.GeminiSkillDir is not null)
        {
            AgentDefinitionLoader.SaveGeminiSkill(
                plan.GeminiSkillDir, id, description, body, displayName, color, avatarFileName, geminiModel,
                // Gemini専用のときだけ、非表示になるClaude側モデルを補助キーで保持する
                auxClaudeModel: plan.ClaudeFilePath is null ? claudeModel : null);
        }
        else if (removeUndeployedSide)
        {
            var skillDir = Path.Combine(geminiSkillsDir, id);
            if (Directory.Exists(skillDir))
            {
                try
                {
                    Directory.Delete(skillDir, recursive: true);
                }
                catch
                {
                    // 削除失敗時はスキップ
                }
            }
        }
    }

    /// <summary>
    /// 同梱アバター画像(Assets/Avatars/{id}.jpg|png|webp)があれば、配置先に応じてコピーする
    /// (Claude: {claudeDir}/{id}.ext、Gemini: {skillDir}/{id}.ext と avatar.ext)。既存の画像は上書きしない。
    /// </summary>
    /// <param name="plan">書き出し計画。</param>
    /// <param name="claudeDir">.claude/agentsフォルダ。</param>
    /// <param name="id">エージェントの識別子。</param>
    /// <param name="bundledAvatarDir">同梱アバターのフォルダ。</param>
    public static void CopyBundledAvatarIfExists(AgentDeployPlan plan, string claudeDir, string id, string bundledAvatarDir)
    {
        try
        {
            foreach (var ext in TemplateAvatarExtensions)
            {
                var source = Path.Combine(bundledAvatarDir, id + ext);
                if (!File.Exists(source))
                {
                    continue;
                }

                if (plan.DeployToClaude)
                {
                    CopyIfAbsent(source, Path.Combine(claudeDir, id + ext));
                }

                if (plan.GeminiSkillDir is not null)
                {
                    CopyIfAbsent(source, Path.Combine(plan.GeminiSkillDir, id + ext));
                    CopyIfAbsent(source, Path.Combine(plan.GeminiSkillDir, GeminiStandardAvatarBaseName + ext));
                }

                break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] アバター画像コピー失敗(スキップ): {ex.Message}");
        }
    }

    /// <summary>
    /// ユーザーが選んだ(または現在使っている)アバター画像を、配置先に応じてコピーする
    /// (Claude: {claudeDir}/{id}.ext、Gemini: {skillDir}/{id}.ext と avatar.ext)。
    /// 配置先を変更しても画像が失われないよう、移行先へ確実にコピーする。コピー元は削除しない。
    /// </summary>
    /// <param name="plan">書き出し計画。</param>
    /// <param name="claudeDir">.claude/agentsフォルダ。</param>
    /// <param name="id">エージェントの識別子。</param>
    /// <param name="sourceAvatarPath">コピー元の画像パス。</param>
    public static void CopyAvatarToTargets(AgentDeployPlan plan, string claudeDir, string id, string sourceAvatarPath)
    {
        if (string.IsNullOrWhiteSpace(sourceAvatarPath) || !File.Exists(sourceAvatarPath))
        {
            return;
        }

        var ext = Path.GetExtension(sourceAvatarPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".png";
        }

        if (plan.ClaudeFilePath is not null)
        {
            CopyOverwrite(sourceAvatarPath, Path.Combine(claudeDir, id + ext));
        }

        if (plan.GeminiSkillDir is not null)
        {
            CopyOverwrite(sourceAvatarPath, Path.Combine(plan.GeminiSkillDir, id + ext));
            CopyOverwrite(sourceAvatarPath, Path.Combine(plan.GeminiSkillDir, GeminiStandardAvatarBaseName + ext));
        }
    }

    /// <summary>コピー先フォルダを作成して上書きコピーする(コピー元と同一なら何もしない)。</summary>
    private static void CopyOverwrite(string source, string dest)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] アバター画像コピー失敗(スキップ): {ex.Message}");
        }
    }

    /// <summary>コピー先が無い場合だけコピーする(フォルダが無ければ作成)。</summary>
    private static void CopyIfAbsent(string source, string dest)
    {
        if (File.Exists(dest))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: false);
    }
}
