using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NLog;
using Forms = System.Windows.Forms;

namespace clawSoft.clawPDF.Shared.Helper
{
    /// <summary>
    ///     Remembers position and size of a Windows common file dialog ("Save as").
    ///     The dialog is a native window, so it is found with a WinEvent hook on the current thread:
    ///     when it is shown it is moved to the saved rectangle, and every move/resize is recorded.
    ///     Dispose (after ShowDialog returns) stores the last rectangle.
    /// </summary>
    /// <example>
    ///     using (new FileDialogPlacementTracker("SaveFileDialog"))
    ///         result = saveFileDialog.ShowDialog(owner);
    /// </example>
    public sealed class FileDialogPlacementTracker : IDisposable
    {
        private const uint EventObjectShow = 0x8002;
        private const uint EventObjectLocationChange = 0x800B;
        private const uint WineventOutOfContext = 0x0000;
        private const int ObjIdWindow = 0;
        private const uint GaRoot = 2;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;

        /// <summary>The dialog may still position itself right after being shown; keep our position during this time</summary>
        private const int EnforceMilliseconds = 400;

        private const int MinVisibleWidth = 60;
        private const int MinVisibleHeight = 20;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly WinEventDelegate _callback; // must stay referenced while the hook is active
        private readonly IntPtr _hook;
        private readonly string _key;

        private IntPtr _dialog = IntPtr.Zero;
        private bool _hasLastRect;
        private Rect _lastRect;
        private bool _hasTarget;
        private Rect _target;
        private int _shownAt;

        public FileDialogPlacementTracker(string key)
        {
            _key = "FileDialog." + key;
            _callback = OnWinEvent;

            try
            {
                _hasTarget = TryLoad(out _target) && IsTitleBarVisible(_target);
                _hook = SetWinEventHook(EventObjectShow, EventObjectLocationChange, IntPtr.Zero, _callback,
                    0, GetCurrentThreadId(), WineventOutOfContext);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "FileDialogPlacement: could not install hook");
                _hook = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_hook != IntPtr.Zero)
                    UnhookWinEvent(_hook);

                if (_hasLastRect)
                    Save(_lastRect);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "FileDialogPlacement: could not save");
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
            uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (hwnd == IntPtr.Zero || idObject != ObjIdWindow || idChild != 0)
                    return;

                if (_dialog == IntPtr.Zero)
                {
                    // first top-level dialog window (#32770) of this thread that becomes visible = the file dialog
                    if (eventType != EventObjectShow || !IsTopLevelDialog(hwnd))
                        return;

                    _dialog = hwnd;
                    _shownAt = Environment.TickCount;
                    if (_hasTarget)
                        MoveTo(_target);
                    Record();
                    return;
                }

                if (hwnd != _dialog || eventType != EventObjectLocationChange)
                    return;

                if (_hasTarget && unchecked(Environment.TickCount - _shownAt) < EnforceMilliseconds)
                {
                    Rect current;
                    if (GetWindowRect(_dialog, out current) && !current.Equals(_target))
                    {
                        MoveTo(_target);
                        return;
                    }
                }

                Record();
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "FileDialogPlacement: hook callback failed");
            }
        }

        private void MoveTo(Rect r)
        {
            SetWindowPos(_dialog, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                SwpNoZOrder | SwpNoActivate);
        }

        private void Record()
        {
            if (_dialog == IntPtr.Zero || IsIconic(_dialog) || IsZoomed(_dialog))
                return;

            Rect r;
            if (GetWindowRect(_dialog, out r) && r.Right - r.Left > 0 && r.Bottom - r.Top > 0)
            {
                _lastRect = r;
                _hasLastRect = true;
            }
        }

        private static bool IsTopLevelDialog(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd) || GetAncestor(hwnd, GaRoot) != hwnd)
                return false;

            var className = new StringBuilder(64);
            GetClassName(hwnd, className, className.Capacity);
            return className.ToString() == "#32770";
        }

        private static bool IsTitleBarVisible(Rect r)
        {
            var titleBar = new System.Drawing.Rectangle(r.Left, r.Top, Math.Max(r.Right - r.Left, MinVisibleWidth), 30);
            return Forms.Screen.AllScreens.Any(screen =>
            {
                var visible = System.Drawing.Rectangle.Intersect(screen.WorkingArea, titleBar);
                return visible.Width >= MinVisibleWidth && visible.Height >= MinVisibleHeight;
            });
        }

        private bool TryLoad(out Rect rect)
        {
            rect = new Rect();
            string value;
            using (var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WindowPlacementHelper.RegistryPath))
            {
                value = regKey?.GetValue(_key) as string;
            }

            var parts = value?.Split(';');
            if (parts == null || parts.Length < 4)
                return false;

            int left, top, width, height;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out left) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out top) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out width) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out height) ||
                width <= 0 || height <= 0)
                return false;

            rect = new Rect { Left = left, Top = top, Right = left + width, Bottom = top + height };
            return true;
        }

        private void Save(Rect r)
        {
            var value = string.Join(";",
                r.Left.ToString(CultureInfo.InvariantCulture),
                r.Top.ToString(CultureInfo.InvariantCulture),
                (r.Right - r.Left).ToString(CultureInfo.InvariantCulture),
                (r.Bottom - r.Top).ToString(CultureInfo.InvariantCulture));

            using (var regKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(WindowPlacementHelper.RegistryPath))
            {
                regKey?.SetValue(_key, value);
            }
        }

        #region Win32

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
            int idChild, uint dwEventThread, uint dwmsEventTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        #endregion
    }
}
