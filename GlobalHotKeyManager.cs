/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Elektra.UI.Interop;

public sealed partial class GlobalHotKeyManager(Window window, Action onPressed, int hotKeyId = 0xE1EC) : IDisposable
{
    private const int WmHotKey = 0x0312;
    private HwndSource? _source;
    private bool _registered;

    public bool RegisterWinAltE()
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        _registered = RegisterHotKey(
            hwnd,
            hotKeyId,
            0x0008 | 0x0001 | 0x4000, // MOD_WIN | MOD_ALT | MOD_NOREPEAT
            (uint)KeyInterop.VirtualKeyFromKey(Key.E));
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == hotKeyId)
        {
            onPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        _source.RemoveHook(WndProc);
        if (_registered && _source.Handle != IntPtr.Zero)
            UnregisterHotKey(_source.Handle, hotKeyId);
        _source = null;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}