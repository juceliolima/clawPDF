using System;
using System.Windows;
using System.Windows.Threading;
using clawSoft.clawPDF.Core.Jobs;

namespace clawSoft.clawPDF.Views
{
    /// <summary>
    ///     Small "working" animation shown while a job is converted. It closes itself as soon as the job is completed.
    /// </summary>
    internal partial class ConversionProgressWindow : Window
    {
        /// <summary>Checks the job state regularly, in case the completed event was missed</summary>
        private readonly DispatcherTimer _timer;

        private IJob _job;
        private bool _closeRequested;

        public ConversionProgressWindow()
        {
            InitializeComponent();

            // This splash is always centered and can't be moved, so it does not remember its position.
            _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _timer.Tick += (sender, args) => CloseIfCompleted();

            Loaded += (sender, args) => CloseIfCompleted();
            Closed += (sender, args) =>
            {
                _timer.Stop();
                if (_job != null)
                    _job.OnJobCompleted -= job_OnJobCompleted;
            };
        }

        public void ApplyJob(IJob job)
        {
            _job = job;
            job.OnJobCompleted += job_OnJobCompleted;
            _timer.Start();
        }

        private void CloseIfCompleted()
        {
            if (_job != null && _job.Completed)
                RequestClose();
        }

        private void job_OnJobCompleted(object sender, JobCompletedEventArgs e)
        {
            // called on the conversion thread
            Dispatcher.BeginInvoke(new Action(RequestClose));
        }

        private void RequestClose()
        {
            if (_closeRequested)
                return;

            // The window may not be shown yet (very fast jobs); Loaded/the timer will try again
            if (!IsLoaded)
                return;

            _closeRequested = true;
            _timer.Stop();
            Close();
        }
    }
}
