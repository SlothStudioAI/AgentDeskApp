using System.IO;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="GroupRegistry"/>(CLAUDE.mdの「## グループ」セクション読み書き)に関するテスト。
/// </summary>
public class GroupRegistryTests
{
    [Fact]
    public void LoadGroups_ファイルが無ければ空を返す()
    {
        var path = Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid() + ".md");

        var groups = GroupRegistry.LoadGroups(path);

        Assert.Empty(groups);
    }

    [Fact]
    public void LoadGroups_セクションが無ければ空を返す()
    {
        var path = CreateTempFile("# 何かのルール\n本文だけ。\n");

        try
        {
            var groups = GroupRegistry.LoadGroups(path);
            Assert.Empty(groups);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadGroups_箇条書きを読み取れる()
    {
        var path = CreateTempFile("""
            # ルール

            ## グループ
            - C:\Work
            - D:\Projects

            ## 別のセクション
            関係ない本文。
            """);

        try
        {
            var groups = GroupRegistry.LoadGroups(path);
            Assert.Equal(["C:\\Work", "D:\\Projects"], groups);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveGroups_セクションが無いファイルには末尾に新設する()
    {
        var path = CreateTempFile("# 会社のルール\n既存の本文。\n");

        try
        {
            GroupRegistry.SaveGroups(path, ["C:\\Work"]);

            var content = File.ReadAllText(path);
            Assert.Contains("既存の本文。", content);
            Assert.Contains("## グループ", content);
            Assert.Contains("- C:\\Work", content);

            var reloaded = GroupRegistry.LoadGroups(path);
            Assert.Equal(["C:\\Work"], reloaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveGroups_既存セクションは中身だけ置き換える()
    {
        var path = CreateTempFile("""
            # ルール

            ## グループ
            - C:\Old

            ## 別のセクション
            残るべき本文。
            """);

        try
        {
            GroupRegistry.SaveGroups(path, ["C:\\New1", "C:\\New2"]);

            var reloaded = GroupRegistry.LoadGroups(path);
            Assert.Equal(["C:\\New1", "C:\\New2"], reloaded);

            var content = File.ReadAllText(path);
            Assert.Contains("残るべき本文。", content);
            Assert.DoesNotContain("C:\\Old", content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RemoveGroup_指定したパスがセクションから除外される()
    {
        var path = CreateTempFile("""
            # ルール

            ## グループ
            - C:\Keep1
            - C:\ToRemove
            - C:\Keep2

            ## 別のセクション
            本文。
            """);

        try
        {
            GroupRegistry.RemoveGroup(path, "C:\\ToRemove");

            var reloaded = GroupRegistry.LoadGroups(path);
            Assert.Equal(["C:\\Keep1", "C:\\Keep2"], reloaded);

            var content = File.ReadAllText(path);
            Assert.Contains("本文。", content);
            Assert.DoesNotContain("C:\\ToRemove", content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadWorkspaces_settingsに存在する場合はそれを優先する()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), "settings-" + Guid.NewGuid() + ".json");
        var mdPath = CreateTempFile("""
            ## グループ
            - C:\FromMd
            """);

        try
        {
            AppSettingsLoader.Save(settingsPath, new AppSettings(Workspaces: ["C:\\FromSettings"]));

            var workspaces = GroupRegistry.LoadWorkspaces(mdPath, settingsPath);
            Assert.Equal(["C:\\FromSettings"], workspaces);
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            File.Delete(mdPath);
        }
    }

    [Fact]
    public void LoadWorkspaces_settingsが未設定ならMDから読み込みsettingsに自動保存する()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), "settings-" + Guid.NewGuid() + ".json");
        var mdPath = CreateTempFile("""
            ## グループ
            - C:\FromMd1
            - C:\FromMd2
            """);

        try
        {
            // settings.json は空/Workspacesがnull
            AppSettingsLoader.Save(settingsPath, new AppSettings(Workspaces: null));

            var workspaces = GroupRegistry.LoadWorkspaces(mdPath, settingsPath);
            Assert.Equal(["C:\\FromMd1", "C:\\FromMd2"], workspaces);

            // settings.json に移行されて保存されたことを確認
            var reloadedSettings = AppSettingsLoader.Load(settingsPath);
            Assert.Equal(["C:\\FromMd1", "C:\\FromMd2"], reloadedSettings.Workspaces);
        }
        finally
        {
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            File.Delete(mdPath);
        }
    }


    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "group-registry-test-" + Guid.NewGuid() + ".md");
        File.WriteAllText(path, content);
        return path;
    }
}
