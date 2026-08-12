using System;
using System.Globalization;
using System.Linq;

using AppKit;
using Foundation;

namespace AnalysisITC
{
    [Register("MacPreferencesWindowController")]
    public sealed class MacPreferencesWindowController : NSWindowController
    {
        MacPreferencesPaneController[] panes;
        NSTabViewController tabController;
        bool hasShown;

        public MacPreferencesWindowController(IntPtr handle) : base(handle)
        {
        }

        public override void WindowDidLoad()
        {
            base.WindowDidLoad();

            tabController = Window.ContentViewController as NSTabViewController
                ?? throw new InvalidOperationException("Preferences.storyboard must use an NSTabViewController as the window content controller.");
            panes = tabController.TabViewItems
                .Select(item => item.ViewController as MacPreferencesPaneController)
                .ToArray();
            if (panes.Length != 4 || panes.Any(pane => pane == null))
                throw new InvalidOperationException("Preferences.storyboard must contain the four preferences pane controllers.");
        }

        internal void ShowPreferences()
        {
            _ = Window;
            LoadState(MacPreferencesState.FromSettings());
            ShowWindow(this);
            if (!hasShown) Window.Center();
            hasShown = true;
            Window.MakeKeyAndOrderFront(this);
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
            Window.MakeFirstResponder(null);
        }

        internal void ApplyPreferences()
        {
            Window.MakeFirstResponder(null);
            ClearStatus();

            var state = MacPreferencesState.FromSettings();
            foreach (var pane in panes.OrderBy(item => item.PaneIndex))
            {
                if (pane.TryUpdateState(state, out var error)) continue;

                tabController.SelectedTabViewItemIndex = pane.PaneIndex;
                pane.SetStatus(error.Message, true);
                if (error.Control != null) Window.MakeFirstResponder(error.Control);
                return;
            }

            try
            {
                state.Apply();
                Window.PerformClose(this);
            }
            catch (Exception ex)
            {
                CurrentPane.SetStatus(ex.Message, true);
            }
        }

        internal void CancelPreferences()
        {
            Window.PerformClose(this);
        }

        internal void RestoreDefaults()
        {
            LoadState(MacPreferencesState.Defaults());
            CurrentPane.SetStatus("Defaults staged. Choose Apply to save them.", false);
        }

        internal void SetCurrentStatus(string message, bool error)
        {
            CurrentPane.SetStatus(message, error);
        }

        void LoadState(MacPreferencesState state)
        {
            foreach (var pane in panes.OrderBy(item => item.PaneIndex))
            {
                _ = pane.View;
                pane.LoadState(state);
                pane.SetStatus("", false);
                pane.ResetScrollPosition();
            }
        }

        void ClearStatus()
        {
            foreach (var pane in panes) pane.SetStatus("", false);
        }

        MacPreferencesPaneController CurrentPane
        {
            get
            {
                var index = Math.Max(0, Math.Min(panes.Length - 1, (int)tabController.SelectedTabViewItemIndex));
                return panes.First(pane => pane.PaneIndex == index);
            }
        }
    }

    public abstract class MacPreferencesPaneController : NSViewController
    {
        protected MacPreferencesPaneController(IntPtr handle) : base(handle)
        {
        }

        [Outlet]
        public NSScrollView PreferencesScrollView { get; set; }

        [Outlet]
        public NSTextField StatusLabel { get; set; }

        internal abstract int PaneIndex { get; }
        internal abstract void LoadState(MacPreferencesState state);
        internal abstract bool TryUpdateState(MacPreferencesState state, out PreferencesValidationError error);

        protected MacPreferencesWindowController Coordinator =>
            View?.Window?.WindowController as MacPreferencesWindowController;

        internal void SetStatus(string message, bool error)
        {
            if (StatusLabel == null) return;
            StatusLabel.StringValue = message ?? "";
            StatusLabel.TextColor = error ? NSColor.SystemRed : NSColor.SecondaryLabel;
        }

        internal void ResetScrollPosition()
        {
            if (PreferencesScrollView == null) return;
            PreferencesScrollView.ContentView.ScrollToPoint(CoreGraphics.CGPoint.Empty);
            PreferencesScrollView.ReflectScrolledClipView(PreferencesScrollView.ContentView);
        }

        [Export("applyPreferences:")]
        public void ApplyPreferences(NSObject sender) => Coordinator?.ApplyPreferences();

        [Export("cancelPreferences:")]
        public void CancelPreferences(NSObject sender) => Coordinator?.CancelPreferences();

        [Export("restorePreferenceDefaults:")]
        public void RestorePreferenceDefaults(NSObject sender) => Coordinator?.RestoreDefaults();

        protected static bool ReadDouble(
            NSTextField field,
            string label,
            double minimum,
            double maximum,
            out double value,
            out PreferencesValidationError error)
        {
            var text = field?.StringValue ?? "";
            if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                && value >= minimum && value <= maximum)
            {
                error = null;
                return true;
            }

            error = new PreferencesValidationError(
                $"{label} must be between {minimum:G5} and {maximum:G5}.", field);
            return false;
        }

        protected static bool ReadInt(
            NSTextField field,
            string label,
            int minimum,
            int maximum,
            out int value,
            out PreferencesValidationError error)
        {
            value = 0;
            if (field != null
                && int.TryParse(field.StringValue, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
                && value >= minimum && value <= maximum)
            {
                error = null;
                return true;
            }

            error = new PreferencesValidationError(
                $"{label} must be an integer between {minimum} and {maximum}.", field);
            return false;
        }

        protected static void PopulatePopup<T>(NSPopUpButton popup, T[] values, Func<T, string> title)
        {
            popup.RemoveAllItems();
            foreach (var value in values)
                popup.Menu.AddItem(new NSMenuItem(title(value)) { Tag = Convert.ToInt32(value) });
        }

        protected static T PopupValue<T>(NSPopUpButton popup) where T : struct =>
            (T)Enum.ToObject(typeof(T), (int)popup.SelectedTag);

        protected static void SelectPopup<T>(NSPopUpButton popup, T value) =>
            popup.SelectItemWithTag(Convert.ToInt32(value));

        protected static void Set(NSButton checkbox, bool value) =>
            checkbox.State = value ? NSCellStateValue.On : NSCellStateValue.Off;

        protected static bool IsOn(NSButton checkbox) => checkbox.State == NSCellStateValue.On;

        protected static void ConfigureDiscreteSlider(NSSlider slider, int valueCount)
        {
            slider.MinValue = 0;
            slider.MaxValue = valueCount - 1;
            slider.TickMarksCount = valueCount;
            slider.AllowsTickMarkValuesOnly = true;
            slider.Continuous = true;
        }

        protected static int NearestIndex(int[] values, int value)
        {
            var nearest = 0;
            var smallestDistance = Math.Abs((long)values[0] - value);
            for (var index = 1; index < values.Length; index++)
            {
                var distance = Math.Abs((long)values[index] - value);
                if (distance < smallestDistance)
                {
                    nearest = index;
                    smallestDistance = distance;
                }
            }
            return nearest;
        }

        protected static int NearestIndex(double[] values, double value)
        {
            var nearest = 0;
            var smallestDistance = Math.Abs(values[0] - value);
            for (var index = 1; index < values.Length; index++)
            {
                var distance = Math.Abs(values[index] - value);
                if (distance < smallestDistance)
                {
                    nearest = index;
                    smallestDistance = distance;
                }
            }
            return nearest;
        }

        protected static int SliderIndex(NSSlider slider, int valueCount) =>
            Math.Max(0, Math.Min(valueCount - 1, (int)Math.Round(slider.DoubleValue)));

        protected static string Format(double value) => value.ToString("G6", CultureInfo.CurrentCulture);

        protected static T[] EnumValues<T>() => Enum.GetValues(typeof(T)).Cast<T>().ToArray();

        protected static string FriendlyName<T>(T value)
        {
            var text = value.ToString();
            return string.Concat(text.Select((character, index) =>
                index > 0 && char.IsUpper(character) && !char.IsUpper(text[index - 1])
                    ? " " + character
                    : character.ToString()));
        }
    }

    public sealed class PreferencesValidationError
    {
        public PreferencesValidationError(string message, NSView control)
        {
            Message = message;
            Control = control;
        }

        public string Message { get; }
        public NSView Control { get; }
    }

    [Register("FlippedPreferencesDocumentView")]
    public sealed class FlippedPreferencesDocumentView : NSView
    {
        public FlippedPreferencesDocumentView(IntPtr handle) : base(handle) { }

        public override bool IsFlipped => true;
    }
}
