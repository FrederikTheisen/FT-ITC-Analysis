using System;
using System.Globalization;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC.GUI.MacOS.CustomViews
{
    public class ValueWithErrorTextField : NSTextField, INSTextViewDelegate
    {
        private const string Separator = " ± ";

        private string _valueText = "";
        private string _errorText = "";
        private bool _editingError = false;
        private bool _internalUpdate = false;

        public bool HasError => _editingError || !string.IsNullOrWhiteSpace(_errorText);

        public string ValueText
        {
            get => _valueText;
            set
            {
                _valueText = NormalizeNumericText(value);
                UpdateDisplayedText(moveCaretToEnd: false);
            }
        }

        public string ErrorText
        {
            get => _errorText;
            set
            {
                _errorText = NormalizeNumericText(value);
                _editingError = !string.IsNullOrWhiteSpace(_errorText);
                UpdateDisplayedText(moveCaretToEnd: false);
            }
        }

        public double DoubleValuePart =>
            TryParseDouble(_valueText, out var v) ? v : 0.0;

        public double DoubleErrorPart =>
            TryParseDouble(_errorText, out var e) ? e : 0.0;

        public bool HasValidValue =>
            string.IsNullOrWhiteSpace(_valueText) || TryParseDouble(_valueText, out _);

        public bool HasValidError =>
            string.IsNullOrWhiteSpace(_errorText) || TryParseDouble(_errorText, out _);

        public bool HasValidInput => HasValidValue && HasValidError;

        public ValueWithErrorTextField(CGRect frameRect) : base(frameRect)
        {
            Bordered = false;
            BezelStyle = NSTextFieldBezelStyle.Rounded;
            FocusRingType = NSFocusRingType.None;
            ControlSize = NSControlSize.Small;
            this.Bezeled = true;
            Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            Alignment = NSTextAlignment.Right;
            LineBreakMode = NSLineBreakMode.Clipping;
            TranslatesAutoresizingMaskIntoConstraints = false;

            Changed += OnChanged;
            EditingEnded += OnEditingEnded;
            EditingBegan += OnEditingBegan;
        }

        public void SetValue(double value, double error = 0)
        {
            _valueText = value.ToString("####0.0####", CultureInfo.InvariantCulture);
            _errorText = error == 0
                ? ""
                : error.ToString("####0.0####", CultureInfo.InvariantCulture);

            _editingError = !string.IsNullOrWhiteSpace(_errorText);
            UpdateDisplayedText(moveCaretToEnd: false);
        }

        public bool TryGetValue(out double value, out double error)
        {
            value = 0;
            error = 0;

            if (!TryParseDouble(_valueText, out value))
                return false;

            if (string.IsNullOrWhiteSpace(_errorText))
            {
                error = 0;
                return true;
            }

            if (!TryParseDouble(_errorText, out error))
                return false;

            if (error < 0)
                error = Math.Abs(error);

            return true;
        }

        public override void KeyDown(NSEvent theEvent)
        {
            if (theEvent == null)
            {
                base.KeyDown(theEvent);
                return;
            }

            // Space: switch from value mode to error mode once
            if (theEvent.KeyCode == 49) // space
            {
                if (!_editingError)
                {
                    _editingError = true;
                    UpdateDisplayedText(moveCaretToEnd: true);
                    return;
                }
            }

            // Backspace: if error is empty, collapse back to value-only
            if (theEvent.KeyCode == 51) // delete/backspace
            {
                if (_editingError && string.IsNullOrWhiteSpace(_errorText))
                {
                    _editingError = false;
                    UpdateDisplayedText(moveCaretToEnd: true);
                    return;
                }
            }

            base.KeyDown(theEvent);
        }

        private void OnChanged(object sender, EventArgs e)
        {
            if (_internalUpdate) return;

            ParseFromDisplayedText(StringValue);
            UpdateDisplayedText(moveCaretToEnd: true);
        }

        private void OnEditingBegan(object sender, EventArgs e)
        {
            try
            {
                var editor = CurrentEditor;
                if (editor != null)
                    editor.Delegate = this;
            }
            catch
            {
            }
        }

        private void OnEditingEnded(object sender, EventArgs e)
        {
            try
            {
                var editor = CurrentEditor;
                if (editor != null && ReferenceEquals(editor.Delegate, this))
                    editor.Delegate = null;
            }
            catch
            {
            }

            if (_internalUpdate) return;

            ParseFromDisplayedText(StringValue);

            // If error field was activated but left empty, collapse it again
            if (string.IsNullOrWhiteSpace(_errorText))
                _editingError = false;

            UpdateDisplayedText(moveCaretToEnd: false);
        }

        [Export("textView:doCommandBySelector:")]
        public bool DoCommandBySelector(NSTextView textView, ObjCRuntime.Selector commandSelector)
        {
            var sel = commandSelector?.Name;
            if (string.IsNullOrEmpty(sel))
                return false;

            // Full selection: destructive/editing command clears everything including ±
            if (IsAllSelected(textView) &&
                (sel == "deleteBackward:" || sel == "deleteForward:"))
            {
                ClearAll();
                return true;
            }

            // Empty error part + delete => remove ± and return to value-only
            if ((sel == "deleteBackward:" || sel == "deleteForward:") &&
                _editingError &&
                string.IsNullOrWhiteSpace(_errorText))
            {
                _editingError = false;
                _errorText = "";
                UpdateDisplayedText(moveCaretToEnd: true);
                return true;
            }

            return false;
        }

        private bool IsAllSelected(NSTextView editor)
        {
            try
            {
                if (editor == null) return false;
                var range = editor.SelectedRange;
                return range.Location == 0 && range.Length == (StringValue?.Length ?? 0);
            }
            catch
            {
                return false;
            }
        }

        private void ClearAll()
        {
            _valueText = "";
            _errorText = "";
            _editingError = false;
            UpdateDisplayedText(moveCaretToEnd: true);
        }

        private void ParseFromDisplayedText(string displayed)
        {
            displayed = displayed ?? "";
            displayed = displayed.Replace(',', '.');

            if (displayed.Contains("±"))
            {
                var parts = displayed.Split(new[] { '±' }, 2, StringSplitOptions.None);
                _valueText = NormalizeNumericText(parts[0]);
                _errorText = parts.Length > 1 ? NormalizeNumericText(parts[1]) : "";
                _editingError = true;
                return;
            }

            // User typed a space before ± was inserted
            var firstSpace = displayed.IndexOf(' ');
            if (firstSpace >= 0)
            {
                var left = displayed.Substring(0, firstSpace);
                var right = displayed.Substring(firstSpace + 1);

                _valueText = NormalizeNumericText(left);
                _errorText = NormalizeNumericText(right);
                _editingError = true;
                return;
            }

            _valueText = NormalizeNumericText(displayed);

            if (string.IsNullOrWhiteSpace(_errorText))
                _editingError = false;
        }

        private void UpdateDisplayedText(bool moveCaretToEnd)
        {
            _internalUpdate = true;

            try
            {
                StringValue = _editingError
                    ? _valueText + Separator + _errorText
                    : _valueText;

                if (moveCaretToEnd)
                    MoveCaretToEnd();
            }
            finally
            {
                _internalUpdate = false;
            }
        }

        private void MoveCaretToEnd()
        {
            try
            {
                var editor = CurrentEditor;
                if (editor != null)
                {
                    var end = StringValue?.Length ?? 0;
                    editor.SelectedRange = new NSRange(end, 0);
                }
            }
            catch
            {
                // Ignore caret-placement failures
            }
        }

        private static string NormalizeNumericText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            s = s.Replace(",", ".");
            s = s.Replace("±", "");
            s = s.Trim();

            return s;
        }

        private static bool TryParseDouble(string s, out double value)
        {
            s = NormalizeNumericText(s);

            return double.TryParse(
                s,
                NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}