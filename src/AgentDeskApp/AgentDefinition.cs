namespace AgentDeskApp;

/// <summary>
/// エージェントが対応しているエンジン種別。
/// </summary>
public enum AgentEngineKind
{
    /// <summary>Claude Code専用(.claude/agents/配下のみに存在)。</summary>
    Claude,

    /// <summary>Gemini / Antigravity専用(.agents/skills/配下のみに存在)。</summary>
    Gemini,

    /// <summary>ClaudeとGeminiの両方に対応・共有されているエージェント。</summary>
    Shared,
}

/// <summary>
/// サブエージェント定義の置き場所の区分。部(グローバル)→グループ(ユーザー登録の作業ルート)→
/// チーム(グループ直下のプロジェクトフォルダ、自動探索)の3階層に対応する(design.md §1参照)。
/// </summary>
public enum AgentScope
{
    /// <summary>ユーザーホームの.claude/agents/に置かれた定義(部直轄、全プロジェクト共通)。</summary>
    Global,

    /// <summary>グループ(ユーザーが登録した作業ルート)直下の.claude/agents/に置かれた定義。</summary>
    Group,

    /// <summary>チーム(グループ直下のプロジェクトフォルダ)直下の.claude/agents/に置かれた定義。</summary>
    Team,
}

/// <summary>
/// 1件のサブエージェント定義(.claude/agents/配下の.mdファイルまたは.agents/skills/配下のSKILL.md)。
/// Claude CodeおよびGemini(Antigravity)のハイブリッド表示に対応する。
/// </summary>
/// <param name="Name">frontmatterのnameフィールド(一意の識別子)。</param>
/// <param name="Description">frontmatterのdescriptionフィールド(委譲判断に使う説明文)。</param>
/// <param name="Tools">frontmatterのtoolsフィールド(使用可能ツールの一覧)。未指定ならnull。</param>
/// <param name="Model">frontmatterのmodelフィールド(sonnet/opus等)。未指定ならnull。</param>
/// <param name="Color">frontmatterのcolorフィールド(表示色)。未指定ならnull。</param>
/// <param name="Body">frontmatter区切り(---)より後の本文。個性・行動ルールを表す。</param>
/// <param name="Scope">部/グループ/チームの区分。</param>
/// <param name="FilePath">読み込んだ.mdファイルの絶対パス。</param>
/// <param name="DisplayName">
/// frontmatterの<c>displayName</c>フィールド(AgentDeskApp独自の拡張項目、公式仕様には無い)。
/// 画面上の表示にのみ使い、<see cref="Name"/>(subagent_type等に使われる識別子)には影響しない。
/// 未指定ならnull(その場合は<see cref="EffectiveDisplayName"/>が<see cref="Name"/>にフォールバックする)。
/// </param>
/// <param name="AvatarPath">解決済みのアバター画像絶対パス。未指定ならnull。</param>
/// <param name="Engine">対応エンジン種別(Claude / Gemini / Shared)。既定値はClaude。</param>
/// <param name="GeminiSkillPath">共有エージェントの場合のGemini側SKILL.mdパス。未指定ならnull。</param>
/// <param name="GeminiModel">
/// Sharedエージェントの場合のGemini側モデル値(flash/pro/flash_lite/inherit)。未指定ならnull (U-23)。
/// Engine=Geminiの単独エージェントでは<see cref="Model"/>にそのままGemini側の値が入るため使わない。
/// </param>
/// <param name="ClaudeModel">
/// Gemini専用エージェントの場合に、SKILL.mdの補助キー(claudeModel)へ退避してあるClaude側モデル値。
/// 配備先をGemini専用にしてもClaude側の選択を失わないための保持用。未指定ならnull。
/// </param>
/// <param name="AvatarDisabled">
/// frontmatterに「画像なし」の印(<c>avatar: none</c>)があるならtrue。
/// このとき<see cref="AvatarPath"/>はnull(同名画像・同梱画像も使わず頭文字表示)になる。
/// </param>
public sealed record AgentDefinition(
    string Name,
    string Description,
    string? Tools,
    string? Model,
    string? Color,
    string Body,
    AgentScope Scope,
    string FilePath,
    string? DisplayName = null,
    string? AvatarPath = null,
    AgentEngineKind Engine = AgentEngineKind.Claude,
    string? GeminiSkillPath = null,
    string? GeminiModel = null,
    bool AvatarDisabled = false,
    string? ClaudeModel = null)
{
    /// <summary>画面表示に使う名前。DisplayName未指定時はNameにフォールバックする。</summary>
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
}
