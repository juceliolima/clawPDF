using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using clawSoft.clawPDF.Core.Jobs;
using clawSoft.clawPDF.Helper;
using clawSoft.clawPDF.Shared.Helper;
using clawSoft.clawPDF.Shared.ViewModels;
using clawSoft.clawPDF.Shared.Views;
using clawSoft.clawPDF.ViewModels;
using clawSoft.clawPDF.Workflow;
using NLog;

namespace clawSoft.clawPDF.Views
{
    internal partial class ManagePrintJobsWindow : Window
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>Drop-down of the "Merge All" split button (one entry per printer)</summary>
        private readonly ContextMenu _mergeByPrinterMenu = new ContextMenu();

        /// <summary>Drop-down of the "Delete" split button (one entry per printer)</summary>
        private readonly ContextMenu _deleteByPrinterMenu = new ContextMenu();

        public ManagePrintJobsWindow()
        {
            InitializeComponent();
            WindowPlacementHelper.Attach(this);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            IntPtr hWnd = new WindowInteropHelper(GetWindow(this)).EnsureHandle();
            ThemeHelper.ChangeTitleBar(hWnd);

            TranslationHelper.Instance.TranslatorInstance.Translate(this);
            var view = (GridView)JobList.View;
            view.Columns[0].Header =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow", "TitleColoumn",
                    "Title");
            view.Columns[1].Header =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow", "FilesColoumn",
                    "Files");
            view.Columns[2].Header =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow", "PagesColoumn",
                    "Pages");
            view.Columns[3].Header =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow", "PrinterColoumn",
                    "Printer");
            MergeByPrinterButton.ToolTip =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow",
                    "MergeByPrinterToolTip", "Merge all jobs of one printer");
            DeleteByPrinterButton.ToolTip =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow",
                    "DeleteByPrinterToolTip", "Delete all jobs of one printer");
            PreviewButton.ToolTip =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow",
                    "PreviewToolTip", "Preview the selected print job (double click / Enter)");
            SaveButton.ToolTip =
                TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow",
                    "SaveToolTip", "Save the selected print job now, using the default profile");
            UpdatePreviewButton();

            var vm = (ManagePrintJobsViewModel)DataContext;
            vm.ConfirmMergeOfDifferentPrinters = ConfirmMergeOfDifferentPrinters;
            vm.ConfirmDelete = ConfirmDelete;
        }

        /// <summary>
        ///     Confirmation before print jobs are deleted (selection or all jobs of a printer)
        /// </summary>
        private bool ConfirmDelete(int count, string printerName)
        {
            var translator = TranslationHelper.Instance.TranslatorInstance;
            string message;

            if (printerName != null)
            {
                var format = translator.GetTranslation("ManagePrintJobsWindow", "DeletePrinterJobsQuestion",
                    "Delete all {0} print job(s) of printer \"{1}\"?");
                message = SafeFormat(format, count, string.IsNullOrEmpty(printerName) ? "?" : printerName);
            }
            else if (count == 1)
            {
                message = translator.GetTranslation("ManagePrintJobsWindow", "DeleteJobQuestion",
                    "Delete the selected print job?");
            }
            else
            {
                var format = translator.GetTranslation("ManagePrintJobsWindow", "DeleteJobsQuestion",
                    "Delete the {0} selected print jobs?");
                message = SafeFormat(format, count, "");
            }

            var caption = translator.GetTranslation("ManagePrintJobsWindow", "DeleteCaption", "Delete");

            return MessageWindow.ShowTopMost(message, caption, MessageWindowButtons.YesNo,
                       MessageWindowIcon.Question) == MessageWindowResponse.Yes;
        }

        private static string SafeFormat(string format, int count, string name)
        {
            try
            {
                return string.Format(format, count, name);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>
        ///     Confirmation before "Merge All" joins documents that were printed on different printers
        /// </summary>
        private bool ConfirmMergeOfDifferentPrinters(IList<string> printers)
        {
            var translator = TranslationHelper.Instance.TranslatorInstance;
            var names = string.Join(Environment.NewLine,
                printers.Select(p => "   \u2022 " + (string.IsNullOrEmpty(p) ? "?" : p)));

            var message = translator.GetTranslation("ManagePrintJobsWindow", "MergeDifferentPrintersMessage",
                              "The print jobs come from different printers:") +
                          Environment.NewLine + names + Environment.NewLine + Environment.NewLine +
                          translator.GetTranslation("ManagePrintJobsWindow", "MergeDifferentPrintersQuestion",
                              "Do you really want to merge all of them into one document?");
            var caption = translator.GetTranslation("ManagePrintJobsWindow", "MergeDifferentPrintersCaption",
                "Merge All");

            return MessageWindow.ShowTopMost(message, caption, MessageWindowButtons.YesNo,
                       MessageWindowIcon.Question) == MessageWindowResponse.Yes;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            DragAndDropHelper.DragEnter(e);
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            DragAndDropHelper.Drop(e);
        }

        private void JobList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // re-evaluates all commands, including the up/down arrows (they stayed disabled before)
            ((ManagePrintJobsViewModel)DataContext).RaiseRefreshView();
            UpdatePreviewButton();
        }

        private void UpdatePreviewButton()
        {
            if (PreviewButton != null && JobList != null)
                PreviewButton.IsEnabled = JobList.SelectedItem is IJobInfo;
            if (SaveButton != null && JobList != null)
                SaveButton.IsEnabled = JobList.SelectedItems.Count == 1 && JobList.SelectedItem is IJobInfo;
        }

        private void JobListItem_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            var item = sender as ListViewItem;
            if (item?.DataContext is IJobInfo jobInfo)
            {
                e.Handled = true;
                ShowPreview(jobInfo);
            }
        }

        private void PreviewButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (JobList.SelectedItem is IJobInfo jobInfo)
                ShowPreview(jobInfo);
        }

        /// <summary>
        ///     Saves the selected job right away: it becomes the next job in the queue and its workflow
        ///     opens the "Save as" dialog directly, without the print job window.
        /// </summary>
        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (JobList.SelectedItems.Count != 1 || !(JobList.SelectedItem is IJobInfo jobInfo))
                return;

            if (!JobInfoQueue.Instance.MoveToFront(jobInfo))
                return;

            DirectSaveRequest.Set(jobInfo);
            Close();
        }

        /// <summary>
        ///     Opens the drop-down of the "Merge All" split button with one entry per printer
        /// </summary>
        private void MergeByPrinterButton_OnClick(object sender, RoutedEventArgs e)
        {
            var vm = (ManagePrintJobsViewModel)DataContext;
            var format = TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow",
                "MergeFromPrinter", "Merge from {0} ({1})");

            _mergeByPrinterMenu.Items.Clear();
            foreach (var printer in vm.GetPrintersWithJobCount())
            {
                var printerName = printer.Key;
                var name = string.IsNullOrEmpty(printerName) ? "?" : printerName;
                string header;
                try
                {
                    header = string.Format(format, name, printer.Value);
                }
                catch (FormatException)
                {
                    header = name + " (" + printer.Value + ")";
                }

                var item = new MenuItem
                {
                    Header = header,
                    IsEnabled = printer.Value > 1 // merging needs at least two jobs
                };
                item.Click += (s, args) =>
                {
                    vm.MergeJobsOfPrinter(printerName);
                    UpdatePreviewButton();
                };
                _mergeByPrinterMenu.Items.Add(item);
            }

            if (_mergeByPrinterMenu.Items.Count == 0)
                return;

            _mergeByPrinterMenu.PlacementTarget = MergeAllButton;
            _mergeByPrinterMenu.Placement = PlacementMode.Bottom;
            _mergeByPrinterMenu.IsOpen = true;
        }

        /// <summary>
        ///     Opens the drop-down of the "Delete" split button with one entry per printer
        /// </summary>
        private void DeleteByPrinterButton_OnClick(object sender, RoutedEventArgs e)
        {
            var vm = (ManagePrintJobsViewModel)DataContext;
            var format = TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow",
                "DeleteFromPrinter", "Delete from {0} ({1})");

            _deleteByPrinterMenu.Items.Clear();
            foreach (var printer in vm.GetPrintersWithJobCount())
            {
                var printerName = printer.Key;
                var name = string.IsNullOrEmpty(printerName) ? "?" : printerName;
                string header;
                try
                {
                    header = string.Format(format, name, printer.Value);
                }
                catch (FormatException)
                {
                    header = name + " (" + printer.Value + ")";
                }

                var item = new MenuItem { Header = header };
                item.Click += (s, args) =>
                {
                    vm.DeleteJobsOfPrinter(printerName);
                    UpdatePreviewButton();
                };
                _deleteByPrinterMenu.Items.Add(item);
            }

            if (_deleteByPrinterMenu.Items.Count == 0)
                return;

            _deleteByPrinterMenu.PlacementTarget = DeleteButton;
            _deleteByPrinterMenu.Placement = PlacementMode.Bottom;
            _deleteByPrinterMenu.IsOpen = true;
        }

        private void ShowPreview(IJobInfo jobInfo)
        {
            try
            {
                // Modal: the job can't be deleted/merged while it is shown
                var preview = new PrintJobPreviewWindow(jobInfo) { Owner = this };
                preview.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not open the print preview");
                MessageBox.Show(this, ex.Message, "clawPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnActivated(object sender, EventArgs e)
        {
            ((ManagePrintJobsViewModel)DataContext).RaiseRefreshView();
        }

        private void ManagePrintJobsWindow_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.Enter && JobList.IsKeyboardFocusWithin && JobList.SelectedItem is IJobInfo jobInfo)
            {
                e.Handled = true;
                ShowPreview(jobInfo);
            }
        }
    }
}