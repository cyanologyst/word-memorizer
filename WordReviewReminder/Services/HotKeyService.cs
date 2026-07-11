using System.Runtime.InteropServices;

namespace WordReviewReminder.Services;

public sealed class HotKeyService : System.Windows.Forms.NativeWindow, IDisposable
{
    private const int HotKeyId = 0x5752;
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkR = 0x52;
    private bool _registered;

    public event EventHandler? Pressed;

    public void Register(IntPtr windowHandle)
    {
        Unregister();
        AssignHandle(windowHandle);
        _registered = RegisterHotKey(windowHandle, HotKeyId, ModControl | ModAlt, VkR);
    }

    public void Unregister()
    {
        if (_registered && Handle != IntPtr.Zero)
        {
            UnregisterHotKey(Handle, HotKeyId);
        }

        _registered = false;
        if (Handle != IntPtr.Zero)
        {
            ReleaseHandle();
        }
    }

    protected override void WndProc(ref System.Windows.Forms.Message m)
    {
        if (m.Msg == WmHotKey && m.WParam.ToInt32() == HotKeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
