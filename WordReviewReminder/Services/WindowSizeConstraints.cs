using System.Runtime.InteropServices;

namespace WordReviewReminder.Services;

public sealed class WindowSizeConstraints : IDisposable
{
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmNcDestroy = 0x0082;
    private const nuint SubclassId = 1;

    private readonly nint _windowHandle;
    private readonly int _minimumWidth;
    private readonly int _minimumHeight;
    private readonly SubclassProcedure _procedure;
    private bool _disposed;

    public WindowSizeConstraints(nint windowHandle, int minimumWidth, int minimumHeight)
    {
        _windowHandle = windowHandle;
        _minimumWidth = minimumWidth;
        _minimumHeight = minimumHeight;
        _procedure = WindowProcedure;

        if (!SetWindowSubclass(_windowHandle, _procedure, SubclassId, 0))
        {
            throw new InvalidOperationException("The application window could not apply its minimum size.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        RemoveWindowSubclass(_windowHandle, _procedure, SubclassId);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private nint WindowProcedure(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var scale = Math.Max(1d, GetDpiForWindow(windowHandle) / 96d);
            info.MinimumTrackSize.X = (int)Math.Ceiling(_minimumWidth * scale);
            info.MinimumTrackSize.Y = (int)Math.Ceiling(_minimumHeight * scale);
            Marshal.StructureToPtr(info, lParam, false);
            return 0;
        }

        if (message == WmNcDestroy)
        {
            RemoveWindowSubclass(windowHandle, _procedure, subclassId);
            _disposed = true;
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaximumSize;
        public Point MaximumPosition;
        public Point MinimumTrackSize;
        public Point MaximumTrackSize;
    }

    private delegate nint SubclassProcedure(
        nint windowHandle,
        uint message,
        nuint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        nint windowHandle,
        SubclassProcedure procedure,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        nint windowHandle,
        SubclassProcedure procedure,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
