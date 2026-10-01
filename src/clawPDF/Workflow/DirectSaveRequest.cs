using clawSoft.clawPDF.Core.Jobs;

namespace clawSoft.clawPDF.Workflow
{
    /// <summary>
    ///     Marks a print job that the user asked to save directly from the "Manage Print Jobs" window.
    ///     The interactive workflow of that job skips the print job window and opens the "Save as" dialog.
    /// </summary>
    internal static class DirectSaveRequest
    {
        private static readonly object Lock = new object();
        private static IJobInfo _jobInfo;

        public static void Set(IJobInfo jobInfo)
        {
            lock (Lock)
            {
                _jobInfo = jobInfo;
            }
        }

        /// <summary>
        ///     Returns true (once) if the given job was marked for direct saving.
        /// </summary>
        public static bool Consume(IJobInfo jobInfo)
        {
            lock (Lock)
            {
                if (jobInfo == null || !ReferenceEquals(_jobInfo, jobInfo))
                    return false;

                _jobInfo = null;
                return true;
            }
        }
    }
}
