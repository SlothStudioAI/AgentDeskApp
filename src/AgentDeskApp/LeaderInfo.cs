namespace AgentDeskApp;

/// <summary>
/// CLAUDE.mdの「## リーダー」セクションから読み取った、部長/リーダーの名前・役割。
/// どちらも未設定の場合はnull(呼び出し元が既定値にフォールバックする)。
/// </summary>
/// <param name="Name">「名前:」行の値。</param>
/// <param name="Role">「役割:」行の値。部のCLAUDE.mdでのみ使う(グループ・チームでは役割は常に「リーダー」固定)。</param>
public sealed record LeaderInfo(string? Name, string? Role);
