using System.IO;

namespace AgentDeskApp;

/// <summary>
/// メンバー複製時に、元のメンバーの画像を複製先のId名でコピーする、UIから分離したロジック。
/// 元がユーザー設定の画像でも、同梱画像(Assets/Avatars/{元Id})による代替表示でも、
/// 複製先のIdでは同梱画像が見つからないため、実ファイルとしてコピーして表示を引き継ぐ。元の画像は削除しない。
/// </summary>
public static class AgentCloneAvatarCopier
{
    /// <summary>
    /// 元メンバーの画像を、複製先の配置先(Claude: {claudeDir}/{新Id}.ext、Gemini: {skillDir}/{新Id}.ext と avatar.ext)へコピーする。
    /// 画像なし(解決できない・「画像なし」の印)の場合は何もしない。
    /// </summary>
    /// <param name="source">複製元のメンバー定義。</param>
    /// <param name="plan">複製先の書き出し計画。</param>
    /// <param name="claudeDir">複製先の.claude/agentsフォルダ(Claude配置の場合に使う)。</param>
    /// <param name="newId">複製先のId。</param>
    /// <returns>画像をコピーしようとしたらtrue。コピー元が無ければfalse。</returns>
    public static bool CopyForClone(AgentDefinition source, AgentDeployPlan plan, string claudeDir, string newId)
    {
        if (source.AvatarDisabled || string.IsNullOrWhiteSpace(source.AvatarPath) || !File.Exists(source.AvatarPath))
        {
            return false;
        }

        AgentDeployWriter.CopyAvatarToTargets(plan, claudeDir, newId, source.AvatarPath);
        return true;
    }
}
