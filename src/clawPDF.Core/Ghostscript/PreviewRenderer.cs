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
        ///     Renders all source files of the job (in order, as one continuous document) to PNG files.
        /// </summary>
        /// <param name="jobInfo">The job to preview</param>
        /// <param name="sessionFolder">Empty folder created by <see cref="CreateSessionFolder" /></param>
        /// <returns>Full paths of the rendered pages, ordered by page number</returns>
        public IList<string> Render(IJobInfo jobInfo, string sessionFolder)
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

            // Copy the spool files, so the job can be deleted/merged/converted
            // while (or after) the preview is being rendered without file locks.
            var sourceFiles = new List<string>();
            var index = 0;
            foreach (var sfi in jobInfo.SourceFiles)
            {
                if (string.IsNullOrEmpty(sfi.Filename) || !File.Exists(sfi.Filename))
                {
                    Logger.Warn("Preview: spool file not found: " + sfi.Filename);
                    continue;
                }

                var copy = Path.Combine(sourceFolder,
                    index.ToString("D3") + "_" + Path.GetFileName(sfi.Filename));
                File.Copy(sfi.Filename, copy, true);
                sourceFiles.Add(copy);
                index++;
            }

            if (sourceFiles.Count == 0)
                throw new FileNotFoundException("No spool file found for this print job.");

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
                "-sOutputFile=" + Path.Combine(pagesFolder, PagePrefix + "%05d.png"),
                "-f"
            };
            parameters.AddRange(sourceFiles);

            Logger.Debug("Preview Ghostscript parameters:\r\n" + string.Join("\r\n", parameters));

            ExternalException gsError = null;
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

            var pages = Directory.GetFiles(pagesFolder, PagePrefix + "*.png")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

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
}
