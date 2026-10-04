using System.IO;
using AgentDeskApp;
using Xunit;

namespace AgentDeskApp.Tests;

/// <summary><see cref="AgentCloneAvatarCopier"/>(複製時の画像引き継ぎ)のテスト。一時ディレクトリを使う。</summary>
public sealed class AgentCloneAvatarCopierTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AgentCloneAvatarCopierTests_" + Guid.NewGuid().ToString("N"));
    private string ClaudeDir => Path.Combine(_root, ".claude", "agents");
    private string BundledDir => Path.Combine(_root, "Assets", "Avatars");

    public AgentCloneAvatarCopierTests()
    {
        Directory.CreateDirectory(ClaudeDir);
        Directory.CreateDirectory(BundledDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static AgentDefinition Source(string id, string? avatarPath, bool disabled = false) =>
        new(id, "desc", null, null, null, "body", AgentScope.Team, "x", "表示名", avatarPath, AvatarDisabled: disabled);

    private AgentDeployPlan ClaudePlan(string id) =>
        new(AgentEngineKind.Claude, Path.Combine(ClaudeDir, id + ".md"), null, null, []);

    [Fact]
    public void ユーザー設定の画像は複製先のId名でコピーし元は残す()
    {
        var src = Path.Combine(ClaudeDir, "qa.png");
        File.WriteAllText(src, "user-image");

        var copied = AgentCloneAvatarCopier.CopyForClone(Source("qa", src), ClaudePlan("qa-copy"), ClaudeDir, "qa-copy");

        Assert.True(copied);
        Assert.Equal("user-image", File.ReadAllText(Path.Combine(ClaudeDir, "qa-copy.png")));
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void Gemini配置では新Id名とavatar名の両方へコピーする()
    {
        var src = Path.Combine(ClaudeDir, "custom.jpg");
        File.WriteAllText(src, "img");
        var skillDir = Path.Combine(_root, ".agents", "skills", "qa-copy");
        var plan = new AgentDeployPlan(AgentEngineKind.Gemini, null, skillDir, Path.Combine(skillDir, "SKILL.md"), []);

        AgentCloneAvatarCopier.CopyForClone(Source("qa", src), plan, ClaudeDir, "qa-copy");

        Assert.True(File.Exists(Path.Combine(skillDir, "qa-copy.jpg")));
        Assert.True(File.Exists(Path.Combine(skillDir, "avatar.jpg")));
    }

    [Fact]
    public void 同梱画像のみの場合は同梱画像を複製先にコピーし同梱画像は残す()
    {
        var bundled = Path.Combine(BundledDir, "qa.jpg");
        File.WriteAllText(bundled, "bundled");

        var copied = AgentCloneAvatarCopier.CopyForClone(Source("qa", bundled), ClaudePlan("qa-copy"), ClaudeDir, "qa-copy");

        Assert.True(copied);
        Assert.Equal("bundled", File.ReadAllText(Path.Combine(ClaudeDir, "qa-copy.jpg")));
        Assert.True(File.Exists(bundled));
    }

    [Fact]
    public void 画像なしの場合は何もコピーしない()
    {
        Assert.False(AgentCloneAvatarCopier.CopyForClone(Source("qa", null), ClaudePlan("qa-copy"), ClaudeDir, "qa-copy"));
        Assert.False(AgentCloneAvatarCopier.CopyForClone(
            Source("qa", Path.Combine(ClaudeDir, "missing.png"), disabled: true), ClaudePlan("qa-copy"), ClaudeDir, "qa-copy"));
        Assert.Empty(Directory.GetFiles(ClaudeDir));
    }
}
