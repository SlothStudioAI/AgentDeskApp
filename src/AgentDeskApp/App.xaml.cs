using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;

namespace AgentDeskApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"予期せぬエラーが発生しました:\n{args.Exception.Message}\n\n詳細:\n{args.Exception}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        const string appName = "AgentDeskApp_SingleInstance_Mutex_2026";
        _mutex = new Mutex(true, appName, out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "AgentDesk Studio は既に起動しています。\nタスクバーまたは既存のウィンドウをご確認ください。",
                "二重起動防止",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // すでに解放されている場合は無視
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

