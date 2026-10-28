using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using clawSoft.clawPDF.Core.Jobs;
using clawSoft.clawPDF.Core.Settings;
using clawSoft.clawPDF.Helper;
using clawSoft.clawPDF.Shared.Helper;
using clawSoft.clawPDF.Shared.ViewModels.UserControls;

namespace clawSoft.clawPDF.Views.UserControls
{
    /// <summary>
    ///     Profile tab "Rotation": rules that rotate pages automatically based on the document title
    /// </summary>
    internal partial class RotationTab : UserControl
    {
        private const string Section = "RotationTab";
        private static readonly TranslationHelper TranslationHelper = TranslationHelper.Instance;

        public RotationTab()
        {
            InitializeComponent();
            if (TranslationHelper.IsInitialized) TranslationHelper.TranslatorInstance.Translate(this);

            var view = (GridView)RulesList.View;
            view.Columns[0].Header = T("PatternColumn", "Title pattern");
            view.Columns[1].Header = T("RotationColumn", "Rotation");

            AngleComboBox.ItemsSource = new List<AngleItem>
            {
                new AngleItem(90, AngleText(90)),
                new AngleItem(270, AngleText(270)),
                new AngleItem(180, AngleText(180))
            };
            AngleComboBox.SelectedIndex = 0;

            ViewModel.ProfileChanged += (sender, args) => RefreshList(-1);
            UpdateButtons();
        }

        public CurrentProfileViewModel ViewModel => (CurrentProfileViewModel)DataContext;

        private IList<RotationRule> Rules => ViewModel.CurrentProfile?.RotationRules;

        private static string T(string key, string fallback)
        {
            try
            {
                return TranslationHelper.TranslatorInstance.GetTranslation(Section, key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private static string AngleText(int angle)
        {
            switch (angle)
            {
                case 90:
                    return T("AngleRight", "90° to the right (clockwise)");
                case 270:
                    return T("AngleLeft", "90° to the left (counterclockwise)");
                case 180:
                    return T("Angle180", "180° (upside down)");
                default:
                    return angle + "°";
            }
        }

        private void RefreshList(int selectIndex)
        {
            var rules = Rules;
            RulesList.ItemsSource = rules?.Select(r => new RuleItem(r, AngleText(r.Angle))).ToList();

            if (rules != null && selectIndex >= 0 && selectIndex < rules.Count)
                RulesList.SelectedIndex = selectIndex;

            UpdateButtons();
            UpdateTestResult();
        }

        private void UpdateButtons()
        {
            var hasProfile = Rules != null;
            var index = RulesList.SelectedIndex;
            var hasPattern = !string.IsNullOrWhiteSpace(PatternTextBox.Text);

            AddRuleButton.IsEnabled = hasProfile && hasPattern;
            UpdateRuleButton.IsEnabled = hasProfile && hasPattern && index >= 0;
            RemoveRuleButton.IsEnabled = hasProfile && index >= 0;
            MoveRuleUpButton.IsEnabled = hasProfile && index > 0;
            MoveRuleDownButton.IsEnabled = hasProfile && index >= 0 && index < Rules.Count - 1;
        }

        private int SelectedAngle => AngleComboBox.SelectedValue is int angle ? angle : 90;

        private void RulesList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RulesList.SelectedItem is RuleItem item)
            {
                PatternTextBox.Text = item.Rule.Pattern;
                AngleComboBox.SelectedValue = item.Rule.Angle;
            }

            UpdateButtons();
        }

        private void PatternTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateButtons();
        }

        private void AddRuleButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (Rules == null || string.IsNullOrWhiteSpace(PatternTextBox.Text))
                return;

            Rules.Add(new RotationRule(PatternTextBox.Text.Trim(), SelectedAngle));
            RefreshList(Rules.Count - 1);
        }

        private void UpdateRuleButton_OnClick(object sender, RoutedEventArgs e)
        {
            var index = RulesList.SelectedIndex;
            if (Rules == null || index < 0 || index >= Rules.Count || string.IsNullOrWhiteSpace(PatternTextBox.Text))
                return;

            Rules[index] = new RotationRule(PatternTextBox.Text.Trim(), SelectedAngle);
            RefreshList(index);
        }

        private void RemoveRuleButton_OnClick(object sender, RoutedEventArgs e)
        {
            var index = RulesList.SelectedIndex;
            if (Rules == null || index < 0 || index >= Rules.Count)
                return;

            Rules.RemoveAt(index);
            PatternTextBox.Text = "";
            RefreshList(Math.Min(index, Rules.Count - 1));
        }

        private void MoveRuleUpButton_OnClick(object sender, RoutedEventArgs e)
        {
            Move(-1);
        }

        private void MoveRuleDownButton_OnClick(object sender, RoutedEventArgs e)
        {
            Move(+1);
        }

        private void Move(int delta)
        {
            var index = RulesList.SelectedIndex;
            var target = index + delta;
            if (Rules == null || index < 0 || target < 0 || target >= Rules.Count)
                return;

            var rule = Rules[index];
            Rules.RemoveAt(index);
            Rules.Insert(target, rule);
            RefreshList(target);
        }

        private void TestTitleTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTestResult();
        }

        /// <summary>Shows which rule (if any) would rotate a document with the typed title</summary>
        private void UpdateTestResult()
        {
            var title = TestTitleTextBox.Text;
            if (string.IsNullOrEmpty(title) || Rules == null)
            {
                TestResultText.Text = "";
                return;
            }

            RotationRule rule = null;
            try
            {
                rule = PageRotation.FindRule(Rules, title,
                    SettingsHelper.Settings?.ApplicationSettings?.TitleReplacement);
            }
            catch
            {
                // ignore
            }

            TestResultText.Text = rule == null
                ? T("TestNoMatch", "No rule matches: the pages are not rotated.")
                : string.Format(T("TestMatch", "Rule \"{0}\": {1}"), rule.Pattern, AngleText(rule.Angle));
        }

        private class AngleItem
        {
            public AngleItem(int angle, string text)
            {
                Angle = angle;
                Text = text;
            }

            public int Angle { get; }
            public string Text { get; }
        }

        private class RuleItem
        {
            public RuleItem(RotationRule rule, string angleText)
            {
                Rule = rule;
                AngleText = angleText;
            }

            public RotationRule Rule { get; }
            public string Pattern => Rule.Pattern;
            public string AngleText { get; }
        }
    }
}
