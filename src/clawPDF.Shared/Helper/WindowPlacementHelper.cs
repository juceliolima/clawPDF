using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using NLog;
using Forms = System.Windows.Forms;

namespace clawSoft.clawPDF.Shared.Helper
{
    /// <summary>
    ///     Remembers the last position (and size/maximized state of resizable windows) of every window
    ///     and restores it the next time a window of the same type is opened.
    ///     Stored per user in HKCU\Software\clawSoft\clawPDF\WindowPlacement (one value per window type),
    ///     outside of the Settings key, which is cleared whenever the settings are saved.
    /// </summary>
    /// <remarks>
    ///     Usage: call <see cref="Attach" /> in the window constructor, right after InitializeComponent().
    /// </remarks>
    public static class WindowPlacementHelper
    {
        internal const string RegistryPath = @"Software\clawSoft\clawPDF\WindowPlacement";

        /// <summary>Minimum part of the title bar (in pixels) that must be on a screen to accept a saved position</summary>
        private const int MinVisibleWidth = 60;

        private const int MinVisibleHeight = 20;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     Restores the saved placement now (before the window is shown) and saves it again when the window closes.
        /// </summary>
        /// <param name="window">The window</param>
        /// <param name="key">Optional name; default is the full type name of the window</param>
        public static void Attach(Window window, string key = null)
        {
            if (window == null)
                return;

            if (string.IsNullOrEmpty(key))
                key = window.GetType().FullName;

            try
            {
                Restore(window, key);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "WindowPlacement: could not restore " + key);
            }

            window.Closing += (sender, args) =>
            {
                if (args.Cancel)
                    return;

                try
                {
                    Save(window, key);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "WindowPlacement: could not save " + key);
                }
            };
        }

        private static bool CanResizeWidth(Window window)
        {
            return IsResizable(window) && window.SizeToContent != SizeToContent.Width &&
                   window.SizeToContent != SizeToContent.WidthAndHeight;
        }

        private static bool CanResizeHeight(Window window)
        {
            return IsResizable(window) && window.SizeToContent != SizeToContent.Height &&
                   window.SizeToContent != SizeToContent.WidthAndHeight;
        }

        private static bool IsResizable(Window window)
        {
            return window.ResizeMode == ResizeMode.CanResize || window.ResizeMode == ResizeMode.CanResizeWithGrip;
        }

        private static void Save(Window window, string key)
        {
            // When maximized/minimized, RestoreBounds holds the "normal" position
            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window.RestoreBounds;

            if (bounds.IsEmpty || double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top) ||
                double.IsInfinity(bounds.Left) || double.IsInfinity(bounds.Top) ||
                bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var maximized = window.WindowState == WindowState.Maximized && IsResizable(window);

            var value = string.Join(";",
                bounds.Left.ToString("R", CultureInfo.InvariantCulture),
                bounds.Top.ToString("R", CultureInfo.InvariantCulture),
                bounds.Width.ToString("R", CultureInfo.InvariantCulture),
                bounds.Height.ToString("R", CultureInfo.InvariantCulture),
                maximized ? "Maximized" : "Normal");

            using (var regKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                regKey?.SetValue(key, value);
            }
        }

        private static void Restore(Window window, string key)
        {
            string value;
            using (var regKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                value = regKey?.GetValue(key) as string;
            }

            if (string.IsNullOrEmpty(value))
                return;

            var parts = value.Split(';');
            if (parts.Length < 4)
                return;

            double left, top, width, height;
            if (!TryParse(parts[0], out left) || !TryParse(parts[1], out top) ||
                !TryParse(parts[2], out width) || !TryParse(parts[3], out height))
                return;

            var maximized = parts.Length > 4 && parts[4] == "Maximized";

            var useWidth = CanResizeWidth(window) && width > 0;
            var useHeight = CanResizeHeight(window) && height > 0;

            // Width used for the visibility test: saved width if it will be applied, otherwise the designed width
            var testWidth = useWidth ? width : (double.IsNaN(window.Width) ? width : window.Width);

            if (!IsTitleBarVisible(left, top, testWidth))
            {
                Logger.Debug("WindowPlacement: saved position of " + key + " is off-screen, ignored");
                return;
            }

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;

            if (useWidth)
                window.Width = Math.Max(width, window.MinWidth);
            if (useHeight)
                window.Height = Math.Max(height, window.MinHeight);

            if (maximized && IsResizable(window))
                window.WindowState = WindowState.Maximized;
        }

        private static bool TryParse(string s, out double d)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) &&
                   !double.IsNaN(d) && !double.IsInfinity(d);
        }

        /// <summary>
        ///     Checks if enough of the title bar would be on one of the current screens
        ///     (monitors may have been removed or rearranged since the position was saved).
        /// </summary>
        private static bool IsTitleBarVisible(double left, double top, double width)
        {
            // WPF uses device independent units, the screens are reported in pixels (system DPI)
            var scale = GetSystemDpiScale();
            var titleBar = new System.Drawing.Rectangle(
                (int)Math.Round(left * scale),
                (int)Math.Round(top * scale),
                (int)Math.Round(Math.Max(width, MinVisibleWidth) * scale),
                (int)Math.Round(30 * scale));

            return Forms.Screen.AllScreens.Any(screen =>
            {
                var visible = System.Drawing.Rectangle.Intersect(screen.WorkingArea, titleBar);
                return visible.Width >= MinVisibleWidth && visible.Height >= MinVisibleHeight;
            });
        }

        private static double GetSystemDpiScale()
        {
            try
            {
                var pixels = Forms.Screen.PrimaryScreen.Bounds.Width;
                var dips = SystemParameters.PrimaryScreenWidth;
                if (pixels > 0 && dips > 0)
                    return pixels / dips;
            }
            catch
            {
                // fall through
            }

            return 1.0;
        }
    }
}
