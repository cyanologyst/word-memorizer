using System.Runtime.InteropServices;

namespace WordReviewReminder.Services;

public sealed class TaskbarProgressService
{
    private readonly IntPtr _windowHandle;
    private readonly ITaskbarList3? _taskbar;

    public TaskbarProgressService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        try
        {
            _taskbar = (ITaskbarList3)new TaskbarList();
            _taskbar.HrInit();
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            _taskbar = null;
        }
    }

    public void Set(int value, int maximum)
    {
        if (_taskbar is null)
        {
            return;
        }

        if (maximum <= 0 || value <= 0)
        {
            _taskbar.SetProgressState(_windowHandle, TaskbarProgressState.NoProgress);
            return;
        }

        _taskbar.SetProgressState(_windowHandle, value >= maximum ? TaskbarProgressState.Normal : TaskbarProgressState.Normal);
        _taskbar.SetProgressValue(_windowHandle, (ulong)Math.Clamp(value, 0, maximum), (ulong)maximum);
    }

    public void Clear()
    {
        _taskbar?.SetProgressState(_windowHandle, TaskbarProgressState.NoProgress);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList
    {
    }

    private enum TaskbarProgressState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TaskbarProgressState state);
    }
}
