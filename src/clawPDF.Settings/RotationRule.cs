using System;
using System.Globalization;
using System.Text;
using pdfforge.DataStorage;

namespace clawSoft.clawPDF.Core.Settings
{
    /// <summary>
    ///     Rotates the pages of a print job automatically when its title matches a pattern.
    ///     The pattern supports the wildcards * (any text) and ? (any single character), case insensitive.
    /// </summary>
    public class RotationRule
    {
        public RotationRule()
        {
            Init();
        }

        public RotationRule(string pattern, int angle)
        {
            Pattern = pattern;
            Angle = NormalizeAngle(angle);
        }

        /// <summary>Title pattern with wildcards * and ?</summary>
        public string Pattern { get; set; }

        /// <summary>Clockwise rotation in degrees: 90 (right), 180 or 270 (90 to the left)</summary>
        public int Angle { get; set; }

        public static int NormalizeAngle(int angle)
        {
            angle = ((angle % 360) + 360) % 360;
            // only multiples of 90 are valid page rotations
            return angle - angle % 90;
        }

        private void Init()
        {
            Pattern = "";
            Angle = 90;
        }

        public void ReadValues(Data data, string path)
        {
            try
            {
                Pattern = Data.UnescapeString(data.GetValue(@"" + path + @"Pattern"));
            }
            catch
            {
                Pattern = "";
            }

            try
            {
                Angle = NormalizeAngle(int.Parse(data.GetValue(@"" + path + @"Angle"), CultureInfo.InvariantCulture));
            }
            catch
            {
                Angle = 90;
            }
        }

        public void StoreValues(Data data, string path)
        {
            data.SetValue(@"" + path + @"Pattern", Data.EscapeString(Pattern ?? ""));
            data.SetValue(@"" + path + @"Angle", Angle.ToString(CultureInfo.InvariantCulture));
        }

        public RotationRule Copy()
        {
            return new RotationRule { Pattern = Pattern, Angle = Angle };
        }

        public override bool Equals(object o)
        {
            var v = o as RotationRule;
            if (v == null) return false;

            return string.Equals(Pattern, v.Pattern, StringComparison.Ordinal) && Angle == v.Angle;
        }

        public override int GetHashCode()
        {
            return (Pattern ?? "").GetHashCode() ^ Angle;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Pattern=" + Pattern);
            sb.AppendLine("Angle=" + Angle);
            return sb.ToString();
        }
    }
}
