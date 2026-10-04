using System.IO;
using System.Text.Json;

namespace AgentDeskApp;

/// <summary>
/// <see cref="AppSettings"/>を%APPDATA%\AgentDeskApp\settings.jsonから読み書きするクラス。
/// ファイルが無い・壊れている場合は既定値にフォールバックする(起動時に落ちないようにするため)。
/// </summary>
public static class AppSettingsLoader
{
    /// <summary>設定ファイルの絶対パス。</summary>
    public static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentDeskApp", "settings.json");

    /// <summary>設定を読み込む。ファイルが無い、または解析に失敗した場合は<see cref="AppSettings.Default"/>を返す。</summary>
    public static AppSettings Load() => Load(SettingsFilePath);

    /// <summary>指定したパスから設定を読み込む(テスト用に公開)。</summary>
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return AppSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default;
        }
        catch (JsonException)
        {
            return AppSettings.Default;
        }
    }

    /// <summary>設定を書き出す(フォルダが無ければ作成する)。</summary>
    public static void Save(AppSettings settings) => Save(SettingsFilePath, settings);

    /// <summary>指定したパスへ設定を書き出す(テスト用に公開)。</summary>
    public static void Save(string path, AppSettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(path, json);
    }
}
