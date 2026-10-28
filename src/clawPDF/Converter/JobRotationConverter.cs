using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using clawSoft.clawPDF.Core.Jobs;
using clawSoft.clawPDF.Helper;
using clawSoft.clawPDF.Shared.Helper;
using clawSoft.clawPDF.Workflow;

namespace clawSoft.clawPDF.Converter
{
    /// <summary>
    ///     Shows the automatic rotation (profile rotation rules) of a print job in the "Manage Print Jobs" list:
    ///     "↻ 90°", "↺ 90°", "180°", "Mixed" (merged documents with different rotations) or empty.
    /// </summary>
    internal class JobRotationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var jobInfo = value as IJobInfo;
            if (jobInfo == null)
                return "";

            try
            {
                var settings = SettingsHelper.Settings;
                var profile = WorkflowFactory.PreselectedProfile(jobInfo, settings);
                var rotations = PageRotation.GetSourceRotations(jobInfo, profile,
                    settings.ApplicationSettings.TitleReplacement);

                if (!PageRotation.HasRotation(rotations))
                    return "";

                var distinct = rotations.Distinct().ToList();
                if (distinct.Count > 1)
                    return TranslationHelper.Instance.TranslatorInstance.GetTranslation("ManagePrintJobsWindow",
                        "RotationMixed", "Mixed");

                return Describe(distinct[0]);
            }
            catch
            {
                return "";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        /// <summary>Short text for a clockwise angle</summary>
        public static string Describe(int angle)
        {
            switch (((angle % 360) + 360) % 360)
            {
                case 90:
                    return "↻ 90°";
                case 180:
                    return "180°";
                case 270:
                    return "↺ 90°";
                default:
                    return "";
            }
        }
    }
}
