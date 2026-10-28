using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using clawSoft.clawPDF.Core.Jobs;
using clawSoft.clawPDF.Utilities;
using NLog;

namespace clawSoft.clawPDF.Core.Ghostscript
{
    /// <summary>
    ///     Renders the spool files (PostScript) of a print job into PNG images, one per page,
    ///     so they can be shown in a preview window. The job itself is never modified:
    ///     the spool files are copied to a private temporary folder before rendering.
    /// </summary>
    public class PreviewRenderer
    {
        public const int DefaultDpi = 150;
        private const string PagePrefix = "page_";

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public PreviewRenderer() : this(DefaultDpi)
        {
        }

        public PreviewRenderer(int dpi)
        {
            Dpi = dpi < 36 ? 36 : (dpi > 600 ? 600 : dpi);
        }

        /// <summary>Resolution (dots per inch) used for the page images</summary>
        public int Dpi { get; }

        /// <summary>
        ///     Base folder for all preview data: %TEMP%\clawPDF\Preview
        /// </summary>
        public static string PreviewBaseFolder => Path.Combine(Path.GetTempPath(), "clawPDF", "Preview");

        /// <summary>
        ///     Creates a new, unique, empty folder for one preview session
        /// </summary>
        public static string CreateSessionFolder()
        {
            var folder = Path.Combine(PreviewBaseFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>
        ///     Renders all source files of the job (in order) to PNG files. Each source file is rendered on its own,
        ///     so every page knows the document it belongs to (needed for the automatic rotation rules).
        /// </summary>
        /// <param name="jobInfo">The job to preview</param>
        /// <param name="sessionFolder">Empty folder created by <see cref="CreateSessionFolder" /></param>
        /// <returns>The rendered pages in document order</returns>
        public IList<PreviewRenderedPage> Render(IJobInfo jobInfo, string sessionFolder)
        {
            if (jobInfo == null)
                throw new ArgumentNullException(nameof(jobInfo));
            if (string.IsNullOrEmpty(sessionFolder))
                throw new ArgumentNullException(nameof(sessionFolder));

            if (jobInfo.JobType == JobType.XpsJob)
                throw new NotSupportedException("Preview is not available for XPS jobs.");

            if (new GhostscriptDiscovery().GetBestGhostscriptInstance() == null)
                throw new InvalidOperationException("No valid Ghostscript version found.");

            var sourceFolder = Path.Combine(sessionFolder, "source");
            var pagesFolder = Path.Combine(sessionFolder, "pages");
            Directory.CreateDirectory(sourceFolder);
            Directory.CreateDirectory(pagesFolder);

            var pages = new List<PreviewRenderedPage>();
            ExternalException gsError = null;
            var foundSource = false;

            for (var index = 0; index < jobInfo.SourceFiles.Count; index++)
            {
                var sfi = jobInfo.SourceFiles[index];
                if (string.IsNullOrEmpty(sfi.Filename) || !File.Exists(sfi.Filename))
                {
                    Logger.Warn("Preview: spool file not found: " + sfi.Filename);
                    continue;
                }

                foundSource = true;

                // Copy the spool file, so the job can be deleted/merged/converted
                // while (or after) the preview is being rendered without file locks.
                var copy = Path.Combine(sourceFolder, index.ToString("D3") + "_" + Path.GetFileName(sfi.Filename));
                File.Copy(sfi.Filename, copy, true);

                var prefix = PagePrefix + index.ToString("D3") + "_";
                var parameters = new List<string>
                {
                    "gs",
                    "-sFONTPATH=" + new OsHelper().WindowsFontsFolder + ";" +
                    Path.Combine(Directory.GetCurrentDirectory(), "fonts"),
                    "-dNOPAUSE",
                    "-dBATCH",
                    "-dSAFER",
                    "-sDEVICE=png16m",
                    "-r" + Dpi,
                    "-dTextAlphaBits=4",
                    "-dGraphicsAlphaBits=4",
                    "-sOutputFile=" + Path.Combine(pagesFolder, prefix + "%05d.png"),
                    "-f",
                    copy
                };

                Logger.Debug("Preview Ghostscript parameters:\r\n" + string.Join("\r\n", parameters));

                try
                {
                    GhostscriptCall.CallAPI(parameters.ToArray());
                }
                catch (ExternalException ex)
                {
                    // Ghostscript may fail on a later page; pages rendered so far are still shown
                    Logger.Warn(ex, "Preview: Ghostscript returned an error (" + ex.ErrorCode + ")");
                    gsError = ex;
                }

                pages.AddRange(Directory.GetFiles(pagesFolder, prefix + "*.png")
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new PreviewRenderedPage(f, index)));
            }

            if (!foundSource)
                throw new FileNotFoundException("No spool file found for this print job.");

            if (pages.Count == 0)
                throw new InvalidOperationException("Ghostscript did not produce any page.", gsError);

            Logger.Debug("Preview: rendered " + pages.Count + " page(s)");
            return pages;
        }

        /// <summary>
        ///     Deletes a preview session folder. Never throws.
        /// </summary>
        public static void DeleteSessionFolder(string sessionFolder)
        {
            if (string.IsNullOrEmpty(sessionFolder))
                return;

            try
            {
                if (Directory.Exists(sessionFolder))
                    Directory.Delete(sessionFolder, true);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Preview: could not delete " + sessionFolder);
            }
        }

        /// <summary>
        ///     Removes leftovers of previous sessions (e.g. after a crash) older than the given age. Never throws.
        /// </summary>
        public static void CleanupOldSessions(TimeSpan maxAge)
        {
            try
            {
                if (!Directory.Exists(PreviewBaseFolder))
                    return;

                foreach (var dir in Directory.GetDirectories(PreviewBaseFolder))
                    try
                    {
                        if (DateTime.Now - Directory.GetCreationTime(dir) > maxAge)
                            Directory.Delete(dir, true);
                    }
                    catch
                    {
                        // folder in use or no permission - try again next time
                    }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Preview: cleanup of old sessions failed");
            }
        }
    }

    /// <summary>
    ///     One rendered preview page and the index of the source file (document) it belongs to
    /// </summary>
    public class PreviewRenderedPage
    {
        public PreviewRenderedPage(string file, int sourceIndex)
        {
            File = file;
            SourceIndex = sourceIndex;
        }

        public string File { get; }
        public int SourceIndex { get; }
    }
}
