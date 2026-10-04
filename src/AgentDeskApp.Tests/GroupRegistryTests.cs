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

    /// <summary>「## グループ会社の方針」のような利用者自身の節は置き換えず、別に「## グループ」節を末尾へ新設する。</summary>
    [Fact]
    public void SaveGroups_グループ会社の方針の節は置き換えずに保持する()
    {
        var path = CreateTempFile("# ルール\n\n## グループ会社の方針\n- 親会社の承認を得る\n- 子会社へ共有する\n");

        try
        {
            GroupRegistry.SaveGroups(path, ["C:\\Work"]);

            var content = File.ReadAllText(path);
            Assert.Contains("## グループ会社の方針", content);
            Assert.Contains("- 親会社の承認を得る", content);
            Assert.Contains("- 子会社へ共有する", content);
            Assert.Equal(["C:\\Work"], GroupRegistry.LoadGroups(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>「## グループ会社の方針」節と通常の「## グループ」節が併存するとき、後者だけが更新される。</summary>
    [Fact]
    public void SaveGroups_通常のグループ節だけが更新され会社の方針の節は残る()
    {
        var path = CreateTempFile("""
            ## グループ会社の方針
            - 方針A

            ## グループ
            - C:\Old

            ## 別のセクション
            残る本文。
            """);

        try
        {
            GroupRegistry.SaveGroups(path, ["C:\\New"]);

            var content = File.ReadAllText(path);
            Assert.Contains("- 方針A", content);
            Assert.Contains("残る本文。", content);
            Assert.DoesNotContain("C:\\Old", content);
            Assert.Equal(["C:\\New"], GroupRegistry.LoadGroups(path));
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
