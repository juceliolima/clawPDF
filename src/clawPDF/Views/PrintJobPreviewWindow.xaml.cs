using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using clawSoft.clawPDF.Core.Ghostscript;
using clawSoft.clawPDF.Core.Jobs;
using clawSoft.clawPDF.Shared.Helper;
using NLog;

namespace clawSoft.clawPDF.Views
{
    /// <summary>
    ///     Shows the pages of a print job (spool file) with zoom and view-only rotation.
    ///     Nothing in the job is changed by this window.
    /// </summary>
    internal partial class PrintJobPreviewWindow : Window
    {
        private const string TranslationSection = "PrintJobPreviewWindow";
        private const double MinZoom = 0.1;
        private const double MaxZoom = 8.0;

        /// <summary>Pages further away than this (in viewport heights) get their bitmap released</summary>
        private const double KeepLoadedViewports = 1.5;

        private static readonly double[] ZoomSteps =
            { 0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0 };

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IJobInfo _jobInfo;
        private readonly ObservableCollection<PreviewPage> _pages = new ObservableCollection<PreviewPage>();
        private readonly PreviewRenderer _renderer = new PreviewRenderer();

        private int _currentPage; // 0-based
        private int _pinnedPage = -1; // page selected by navigation, see RefreshVisiblePages
        private bool _isClosed;
        private bool _isRendering;
        private bool _fitPageOnFirstLayout = true;
        private string _sessionFolder;
        private double _zoom = 1.0;

        public PrintJobPreviewWindow(IJobInfo jobInfo)
        {
            _jobInfo = jobInfo ?? throw new ArgumentNullException(nameof(jobInfo));
            InitializeComponent();
            PagesItems.ItemsSource = _pages;
        }

        #region Lifecycle

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var hWnd = new WindowInteropHelper(GetWindow(this)).EnsureHandle();
                ThemeHelper.ChangeTitleBar(hWnd);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Preview: could not apply title bar theme");
            }

            ApplyTranslations();
            UpdateZoomText();
            UpdateNavigation();

            var jobTitle = _jobInfo.Metadata != null ? _jobInfo.Metadata.PrintJobName : null;
            if (!string.IsNullOrEmpty(jobTitle))
                Title = Title + " - " + jobTitle;

            try
            {
                await RenderAsync();
            }
            catch (Exception ex)
            {
                // never let an exception escape an async void handler (it would end the application)
                Logger.Error(ex, "Preview: unexpected error");
                if (!_isClosed)
                {
                    SetBusy(false, "");
                    ShowMessage(T("RenderError", "The preview could not be generated.") + Environment.NewLine +
                                ex.Message);
                }
            }
        }

        private async Task RenderAsync()
        {
            SetBusy(true, T("Rendering", "Generating preview..."));

            PreviewRenderer.CleanupOldSessions(TimeSpan.FromDays(1));
            _sessionFolder = PreviewRenderer.CreateSessionFolder();
            var sessionFolder = _sessionFolder;

            IList<string> files = null;
            Exception error = null;

            _isRendering = true;
            try
            {
                files = await Task.Run(() => _renderer.Render(_jobInfo, sessionFolder));
            }
            catch (Exception ex)
            {
                error = ex;
                Logger.Error(ex, "Preview: rendering failed");
            }
            finally
            {
                _isRendering = false;
            }

            if (_isClosed)
            {
                // window was closed while Ghostscript was still working
                PreviewRenderer.DeleteSessionFolder(sessionFolder);
                return;
            }

            if (error != null || files == null || files.Count == 0)
            {
                SetBusy(false, "");
                ShowMessage(error is NotSupportedException
                    ? T("XpsNotSupported", "Preview is not available for this type of print job.")
                    : T("RenderError", "The preview could not be generated.") +
                      (error != null ? Environment.NewLine + error.Message : ""));
                return;
            }

            foreach (var file in files)
            {
                var page = PreviewPage.Create(file, _renderer.Dpi);
                if (page != null)
                    _pages.Add(page);
            }

            if (_pages.Count == 0)
            {
                SetBusy(false, "");
                ShowMessage(T("RenderError", "The preview could not be generated."));
                return;
            }

            SetBusy(false, string.Format(T("PageCountStatus", "{0} page(s)"), _pages.Count));
            _currentPage = 0;
            UpdateNavigation();

            // Fit the first page once the pages have been laid out
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_fitPageOnFirstLayout)
                {
                    _fitPageOnFirstLayout = false;
                    FitPage();
                }

                RefreshVisiblePages();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            PageScroller.Focus();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _isClosed = true;

            foreach (var page in _pages)
                page.Unload();
            _pages.Clear();

            // If Ghostscript is still running, the folder is deleted when it finishes (see RenderAsync)
            if (!_isRendering)
                PreviewRenderer.DeleteSessionFolder(_sessionFolder);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Translation / status

        private string T(string key, string fallback)
        {
            try
            {
                return TranslationHelper.Instance.TranslatorInstance.GetTranslation(TranslationSection, key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private void ApplyTranslations()
        {
            try
            {
                TranslationHelper.Instance.TranslatorInstance.Translate(this);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Preview: translation failed");
            }

            PreviousPageButton.ToolTip = T("PreviousPageToolTip", "Previous page (Page Up)");
            NextPageButton.ToolTip = T("NextPageToolTip", "Next page (Page Down)");
            PageNumberText.ToolTip = T("PageNumberToolTip", "Go to page");
            ZoomOutButton.ToolTip = T("ZoomOutToolTip", "Zoom out (Ctrl -)");
            ZoomInButton.ToolTip = T("ZoomInToolTip", "Zoom in (Ctrl +)");
            FitWidthButton.ToolTip = T("FitWidthToolTip", "Fit page width");
            FitPageButton.ToolTip = T("FitPageToolTip", "Show whole page");
            ActualSizeButton.ToolTip = T("ActualSizeToolTip", "Actual size (Ctrl 0)");
            RotateLeftButton.ToolTip = T("RotateLeftToolTip", "Rotate counterclockwise (Ctrl L) - view only");
            RotateRightButton.ToolTip = T("RotateRightToolTip", "Rotate clockwise (Ctrl R) - view only");
            RotateAllCheckBox.ToolTip = T("RotateAllToolTip", "Apply rotation to all pages");
        }

        private void SetBusy(bool busy, string status)
        {
            BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = status;
        }

        private void ShowMessage(string message)
        {
            MessageText.Text = message;
            MessageText.Visibility = Visibility.Visible;
            PageScroller.Visibility = Visibility.Collapsed;
            UpdateNavigation();
        }

        #endregion

        #region Navigation

        private void UpdateNavigation()
        {
            var hasPages = _pages.Count > 0;
            PageCountText.Text = "/ " + _pages.Count;
            PageNumberText.Text = hasPages ? (_currentPage + 1).ToString(CultureInfo.InvariantCulture) : "";
            PageNumberText.IsEnabled = hasPages;
            PreviousPageButton.IsEnabled = hasPages && _currentPage > 0;
            NextPageButton.IsEnabled = hasPages && _currentPage < _pages.Count - 1;

            ZoomInButton.IsEnabled = hasPages && _zoom < MaxZoom - 0.001;
            ZoomOutButton.IsEnabled = hasPages && _zoom > MinZoom + 0.001;
            FitWidthButton.IsEnabled = hasPages;
            FitPageButton.IsEnabled = hasPages;
            ActualSizeButton.IsEnabled = hasPages;
            RotateLeftButton.IsEnabled = hasPages;
            RotateRightButton.IsEnabled = hasPages;
            RotateAllCheckBox.IsEnabled = hasPages;
        }

        private void GoToPage(int index)
        {
            if (_pages.Count == 0)
                return;

            index = Math.Max(0, Math.Min(_pages.Count - 1, index));
            var container = PagesItems.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
            if (container == null)
                return;

            var bounds = GetBoundsInScroller(container);
            if (bounds.IsEmpty)
                return;

            PageScroller.ScrollToVerticalOffset(PageScroller.VerticalOffset + bounds.Top - 4);
            _currentPage = index;
            _pinnedPage = index;
            UpdateNavigation();
        }

        private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
        {
            GoToPage(_currentPage - 1);
        }

        private void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            GoToPage(_currentPage + 1);
        }

        private void PageNumberText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            ApplyPageNumberText();
            PageScroller.Focus();
            e.Handled = true;
        }

        private void PageNumberText_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyPageNumberText();
        }

        private void ApplyPageNumberText()
        {
            int number;
            if (int.TryParse(PageNumberText.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                GoToPage(number - 1);
            else
                UpdateNavigation();
        }

        #endregion

        #region Zoom

        private void SetZoom(double zoom, bool keepCenter = true)
        {
            zoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            if (Math.Abs(zoom - _zoom) < 0.0001)
                return;

            // remember the relative position of the viewport center
            double relX = 0.5, relY = 0;
            if (keepCenter && PageScroller.ExtentHeight > 0)
            {
                relY = (PageScroller.VerticalOffset + PageScroller.ViewportHeight / 2) / PageScroller.ExtentHeight;
                if (PageScroller.ExtentWidth > 0)
                    relX = (PageScroller.HorizontalOffset + PageScroller.ViewportWidth / 2) /
                           PageScroller.ExtentWidth;
            }

            _zoom = zoom;
            ZoomTransform.ScaleX = zoom;
            ZoomTransform.ScaleY = zoom;
            PageScroller.UpdateLayout();

            if (keepCenter)
            {
                PageScroller.ScrollToVerticalOffset(relY * PageScroller.ExtentHeight - PageScroller.ViewportHeight / 2);
                PageScroller.ScrollToHorizontalOffset(relX * PageScroller.ExtentWidth -
                                                      PageScroller.ViewportWidth / 2);
            }

            UpdateZoomText();
            UpdateNavigation();
            RefreshVisiblePages();
        }

        private void UpdateZoomText()
        {
            ZoomText.Text = Math.Round(_zoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void ZoomIn()
        {
            var next = ZoomSteps.FirstOrDefault(z => z > _zoom + 0.001);
            SetZoom(next > 0 ? next : MaxZoom);
        }

        private void ZoomOut()
        {
            var prev = ZoomSteps.LastOrDefault(z => z < _zoom - 0.001);
            SetZoom(prev > 0 ? prev : MinZoom);
        }

        /// <summary>Size of a page on screen at 100% zoom, considering its rotation</summary>
        private static Size RotatedSize(PreviewPage page)
        {
            return page.IsSideways
                ? new Size(page.DisplayHeight, page.DisplayWidth)
                : new Size(page.DisplayWidth, page.DisplayHeight);
        }

        private void FitWidth()
        {
            if (_pages.Count == 0)
                return;

            var widest = _pages.Max(p => RotatedSize(p).Width) + 2; // + border
            var available = PageScroller.ViewportWidth - 24;
            if (widest <= 0 || available <= 0)
                return;

            var currentPage = _currentPage;
            SetZoom(available / widest, false);
            GoToPage(currentPage);
        }

        private void FitPage()
        {
            if (_pages.Count == 0)
                return;

            var size = RotatedSize(_pages[Math.Min(_currentPage, _pages.Count - 1)]);
            var availableWidth = PageScroller.ViewportWidth - 24;
            var availableHeight = PageScroller.ViewportHeight - 24;
            if (size.Width <= 0 || size.Height <= 0 || availableWidth <= 0 || availableHeight <= 0)
                return;

            var currentPage = _currentPage;
            SetZoom(Math.Min(availableWidth / (size.Width + 2), availableHeight / (size.Height + 14)), false);
            GoToPage(currentPage);
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomIn();
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            ZoomOut();
        }

        private void FitWidthButton_Click(object sender, RoutedEventArgs e)
        {
            FitWidth();
        }

        private void FitPageButton_Click(object sender, RoutedEventArgs e)
        {
            FitPage();
        }

        private void ActualSizeButton_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(1.0);
        }

        private void PageScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || _pages.Count == 0)
            {
                _pinnedPage = -1; // user scrolls freely again
                return;
            }

            if (e.Delta > 0)
                ZoomIn();
            else if (e.Delta < 0)
                ZoomOut();

            e.Handled = true;
        }

        #endregion

        #region Rotation (view only)

        private void Rotate(int degrees)
        {
            if (_pages.Count == 0)
                return;

            var currentPage = _currentPage;
            if (RotateAllCheckBox.IsChecked == true)
                foreach (var page in _pages)
                    page.Rotate(degrees);
            else
                _pages[currentPage].Rotate(degrees);

            PageScroller.UpdateLayout();
            GoToPage(currentPage);
            RefreshVisiblePages();
        }

        private void RotateLeftButton_Click(object sender, RoutedEventArgs e)
        {
            Rotate(-90);
        }

        private void RotateRightButton_Click(object sender, RoutedEventArgs e)
        {
            Rotate(90);
        }

        #endregion

        #region Keyboard

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            // don't steal keys while the user types a page number
            if (PageNumberText.IsKeyboardFocusWithin)
                return;

            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (ctrl)
                switch (e.Key)
                {
                    case Key.Add:
                    case Key.OemPlus:
                        ZoomIn();
                        e.Handled = true;
                        break;

                    case Key.Subtract:
                    case Key.OemMinus:
                        ZoomOut();
                        e.Handled = true;
                        break;

                    case Key.D0:
                    case Key.NumPad0:
                        SetZoom(1.0);
                        e.Handled = true;
                        break;

                    case Key.R:
                        Rotate(90);
                        e.Handled = true;
                        break;

                    case Key.L:
                        Rotate(-90);
                        e.Handled = true;
                        break;

                    case Key.Home:
                        GoToPage(0);
                        e.Handled = true;
                        break;

                    case Key.End:
                        GoToPage(_pages.Count - 1);
                        e.Handled = true;
                        break;
                }
            else
                switch (e.Key)
                {
                    case Key.Up:
                    case Key.Down:
                    case Key.Space:
                        _pinnedPage = -1; // let the ScrollViewer scroll freely
                        break;

                    case Key.PageUp:
                        GoToPage(_currentPage - 1);
                        e.Handled = true;
                        break;

                    case Key.PageDown:
                        GoToPage(_currentPage + 1);
                        e.Handled = true;
                        break;
                }
        }

        #endregion

        #region Lazy loading of page bitmaps

        private void PageScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            RefreshVisiblePages();
        }

        private void PageScroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pinnedPage = -1; // e.g. dragging the scroll bar
        }

        private void PageScroller_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshVisiblePages();
        }

        private Rect GetBoundsInScroller(FrameworkElement element)
        {
            try
            {
                if (!element.IsVisible || !PageScroller.IsAncestorOf(element))
                    return Rect.Empty;

                return element.TransformToAncestor(PageScroller)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                return Rect.Empty;
            }
        }

        /// <summary>
        ///     Loads the bitmaps of the pages near the viewport, releases the others (keeps memory low
        ///     for jobs with many pages) and determines the current page.
        /// </summary>
        private void RefreshVisiblePages()
        {
            if (_pages.Count == 0 || _isClosed || PageScroller.ViewportHeight <= 0)
                return;

            var viewportHeight = PageScroller.ViewportHeight;
            var keepTop = -viewportHeight * KeepLoadedViewports;
            var keepBottom = viewportHeight * (1 + KeepLoadedViewports);
            var readLine = viewportHeight * 0.1;

            var current = -1;
            var pinnedStillVisible = false;

            for (var i = 0; i < _pages.Count; i++)
            {
                var container = PagesItems.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null)
                    continue;

                var bounds = GetBoundsInScroller(container);
                if (bounds.IsEmpty)
                    continue;

                if (bounds.Bottom >= keepTop && bounds.Top <= keepBottom)
                    _pages[i].Load();
                else
                    _pages[i].Unload();

                var visible = bounds.Bottom > 0 && bounds.Top < viewportHeight;
                if (i == _pinnedPage && visible)
                    pinnedStillVisible = true;

                // current page = first page that still covers the top part of the viewport
                if (current < 0 && bounds.Bottom > readLine && bounds.Top < viewportHeight)
                    current = i;
            }

            // A page chosen with the navigation buttons stays current while it is visible
            // (otherwise the last pages could never become "current", as they can't be scrolled to the top)
            if (pinnedStillVisible)
                current = _pinnedPage;
            else
                _pinnedPage = -1;

            if (current >= 0 && current != _currentPage)
            {
                _currentPage = current;
                if (!PageNumberText.IsKeyboardFocusWithin)
                    UpdateNavigation();
            }
        }

        #endregion
    }

    /// <summary>
    ///     One page of the preview. The bitmap is only kept in memory while the page is near the viewport.
    /// </summary>
    internal class PreviewPage : INotifyPropertyChanged
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private ImageSource _image;
        private int _rotation;

        private PreviewPage(string file, int pixelWidth, int pixelHeight, int dpi)
        {
            File = file;
            // 96 DIP = 1 inch, so 100% zoom shows the page in its real size
            DisplayWidth = pixelWidth * 96.0 / dpi;
            DisplayHeight = pixelHeight * 96.0 / dpi;
        }

        public string File { get; }
        public double DisplayWidth { get; }
        public double DisplayHeight { get; }

        public ImageSource Image
        {
            get => _image;
            private set
            {
                if (ReferenceEquals(_image, value))
                    return;
                _image = value;
                OnPropertyChanged(nameof(Image));
            }
        }

        /// <summary>View rotation in degrees: 0, 90, 180 or 270</summary>
        public int Rotation
        {
            get => _rotation;
            private set
            {
                if (_rotation == value)
                    return;
                _rotation = value;
                OnPropertyChanged(nameof(Rotation));
            }
        }

        public bool IsSideways => Rotation == 90 || Rotation == 270;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Reads only the image header to get the page size. Returns null if the file is unreadable.</summary>
        public static PreviewPage Create(string file, int dpi)
        {
            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation,
                        BitmapCacheOption.None);
                    var frame = decoder.Frames[0];
                    return new PreviewPage(file, frame.PixelWidth, frame.PixelHeight, dpi);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Preview: could not read page image " + file);
                return null;
            }
        }

        public void Rotate(int degrees)
        {
            Rotation = ((Rotation + degrees) % 360 + 360) % 360;
        }

        public void Load()
        {
            if (_image != null)
                return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // releases the file handle immediately
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(File, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                Image = bitmap;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Preview: could not load page image " + File);
            }
        }

        public void Unload()
        {
            Image = null;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
