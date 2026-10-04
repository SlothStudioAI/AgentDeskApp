using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AgentDeskApp;

/// <summary>
/// ウィンドウのタスクバーアイコンを点滅させるクラス(Win32 FlashWindowEx を使用)。
/// アプリが前面にないときだけ点滅させ、ユーザーがアプリを前面に戻すと自動で止まる。
/// </summary>
public static class TaskbarFlasher
{
    /// <summary>FlashWindowEx に渡す点滅指定の構造体。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint CbSize;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    /// <summary>キャプションとタスクバーボタンの両方を点滅させる。</summary>
    private const uint FlashAll = 0x00000003;

    /// <summary>ウィンドウが前面に来るまで点滅を続ける。</summary>
    private const uint FlashTimerNoForeground = 0x0000000C;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    /// <summary>
    /// ウィンドウがアクティブでない場合に限り、タスクバーのアイコンを点滅させる。
    /// 失敗しても例外は出さない(通知の補助機能のため)。
    /// </summary>
    /// <param name="window">点滅させる対象のウィンドウ。</param>
    public static void FlashIfInactive(Window window)
    {
        if (window.IsActive)
        {
            return;
        }

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var info = new FlashWindowInfo
            {
                CbSize = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                Hwnd = hwnd,
                Flags = FlashAll | FlashTimerNoForeground,
                Count = 0,
                Timeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AgentDesk] タスクバー点滅に失敗: {ex.Message}");
        }
    }
}
