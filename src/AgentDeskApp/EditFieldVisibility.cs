namespace AgentDeskApp;

/// <summary>
/// メンバー編集画面で、配備先(エンジン)ごとに表示する入力欄の可否を表す。
/// </summary>
/// <param name="ClaudeModel">「モデル(Claude)」欄を表示するか。</param>
/// <param name="Color">「色(Claude Code)」欄を表示するか。</param>
/// <param name="GeminiModel">「モデル(Gemini)」欄を表示するか。</param>
public sealed record EditFieldVisibility(bool ClaudeModel, bool Color, bool GeminiModel)
{
    /// <summary>
    /// 配備先から各欄の表示可否を求める(UIに依存しない純粋な判定)。
    /// Claude専用: Claudeモデル+色 / Gemini専用: Geminiモデルのみ / Shared: すべて。
    /// </summary>
    /// <param name="engine">配備先のエンジン種別。</param>
    public static EditFieldVisibility For(AgentEngineKind engine)
    {
        var includesClaude = engine != AgentEngineKind.Gemini;
        var includesGemini = engine != AgentEngineKind.Claude;
        return new EditFieldVisibility(includesClaude, includesClaude, includesGemini);
    }
}
