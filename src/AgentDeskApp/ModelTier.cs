namespace AgentDeskApp;

/// <summary>
/// エンジン(Claude / Gemini)に依存しないモデルの「段階」。テンプレート追加画面のモデル選択で使う。
/// </summary>
public enum ModelTier
{
    /// <summary>継承(呼び出し元と同じモデル)。</summary>
    Inherit,

    /// <summary>超軽量・最速(分類・ラベル付け向け)。</summary>
    UltraLight,

    /// <summary>高速・低コスト(標準バランス)。</summary>
    Standard,

    /// <summary>最高知能(複雑な設計・深い推論向け)。</summary>
    Highest,
}

/// <summary>
/// モデルの段階(<see cref="ModelTier"/>)と、Claude / Gemini 各エンジンのモデル名・表示ラベルの対応表を1か所に集約したクラス。
/// 対応はAgentEditWindowのモデル選択肢の説明(Claude: haiku=単純作業・高速 / sonnet=標準バランス / opus=じっくり深く考える、
/// Gemini: flash_lite=超軽量・最速 / flash=高速・低コスト(標準) / pro=最高知能)に沿う。
/// </summary>
public static class ModelTierMap
{
    /// <summary>段階の定義(表示順)。ラベル・Claudeモデル・Geminiモデルの対応表。</summary>
    private static readonly (ModelTier Tier, string Label, string Claude, string Gemini)[] Entries =
    [
        (ModelTier.Inherit, "継承", "inherit", "inherit"),
        (ModelTier.UltraLight, "超軽量・最速", "haiku", "flash_lite"),
        (ModelTier.Standard, "高速・低コスト(標準バランス)", "sonnet", "flash"),
        (ModelTier.Highest, "最高知能", "opus", "pro"),
    ];

    /// <summary>未知のClaudeモデルのときに使う段階。</summary>
    public const ModelTier DefaultTier = ModelTier.Standard;

    /// <summary>全段階を表示順で返す。</summary>
    public static IReadOnlyList<ModelTier> AllTiers { get; } = Entries.Select(e => e.Tier).ToArray();

    /// <summary>段階の表示ラベルを返す。</summary>
    /// <param name="tier">段階。</param>
    public static string Label(ModelTier tier) => Find(tier).Label;

    /// <summary>段階に対応するClaudeモデル名を返す。</summary>
    /// <param name="tier">段階。</param>
    public static string ClaudeModel(ModelTier tier) => Find(tier).Claude;

    /// <summary>段階に対応するGeminiモデル名を返す。</summary>
    /// <param name="tier">段階。</param>
    public static string GeminiModel(ModelTier tier) => Find(tier).Gemini;

    /// <summary>段階の説明(ツールチップ用。Claude / Geminiの各モデル名を示す)。</summary>
    /// <param name="tier">段階。</param>
    public static string Tooltip(ModelTier tier)
    {
        var e = Find(tier);
        return $"{e.Label}\nClaude: {e.Claude} / Gemini: {e.Gemini}";
    }

    /// <summary>
    /// Claudeモデル名(テンプレートの推奨モデル等)から段階を求める。
    /// </summary>
    /// <param name="claudeModel">Claudeモデル名(opus/sonnet/haiku/inherit)。</param>
    /// <returns>対応する段階。haikuは超軽量・最速、未知・空は高速・低コスト(標準バランス)。</returns>
    public static ModelTier FromClaudeModel(string? claudeModel)
    {
        return claudeModel?.Trim().ToLowerInvariant() switch
        {
            "inherit" => ModelTier.Inherit,
            "haiku" => ModelTier.UltraLight,
            "sonnet" => ModelTier.Standard,
            "opus" => ModelTier.Highest,
            _ => DefaultTier,
        };
    }

    /// <summary>表示ラベルから段階を求める(見つからなければnull)。</summary>
    /// <param name="label">表示ラベル。</param>
    public static ModelTier? FromLabel(string? label) =>
        Entries.Where(e => e.Label == label).Select(e => (ModelTier?)e.Tier).FirstOrDefault();

    /// <summary>段階の定義を取得する。</summary>
    private static (ModelTier Tier, string Label, string Claude, string Gemini) Find(ModelTier tier) =>
        Entries.First(e => e.Tier == tier);
}
