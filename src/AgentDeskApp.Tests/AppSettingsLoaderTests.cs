using System.IO;
using AgentDeskApp;

namespace AgentDeskApp.Tests;

/// <summary>
/// <see cref="AppSettingsLoader"/>(設定ファイルの読み書き)に関するテスト。
/// </summary>
public class AppSettingsLoaderTests
{
    [Fact]
    public void Load_ファイルが無ければ既定値を返す()
    {
        var path = Path.Combine(Path.GetTempPath(), "not-exist-" + Guid.NewGuid() + ".json");

        var settings = AppSettingsLoader.Load(path);

        Assert.Equal(AppSettings.DefaultSessionRefreshIntervalSeconds, settings.SessionRefreshIntervalSeconds);
        Assert.Equal(AppSettings.DefaultLogPollIntervalSeconds, settings.LogPollIntervalSeconds);
    }

    [Fact]
    public void Load_壊れたJSONなら既定値を返す()
    {
        var path = CreateTempFile("{ 壊れたJSON");
        try
        {
            var settings = AppSettingsLoader.Load(path);

            Assert.Equal(AppSettings.Default, settings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_旧キーGeminiRecentConversationSecondsは無視され既定の5分になる()
    {
        var path = CreateTempFile("{ \"GeminiRecentConversationSeconds\": 180 }");
        try
        {
            var settings = AppSettingsLoader.Load(path);

            Assert.Equal(5, settings.EffectiveGeminiRecentConversationMinutes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveしてLoadすると同じ値が戻る()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-test-" + Guid.NewGuid() + ".json");
        try
        {
            var original = new AppSettings(15, 5);
            AppSettingsLoader.Save(path, original);

            var loaded = AppSettingsLoader.Load(path);

            Assert.Equal(original, loaded);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void With式による設定更新でWorkspaces等の未編集フィールドが保持される()
    {
        var original = new AppSettings(
            SessionRefreshIntervalSeconds: 30,
            LogPollIntervalSeconds: 5,
            ClaudeCliPath: "claude",
            GeminiCliPath: "agy",
            Workspaces: new[] { @"C:\Studio", @"C:\OtherWork" },
            MaxTaskHistoryRecords: 50,
            TaskHistoryCacheSeconds: 15,
            TaskHistoryMaxScanFiles: 10);

        // SettingsWindow でCLIパスや周期を変更した際のシミュレーション (with式)
        var updated = original with
        {
            SessionRefreshIntervalSeconds = 15,
            GeminiCliPath = @"C:\Custom\agy.exe",
        };

        Assert.Equal(15, updated.SessionRefreshIntervalSeconds);
        Assert.Equal(@"C:\Custom\agy.exe", updated.GeminiCliPath);
        Assert.NotNull(updated.Workspaces);
        Assert.Equal(2, updated.Workspaces!.Count);
        Assert.Equal(@"C:\Studio", updated.Workspaces[0]);
        Assert.Equal(50, updated.MaxTaskHistoryRecords);
        Assert.Equal(10, updated.TaskHistoryMaxScanFiles);
    }

    [Fact]
    public void Default_完了通知と呼びかけ文の既定値()
    {
        var settings = AppSettings.Default;

        Assert.False(settings.CompletionSoundEnabled); // 完了音は既定でオフ(ユーザー決定)
        Assert.Null(settings.CompletionSoundPath);
        Assert.True(settings.AvatarBounceEnabled);
        Assert.True(settings.TaskbarFlashEnabled);
        Assert.Equal(0.22, settings.CompletionBounceAmplitude); // 旧既定0.12の約2倍(弱いという感想を受けて強化)
        Assert.Equal(5, settings.CompletionBounceCount); // 3回→5回(ユーザーの実機確認を受けて)
        Assert.Equal(700, settings.CompletionBounceDurationMilliseconds);
        Assert.True(settings.RunningSwayEnabled);
        Assert.Equal(1.5, settings.RunningSwayAngleDegrees); // 1.0→1.5(もっとぷにぷにに)
        Assert.Equal(2800, settings.RunningSwayPeriodMilliseconds);
        Assert.Equal(0.045, settings.RunningSwayBreathAmplitude); // 0.012→0.045(もっとぷにぷにに)
        Assert.Equal("#22C55E", settings.CompletionGlowColor);
        Assert.Equal(3, settings.CompletionGlowThickness);
        Assert.Equal(22, settings.CompletionGlowBlurRadius);
        Assert.Equal(1800, settings.CompletionGlowPulseMilliseconds);
        Assert.Equal(2000, settings.CopyFeedbackDurationMilliseconds);
        Assert.Equal("{name} エージェント（{displayName}）に、次の作業を頼んでください：", settings.CallPhraseTemplate);
        Assert.Equal("{name} エージェントに、次の作業を頼んでください：", settings.CallPhraseTemplateWithoutDisplayName);
    }

    [Fact]
    public void Load_新しいキーが無い古いsettings_jsonでも既定値で補われ既存値は保たれる()
    {
        // 今回の機能追加より前の形式(完了通知・呼びかけ文のキーが無い)
        var path = CreateTempFile("""
            {
              "SessionRefreshIntervalSeconds": 20,
              "LogPollIntervalSeconds": 3,
              "ClaudeCliPath": null,
              "GeminiCliPath": null,
              "Workspaces": [ "C:\\Studio" ],
              "MaxTaskHistoryRecords": 50,
              "TaskHistoryCacheSeconds": 15,
              "TaskHistoryMaxScanFiles": 10
            }
            """);
        try
        {
            var settings = AppSettingsLoader.Load(path);

            Assert.Equal(20, settings.SessionRefreshIntervalSeconds);
            Assert.Equal(3, settings.LogPollIntervalSeconds);
            Assert.Equal(@"C:\Studio", Assert.Single(settings.Workspaces!));
            Assert.False(settings.CompletionSoundEnabled); // キーが無ければ完了音はオフで補われる
            Assert.Null(settings.CompletionSoundPath);
            Assert.True(settings.AvatarBounceEnabled);
            Assert.True(settings.TaskbarFlashEnabled);
            Assert.Equal(AppSettings.DefaultCompletionBounceAmplitude, settings.CompletionBounceAmplitude);
            Assert.Equal(AppSettings.DefaultCompletionBounceCount, settings.CompletionBounceCount);
            Assert.True(settings.RunningSwayEnabled);
            Assert.Equal(AppSettings.DefaultCompletionGlowColor, settings.EffectiveCompletionGlowColor);
            Assert.Equal(AppSettings.DefaultCopyFeedbackDurationMilliseconds, settings.CopyFeedbackDurationMilliseconds);
            Assert.Equal(AppSettings.DefaultCallPhraseTemplate, settings.CallPhraseTemplate);
            Assert.Equal(AppSettings.DefaultCallPhraseTemplateWithoutDisplayName, settings.CallPhraseTemplateWithoutDisplayName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_完了通知の設定と呼びかけ文ひな形を読み込める()
    {
        var path = CreateTempFile("""
            {
              "CompletionSoundEnabled": true,
              "CompletionSoundPath": "C:\\sounds\\done.wav",
              "AvatarBounceEnabled": false,
              "TaskbarFlashEnabled": false,
              "CallPhraseTemplate": "{displayName}({name})へ：",
              "CopyFeedbackDurationMilliseconds": 3500
            }
            """);
        try
        {
            var settings = AppSettingsLoader.Load(path);

            Assert.True(settings.CompletionSoundEnabled); // 既定はオフだが、明示的にオンにした値は読み込まれる
            Assert.Equal(@"C:\sounds\done.wav", settings.CompletionSoundPath);
            Assert.False(settings.AvatarBounceEnabled);
            Assert.False(settings.TaskbarFlashEnabled);
            Assert.Equal("{displayName}({name})へ：", settings.EffectiveCallPhraseTemplate);
            Assert.Equal(3500, settings.EffectiveCopyFeedbackDurationMilliseconds);
            // 書かれていない項目は既定値
            Assert.Equal(AppSettings.DefaultSessionRefreshIntervalSeconds, settings.SessionRefreshIntervalSeconds);
            Assert.Equal(AppSettings.DefaultCallPhraseTemplateWithoutDisplayName, settings.EffectiveCallPhraseTemplateWithoutDisplayName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Effective値_不正な数値や空のひな形は既定値に置き換わる()
    {
        var settings = AppSettings.Default with
        {
            CompletionBounceDurationMilliseconds = 0,
            CompletionBounceAmplitude = -1,
            CopyFeedbackDurationMilliseconds = -5,
            CallPhraseTemplate = " ",
            CallPhraseTemplateWithoutDisplayName = null,
        };

        Assert.Equal(AppSettings.DefaultCompletionBounceDurationMilliseconds, settings.EffectiveCompletionBounceDurationMilliseconds);
        Assert.Equal(AppSettings.DefaultCompletionBounceAmplitude, settings.EffectiveCompletionBounceAmplitude);
        Assert.Equal(AppSettings.DefaultCopyFeedbackDurationMilliseconds, settings.EffectiveCopyFeedbackDurationMilliseconds);
        Assert.Equal(AppSettings.DefaultCallPhraseTemplate, settings.EffectiveCallPhraseTemplate);
        Assert.Equal(AppSettings.DefaultCallPhraseTemplateWithoutDisplayName, settings.EffectiveCallPhraseTemplateWithoutDisplayName);
    }

    [Fact]
    public void 完了通知の設定はSaveしてLoadしても保たれる()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-test-" + Guid.NewGuid() + ".json");
        try
        {
            var original = AppSettings.Default with
            {
                CompletionSoundEnabled = true, // 既定(オフ)と異なる値で保存されることを確認する
                AvatarBounceEnabled = false,
                TaskbarFlashEnabled = false,
            };
            AppSettingsLoader.Save(path, original);

            var loaded = AppSettingsLoader.Load(path);

            Assert.True(loaded.CompletionSoundEnabled);
            Assert.False(loaded.AvatarBounceEnabled);
            Assert.False(loaded.TaskbarFlashEnabled);
            Assert.Equal(AppSettings.DefaultCallPhraseTemplate, loaded.CallPhraseTemplate);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_旧キーAvatarBounceAmplitudeが保存された設定でも新しい強さの既定値になる()
    {
        // 前回の機能追加後に保存された形式(旧キーと、読み取り専用の Effective 値も書き出されている)
        var path = CreateTempFile("""
            {
              "SessionRefreshIntervalSeconds": 15,
              "AvatarBounceEnabled": true,
              "AvatarBounceDurationMilliseconds": 800,
              "AvatarBounceAmplitude": 0.12,
              "TaskbarFlashEnabled": true,
              "EffectiveAvatarBounceDurationMilliseconds": 800,
              "EffectiveAvatarBounceAmplitude": 0.12
            }
            """);
        try
        {
            var settings = AppSettingsLoader.Load(path);

            Assert.Equal(15, settings.SessionRefreshIntervalSeconds);
            Assert.True(settings.AvatarBounceEnabled);
            Assert.Equal(AppSettings.DefaultCompletionBounceAmplitude, settings.EffectiveCompletionBounceAmplitude);
            Assert.Equal(AppSettings.DefaultCompletionBounceCount, settings.EffectiveCompletionBounceCount);
            Assert.Equal(AppSettings.DefaultCompletionBounceDurationMilliseconds, settings.EffectiveCompletionBounceDurationMilliseconds);
            Assert.True(settings.RunningSwayEnabled);
            Assert.Equal(AppSettings.DefaultRunningSwayAngleDegrees, settings.EffectiveRunningSwayAngleDegrees);
            Assert.Equal(AppSettings.DefaultCompletionGlowColor, settings.EffectiveCompletionGlowColor);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_作業中の揺れと完了の演出の設定を読み込める()
    {
        var path = CreateTempFile("""
            {
              "RunningSwayEnabled": false,
              "RunningSwayAngleDegrees": 2.5,
              "RunningSwayPeriodMilliseconds": 4000,
              "RunningSwayBreathAmplitude": 0,
              "CompletionBounceAmplitude": 0.3,
              "CompletionBounceCount": 5,
              "CompletionBounceDurationMilliseconds": 500,
              "CompletionGlowColor": "#F59E0B",
              "CompletionGlowThickness": 4,
              "CompletionGlowBlurRadius": 30,
              "CompletionGlowPulseMilliseconds": 0
            }
            """);
        try
        {
            var settings = AppSettingsLoader.Load(path);

            Assert.False(settings.RunningSwayEnabled);
            Assert.Equal(2.5, settings.EffectiveRunningSwayAngleDegrees);
            Assert.Equal(4000, settings.EffectiveRunningSwayPeriodMilliseconds);
            Assert.Equal(0, settings.EffectiveRunningSwayBreathAmplitude); // 0 は「伸び縮みしない」として有効
            Assert.Equal(0.3, settings.EffectiveCompletionBounceAmplitude);
            Assert.Equal(5, settings.EffectiveCompletionBounceCount);
            Assert.Equal(500, settings.EffectiveCompletionBounceDurationMilliseconds);
            Assert.Equal("#F59E0B", settings.EffectiveCompletionGlowColor);
            Assert.Equal(4, settings.EffectiveCompletionGlowThickness);
            Assert.Equal(30, settings.EffectiveCompletionGlowBlurRadius);
            Assert.Equal(0, settings.EffectiveCompletionGlowPulseMilliseconds); // 0 は「明滅しない」として有効
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Effective値_作業中の揺れと完了の演出の不正値は既定値に置き換わる()
    {
        var settings = AppSettings.Default with
        {
            RunningSwayAngleDegrees = 30,
            RunningSwayPeriodMilliseconds = 10,
            RunningSwayBreathAmplitude = -0.1,
            CompletionBounceAmplitude = 0.9,
            CompletionBounceCount = 0,
            CompletionBounceDurationMilliseconds = 99999,
            CompletionGlowThickness = 0,
            CompletionGlowBlurRadius = -3,
            CompletionGlowPulseMilliseconds = -1,
        };

        Assert.Equal(AppSettings.DefaultRunningSwayAngleDegrees, settings.EffectiveRunningSwayAngleDegrees);
        Assert.Equal(AppSettings.DefaultRunningSwayPeriodMilliseconds, settings.EffectiveRunningSwayPeriodMilliseconds);
        Assert.Equal(AppSettings.DefaultRunningSwayBreathAmplitude, settings.EffectiveRunningSwayBreathAmplitude);
        Assert.Equal(AppSettings.DefaultCompletionBounceAmplitude, settings.EffectiveCompletionBounceAmplitude);
        Assert.Equal(AppSettings.DefaultCompletionBounceCount, settings.EffectiveCompletionBounceCount);
        Assert.Equal(AppSettings.DefaultCompletionBounceDurationMilliseconds, settings.EffectiveCompletionBounceDurationMilliseconds);
        Assert.Equal(AppSettings.DefaultCompletionGlowThickness, settings.EffectiveCompletionGlowThickness);
        Assert.Equal(AppSettings.DefaultCompletionGlowBlurRadius, settings.EffectiveCompletionGlowBlurRadius);
        Assert.Equal(AppSettings.DefaultCompletionGlowPulseMilliseconds, settings.EffectiveCompletionGlowPulseMilliseconds);
    }

    [Theory]
    [InlineData("#22C55E", "#22C55E")]
    [InlineData(" #8022C55E ", "#8022C55E")] // 前後の空白は取り除き、透明度付きも使える
    [InlineData("green", AppSettings.DefaultCompletionGlowColor)]
    [InlineData("#12345", AppSettings.DefaultCompletionGlowColor)]
    [InlineData("#GGGGGG", AppSettings.DefaultCompletionGlowColor)]
    [InlineData("", AppSettings.DefaultCompletionGlowColor)]
    [InlineData(null, AppSettings.DefaultCompletionGlowColor)]
    public void EffectiveCompletionGlowColor_色の形式でなければ既定色になる(string? configured, string expected)
    {
        var settings = AppSettings.Default with { CompletionGlowColor = configured };

        Assert.Equal(expected, settings.EffectiveCompletionGlowColor);
    }

    [Fact]
    public void 作業中の揺れの設定はSaveしてLoadしても保たれる()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-test-" + Guid.NewGuid() + ".json");
        try
        {
            AppSettingsLoader.Save(path, AppSettings.Default with { RunningSwayEnabled = false });

            Assert.False(AppSettingsLoader.Load(path).RunningSwayEnabled);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-test-" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Save_Effectiveで始まる計算結果は書き出さず保存内容は保持される()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-save-" + Guid.NewGuid() + ".json");
        try
        {
            var settings = AppSettings.Default with
            {
                Workspaces = new[] { @"C:\Studio", @"C:\OtherWork" },
                ClaudeCliPath = "claude",
                CallPhraseTemplate = "{name}さん、お願いします：",
            };

            AppSettingsLoader.Save(path, settings);

            // 保存したJSONの最上位キーに「Effective」で始まるものが無いこと
            using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)))
            {
                var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
                Assert.DoesNotContain(keys, k => k.StartsWith("Effective", StringComparison.Ordinal));
                Assert.Contains("Workspaces", keys);
                Assert.Contains("RunningSwayBreathAmplitude", keys);
            }

            // 読み直しても保存内容(ワークスペース一覧など)が失われないこと
            var loaded = AppSettingsLoader.Load(path);
            Assert.Equal(new[] { @"C:\Studio", @"C:\OtherWork" }, loaded.Workspaces);
            Assert.Equal("claude", loaded.ClaudeCliPath);
            Assert.Equal("{name}さん、お願いします：", loaded.CallPhraseTemplate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_古いファイルに残ったEffectiveキーは無視される()
    {
        var path = Path.Combine(Path.GetTempPath(), "settings-old-effective-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, """
            {
              "Workspaces": [ "C:\\Studio" ],
              "CallPhraseTemplate": null,
              "EffectiveCallPhraseTemplate": "古い計算結果",
              "EffectiveAvatarBounceAmplitude": 0.12,
              "EffectiveClaudeCliPath": "C:\\old\\claude.exe",
              "EffectiveRunningSwayBreathAmplitude": 0.012
            }
            """);

            var settings = AppSettingsLoader.Load(path);

            // 読み込みに失敗して既定値へ戻るのではなく、保存値は読めていること
            Assert.Equal(new[] { @"C:\Studio" }, settings.Workspaces);
            // Effective値は古いキーではなく、保存値(ここでは未設定)から計算し直されること
            Assert.Equal(AppSettings.DefaultCallPhraseTemplate, settings.EffectiveCallPhraseTemplate);
            Assert.Equal(AppSettings.DefaultRunningSwayBreathAmplitude, settings.EffectiveRunningSwayBreathAmplitude);
            Assert.Null(settings.ClaudeCliPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Default_演出の既定値は範囲チェックで置き換えられない()
    {
        // 既定値そのものが「範囲外なら既定値に置き換え」の範囲内であること(既定値を変えたときの確認用)
        var settings = AppSettings.Default;

        Assert.Equal(AppSettings.DefaultRunningSwayAngleDegrees, settings.EffectiveRunningSwayAngleDegrees);
        Assert.Equal(AppSettings.DefaultRunningSwayPeriodMilliseconds, settings.EffectiveRunningSwayPeriodMilliseconds);
        Assert.Equal(AppSettings.DefaultRunningSwayBreathAmplitude, settings.EffectiveRunningSwayBreathAmplitude);
        Assert.Equal(AppSettings.DefaultCompletionBounceAmplitude, settings.EffectiveCompletionBounceAmplitude);
        Assert.Equal(AppSettings.DefaultCompletionBounceCount, settings.EffectiveCompletionBounceCount);
        Assert.Equal(AppSettings.DefaultCompletionBounceDurationMilliseconds, settings.EffectiveCompletionBounceDurationMilliseconds);
    }
}
