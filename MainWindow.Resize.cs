using System.Windows;
using System.Windows.Interop;

namespace FileExplorer;

public partial class MainWindow
{
    private HwndSource? _resizeHwndSource;

    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int ResizeGripThickness = 6;

    private void InitializeWindowResize()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
            return;

        _resizeHwndSource = HwndSource.FromHwnd(helper.Handle);
        _resizeHwndSource?.AddHook(WindowResizeWndProc);
    }

    private void DisposeWindowResize()
    {
        if (_resizeHwndSource is not null)
        {
            _resizeHwndSource.RemoveHook(WindowResizeWndProc);
            _resizeHwndSource = null;
        }
    }

    private IntPtr WindowResizeWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmNcHitTest || WindowState != WindowState.Normal)
            return IntPtr.Zero;

        var screenX = (short)(lParam.ToInt64() & 0xFFFF);
        var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var point = PointFromScreen(new Point(screenX, screenY));

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return IntPtr.Zero;

        var grip = ResizeGripThickness;
        var onLeft = point.X < grip;
        var onRight = point.X >= width - grip;
        var onBottom = point.Y >= height - grip;

        // Top edge is owned by the custom title bar (drag). Resize sides + bottom only.
        if (!(onLeft || onRight || onBottom))
            return IntPtr.Zero;

        handled = true;

        if (onBottom && onLeft)
            return (IntPtr)HtBottomLeft;
        if (onBottom && onRight)
            return (IntPtr)HtBottomRight;
        if (onLeft)
            return (IntPtr)HtLeft;
        if (onRight)
            return (IntPtr)HtRight;

        return (IntPtr)HtBottom;
    }
}
