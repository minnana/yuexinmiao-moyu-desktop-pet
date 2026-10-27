using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace YueXinCat.Services;

public sealed class KeyboardEventArgs : EventArgs
{
    public KeyboardEventArgs(int virtualKey, bool isDown, bool isInjected)
    {
        VirtualKey = virtualKey;
        IsDown = isDown;
        IsInjected = isInjected;
        Key = KeyInterop.KeyFromVirtualKey(virtualKey);
        Label = ResolveLabel(virtualKey);
        Timestamp = DateTimeOffset.Now;
    }

    public int VirtualKey { get; }
    public Key Key { get; }
    public string Label { get; }
    public bool IsDown { get; }
    public bool IsInjected { get; }
    public DateTimeOffset Timestamp { get; }

    private static string ResolveLabel(int virtualKey)
    {
        var scanCode = MapVirtualKey((uint)virtualKey, 0);
        var lParam = (long)(scanCode << 16);
        var buffer = new StringBuilder(64);
        return GetKeyNameText(lParam, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : $"VK 0x{virtualKey:X2}";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(long lParam, StringBuilder text, int maxLength);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}

/// <summary>
/// Low-level Windows keyboard listener. It observes keys for the animation and
/// never sends synthetic input back to the OS. Create/dispose it on the WPF UI
/// thread so the hook callback is serviced by the application's message loop.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private readonly HookDelegate _callback;
    private IntPtr _handle;
    private bool _disposed;

    public KeyboardHook()
    {
        _callback = HookCallback;
    }

    public bool IsRunning => _handle != IntPtr.Zero;
    public event EventHandler<KeyboardEventArgs>? KeyChanged;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
            return true;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        _handle = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(module?.ModuleName), 0);
        return IsRunning;
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero)
            return;
        UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var message = wParam.ToInt32();
                if (message is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp)
                {
                    var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                    var isDown = message is WmKeyDown or WmSysKeyDown;
                    var injected = (data.Flags & 0x10) != 0;
                    KeyChanged?.Invoke(this, new KeyboardEventArgs((int)data.VirtualKeyCode, isDown, injected));
                }
            }
            catch
            {
                // A hook callback must not throw across the native boundary.
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    private delegate IntPtr HookDelegate(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookDelegate lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
