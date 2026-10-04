using System.IO;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// TEAM欄のリーダー(相棒)カード用の集約ロジック(<see cref="LeaderCardSummary"/>)と、
/// キャラ名の設定読み込み、会話の新しさの記録(<see cref="MemberActivityMonitor"/>)に関するテスト。
/// </summary>
public class LeaderCardSummaryTests
{
    private static readonly DateTime T0 = new(2026, 10, 3, 12, 0, 0);

    /// <summary>テスト用の会話(リーダーセッション)を作る。</summary>
    /// <param name="id">セッションID。</param>
    /// <param name="state">状態。</param>
    /// <param name="path">作業ファイルのパス。</param>
    /// <param name="engine">AI。</param>
    /// <param name="minutes">基準時刻からの経過分(大きいほど新しい)。</param>
    private static LeaderSessionInfo Session(string id, MemberActivityState state, string? path, AgentEngineKind engine, int minutes) =>
        new(id, state, path, engine, T0.AddMinutes(minutes));

    [Fact]
    public void 会話が無ければ待機で0件()
    {
        var summary = LeaderCardSummary.Summarize([], AgentEngineKind.Claude);

        Assert.Equal(MemberActivityState.Idle, summary.State);
        Assert.Null(summary.LatestSession);
        Assert.Equal(0, summary.SessionCount);
        Assert.Null(summary.BuildSessionCountText());
    }

    [Fact]
    public void どれか1つでも作業中なら作業中()
    {
        var summary = LeaderCardSummary.Summarize(
        [
            Session("a", MemberActivityState.Done, @"C:\w\a.cs", AgentEngineKind.Gemini, 5),
            Session("b", MemberActivityState.Running, @"C:\w\b.cs", AgentEngineKind.Gemini, 1),
        ], AgentEngineKind.Gemini);

        Assert.Equal(MemberActivityState.Running, summary.State);
    }

    [Fact]
    public void 作業中が無く完了直後があれば完了()
    {
        var summary = LeaderCardSummary.Summarize(
        [
            Session("a", MemberActivityState.Done, null, AgentEngineKind.Claude, 1),
            Session("b", MemberActivityState.Done, null, AgentEngineKind.Claude, 2),
        ], AgentEngineKind.Claude);

        Assert.Equal(MemberActivityState.Done, summary.State);
    }

    [Fact]
    public void 作業中はいちばん新しい会話のファイル名で会話件数も出る()
    {
        var summary = LeaderCardSummary.Summarize(
        [
            Session("old", MemberActivityState.Running, @"C:\w\old.cs", AgentEngineKind.Gemini, 1),
            Session("new", MemberActivityState.Done, @"C:\w\sub\new.md", AgentEngineKind.Gemini, 10),
            Session("mid", MemberActivityState.Done, @"C:\w\mid.cs", AgentEngineKind.Gemini, 5),
        ], AgentEngineKind.Gemini);

        Assert.Equal("new", summary.LatestSession!.SessionId);
        Assert.Equal(3, summary.SessionCount);
        Assert.Equal("会話3件", summary.BuildSessionCountText());
    }

    [Fact]
    public void 会話1件なら件数は添えない()
    {
        var summary = LeaderCardSummary.Summarize(
            [Session("a", MemberActivityState.Running, @"C:\w\a.cs", AgentEngineKind.Claude, 1)],
            AgentEngineKind.Claude);

        Assert.Equal(1, summary.SessionCount);
        Assert.Null(summary.BuildSessionCountText());
    }

    [Fact]
    public void 他のAIの会話は数えない()
    {
        var sessions = new[]
        {
            Session("c1", MemberActivityState.Done, @"C:\w\c.cs", AgentEngineKind.Claude, 1),
            Session("g1", MemberActivityState.Running, @"C:\w\g1.cs", AgentEngineKind.Gemini, 2),
            Session("g2", MemberActivityState.Done, @"C:\w\g2.cs", AgentEngineKind.Gemini, 3),
        };

        var claude = LeaderCardSummary.Summarize(sessions, AgentEngineKind.Claude);
        var gemini = LeaderCardSummary.Summarize(sessions, AgentEngineKind.Gemini);

        Assert.Equal(MemberActivityState.Done, claude.State);
        Assert.Equal(1, claude.SessionCount);
        Assert.Equal(MemberActivityState.Running, gemini.State);
        Assert.Equal(2, gemini.SessionCount);
        Assert.Equal(@"C:\w\g2.cs", gemini.LatestSession!.CurrentWorkPath);
    }

    [Fact]
    public void 時刻が同じなら作業中の会話を新しい方として扱う()
    {
        var summary = LeaderCardSummary.Summarize(
        [
            Session("a", MemberActivityState.Done, @"C:\w\done.cs", AgentEngineKind.Claude, 3),
            Session("b", MemberActivityState.Running, @"C:\w\running.cs", AgentEngineKind.Claude, 3),
        ], AgentEngineKind.Claude);

        Assert.Equal(@"C:\w\running.cs", summary.LatestSession!.CurrentWorkPath);
    }

    [Fact]
    public void 作業ファイルが分からなければセッションIDを出す()
    {
        var summary = LeaderCardSummary.Summarize(
            [Session("abc123", MemberActivityState.Running, null, AgentEngineKind.Claude, 1)],
            AgentEngineKind.Claude);

        Assert.Equal("abc123", summary.LatestSession!.SessionId);
    }

    [Theory]
    [InlineData(true, true, new[] { AgentEngineKind.Claude, AgentEngineKind.Gemini })]
    [InlineData(true, false, new[] { AgentEngineKind.Claude })]
    [InlineData(false, true, new[] { AgentEngineKind.Gemini })]
    [InlineData(false, false, new AgentEngineKind[0])]
    public void CLIが使えるAIだけカードを出す(bool claude, bool gemini, AgentEngineKind[] expected)
    {
        Assert.Equal(expected, LeaderCardSummary.GetVisibleEngines(claude, gemini));
    }

    [Fact]
    public void 同梱画像のファイル名とAI名()
    {
        Assert.Equal("leader-claude.jpg", LeaderCardSummary.GetAvatarFileName(AgentEngineKind.Claude));
        Assert.Equal("leader-gemini.jpg", LeaderCardSummary.GetAvatarFileName(AgentEngineKind.Gemini));
        Assert.Equal("Claude", LeaderCardSummary.GetEngineLabel(AgentEngineKind.Claude));
        Assert.Equal("Gemini", LeaderCardSummary.GetEngineLabel(AgentEngineKind.Gemini));
    }

    [Fact]
    public void キャラ名の既定はクローディとジェミリー()
    {
        var settings = AppSettings.Default;

        Assert.Equal("クローディ", LeaderCardSummary.GetCharacterName(settings, AgentEngineKind.Claude));
        Assert.Equal("ジェミリー", LeaderCardSummary.GetCharacterName(settings, AgentEngineKind.Gemini));
    }

    [Fact]
    public void キャラ名が空欄なら既定値で前後の空白は除く()
    {
        var settings = AppSettings.Default with { ClaudeLeaderCharacterName = "  ", GeminiLeaderCharacterName = " ジェミ " };

        Assert.Equal("クローディ", LeaderCardSummary.GetCharacterName(settings, AgentEngineKind.Claude));
        Assert.Equal("ジェミ", LeaderCardSummary.GetCharacterName(settings, AgentEngineKind.Gemini));
    }

    [Fact]
    public void Load_キャラ名を設定ファイルから読み込める()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-leader-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{ "ClaudeLeaderCharacterName": "トラ", "GeminiLeaderCharacterName": "オオカミ" }""");

            var settings = AppSettingsLoader.Load(path);

            Assert.Equal("トラ", LeaderCardSummary.GetCharacterName(settings, AgentEngineKind.Claude));
            Assert.Equal("オオカミ", LeaderCardSummary.GetCharacterName(settings, AgentEngineKind.Gemini));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_キャラ名のキーが無い古い設定ファイルでは既定値になる()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-leader-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """{ "SessionRefreshIntervalSeconds": 20 }""");

            var settings = AppSettingsLoader.Load(path);

            Assert.Equal(20, settings.SessionRefreshIntervalSeconds);
            Assert.Equal("クローディ", settings.EffectiveClaudeLeaderCharacterName);
            Assert.Equal("ジェミリー", settings.EffectiveGeminiLeaderCharacterName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 直近の待機会話は件数にだけ足され状態には影響しない()
    {
        var running = new LeaderSessionInfo("a", MemberActivityState.Running, null, AgentEngineKind.Claude, T0);

        var summary = LeaderCardSummary.Summarize([running], AgentEngineKind.Claude, recentIdleCount: 1);
        Assert.Equal(2, summary.SessionCount);
        Assert.Equal(MemberActivityState.Running, summary.State);
        Assert.Equal("会話2件", summary.BuildSessionCountText());

        // 作業中の会話が無く待機会話だけでも、2件以上なら件数を出す(状態は待機のまま)
        var idleOnly = LeaderCardSummary.Summarize([], AgentEngineKind.Claude, recentIdleCount: 2);
        Assert.Equal(MemberActivityState.Idle, idleOnly.State);
        Assert.Null(idleOnly.LatestSession);
        Assert.Equal("会話2件", idleOnly.BuildSessionCountText());
    }

    [Fact]
    public void 新設定は既定値が現状どおりで0以下なら既定に戻る()
    {
        var settings = AppSettings.Default;
        Assert.Equal(5, settings.EffectiveClaudeRecentConversationMinutes);
        Assert.Equal(5, settings.EffectiveGeminiRecentConversationMinutes);

        var bad = settings with { ClaudeRecentConversationMinutes = 0, GeminiRecentConversationMinutes = -1 };
        Assert.Equal(5, bad.EffectiveClaudeRecentConversationMinutes);
        Assert.Equal(5, bad.EffectiveGeminiRecentConversationMinutes);
    }

    [Fact]
    public void Claudeの会話は作業中を確認した時刻が新しさとして記録される()
    {
        var now = T0;
        var monitor = new MemberActivityMonitor { Clock = () => now };

        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "busy", null, "t")]);
        Assert.Equal(T0, monitor.GetLeaders(@"C:\Work")[0].LastActivityAt);

        // 作業中のまま時間が進めば更新される
        now = T0.AddMinutes(1);
        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "busy", null, "t")]);
        Assert.Equal(T0.AddMinutes(1), monitor.GetLeaders(@"C:\Work")[0].LastActivityAt);

        // 完了(idle)後は時刻を進めない
        now = T0.AddMinutes(5);
        monitor.RefreshBusySessions([new AgentSessionInfo("s1", @"C:\Work", "interactive", "idle", null, "t")]);
        var leader = monitor.GetLeaders(@"C:\Work")[0];
        Assert.Equal(MemberActivityState.Done, leader.State);
        Assert.Equal(T0.AddMinutes(1), leader.LastActivityAt);
    }

    [Fact]
    public void Geminiの会話はログの更新時刻が新しさになる()
    {
        var modified = T0.AddMinutes(7);
        var monitor = new MemberActivityMonitor
        {
            GeminiScanner = _ => [new GeminiSessionRecord("conversation-1234567890", @"C:\Work", MemberActivityState.Running, @"C:\Work\x.md", modified)],
        };

        var leader = Assert.Single(monitor.GetLeaders(@"C:\Work"));

        Assert.Equal(AgentEngineKind.Gemini, leader.Engine);
        Assert.Equal(modified, leader.LastActivityAt);
    }
}
