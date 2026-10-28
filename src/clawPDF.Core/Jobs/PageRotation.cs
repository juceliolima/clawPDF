using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using clawSoft.clawPDF.Core.Settings;
using NLog;

namespace clawSoft.clawPDF.Core.Jobs
{
    /// <summary>
    ///     Automatic page rotation based on the document title (profile rotation rules).
    ///     Rotation is determined per source file, so a merged job can contain documents with different rotations.
    /// </summary>
    public static class PageRotation
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        ///     Case insensitive wildcard match: * = any text, ? = any single character. The whole title must match.
        /// </summary>
        public static bool Matches(string pattern, string title)
        {
            if (string.IsNullOrWhiteSpace(pattern) || title == null)
                return false;

            var regex = "^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            try
            {
                return Regex.IsMatch(title.Trim(), regex, RegexOptions.IgnoreCase | RegexOptions.Singleline,
                    TimeSpan.FromMilliseconds(200));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        ///     Returns the rule that applies to the title (the raw title or the title after the title replacements),
        ///     or null. The first matching rule wins.
        /// </summary>
        public static RotationRule FindRule(IEnumerable<RotationRule> rules, string rawTitle,
            IEnumerable<TitleReplacement> titleReplacements = null)
        {
            if (rules == null || rawTitle == null)
                return null;

            var titles = new List<string> { rawTitle };
            if (titleReplacements != null)
                try
                {
                    var replacer = new TitleReplacer();
                    replacer.AddReplacements(titleReplacements);
                    var replaced = replacer.Replace(rawTitle);
                    if (!string.Equals(replaced, rawTitle, StringComparison.Ordinal))
                        titles.Add(replaced);
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "PageRotation: title replacement failed");
                }

            return rules.FirstOrDefault(rule => rule != null && rule.Angle % 360 != 0 &&
                                                titles.Any(t => Matches(rule.Pattern, t)));
        }

        /// <summary>
        ///     Clockwise rotation (0, 90, 180, 270) for every source file of the job
        /// </summary>
        public static int[] GetSourceRotations(IJobInfo jobInfo, ConversionProfile profile,
            IEnumerable<TitleReplacement> titleReplacements = null)
        {
            if (jobInfo?.SourceFiles == null)
                return new int[0];

            var replacements = titleReplacements?.ToList();
            return jobInfo.SourceFiles
                .Select(sfi =>
                {
                    var rule = FindRule(profile?.RotationRules, sfi.DocumentTitle ?? "", replacements);
                    return rule == null ? 0 : RotationRule.NormalizeAngle(rule.Angle);
                })
                .ToArray();
        }

        public static bool HasRotation(int[] sourceRotations)
        {
            return sourceRotations != null && sourceRotations.Any(r => r % 360 != 0);
        }

        /// <summary>
        ///     Counts the pages of a PostScript file using the DSC "%%Page:" comments
        ///     (embedded documents between %%BeginDocument and %%EndDocument are ignored).
        ///     Returns -1 if the file can't be read.
        /// </summary>
        public static int CountPostScriptPages(string psFile)
        {
            try
            {
                var pages = 0;
                var nested = 0;
                // Latin-1 reads any byte sequence; only the ASCII comments are of interest
                using (var reader = new StreamReader(psFile, Encoding.GetEncoding(28591)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!line.StartsWith("%%", StringComparison.Ordinal))
                            continue;

                        if (line.StartsWith("%%BeginDocument", StringComparison.Ordinal))
                            nested++;
                        else if (line.StartsWith("%%EndDocument", StringComparison.Ordinal))
                            nested = Math.Max(0, nested - 1);
                        else if (nested == 0 && line.StartsWith("%%Page:", StringComparison.Ordinal))
                            pages++;
                    }
                }

                return pages;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "PageRotation: could not count pages of " + psFile);
                return -1;
            }
        }

        /// <summary>
        ///     Maps the rotation of each source file to the pages of the output document.
        /// </summary>
        /// <param name="sourceRotations">Rotation per source file</param>
        /// <param name="sourcePageCounts">Page count per source file (-1 = unknown)</param>
        /// <param name="firstSourcePage">1-based page number of the first page of the first source file</param>
        /// <param name="totalSourcePages">Number of output pages that belong to the source files</param>
        /// <returns>1-based page number → clockwise rotation (only pages to rotate)</returns>
        public static IDictionary<int, int> MapToPages(int[] sourceRotations, int[] sourcePageCounts,
            int firstSourcePage, int totalSourcePages)
        {
            var result = new Dictionary<int, int>();
            if (!HasRotation(sourceRotations) || totalSourcePages <= 0)
                return result;

            var countsKnown = sourcePageCounts != null && sourcePageCounts.Length == sourceRotations.Length &&
                              sourcePageCounts.All(c => c >= 0) && sourcePageCounts.Sum() == totalSourcePages;

            if (countsKnown)
            {
                var page = firstSourcePage;
                for (var i = 0; i < sourceRotations.Length; i++)
                {
                    for (var p = 0; p < sourcePageCounts[i]; p++, page++)
                        if (sourceRotations[i] % 360 != 0)
                            result[page] = sourceRotations[i];
                }

                return result;
            }

            // Page counts unknown: only safe if every source file has the same rotation
            if (sourceRotations.Distinct().Count() == 1)
            {
                for (var page = firstSourcePage; page < firstSourcePage + totalSourcePages; page++)
                    result[page] = sourceRotations[0];
                return result;
            }

            Logger.Warn("PageRotation: page count per document unknown and rotations differ; pages not rotated.");
            return result;
        }
    }
}
