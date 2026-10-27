using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AppKit;
using CoreGraphics;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;
using AnalysisITC.UI.MacOS.CustomViews;

static class AffinityInputTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Native = typeof(ParameterValueAdjustmentView).Assembly;

    static int Main(string[] args)
    {
        NSApplication.Init();
        var previous = PreferencesState.FromSettings();
        var previousStore = PlatformServices.SettingsStore;
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var store = new InMemorySettingsStore();
            // A previously stored alternate mode must no longer affect entry.
            store.SetBool("InputAffinityAsDissociationConstant", false);
            PlatformServices.RegisterSettingsStore(store);
            AppSettings.Load();
            CheckUnit(ConcentrationUnit.µM, 1, 10);
            CheckUnit(ConcentrationUnit.nM, 1000, 10000);
            Console.WriteLine("PASS: native affinity editors always use Kd, including with an obsolete false preference");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            typeof(PreferencesState).GetMethod("ApplyToSettings", Instance).Invoke(previous, new object[] { false });
            PlatformServices.RegisterSettingsStore(previousStore);
            CultureInfo.CurrentCulture = culture;
        }
    }

    static void CheckUnit(ConcentrationUnit unit, double initialKd, double enteredKd)
    {
        AppSettings.DefaultConcentrationUnit = unit;
        var parameter = new Parameter(ParameterType.Affinity1, 6);
        parameter.Limits[0] = 3;
        parameter.Limits[1] = 9;
        var catalog = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisInspectorDisplayCatalog", true);
        Check((string)catalog.GetMethod("ParameterUnit", new[] { typeof(ParameterType) })
            .Invoke(null, new object[] { parameter.Key }) == unit.GetName(), "analysis unit");
        var formatted = (string)catalog.GetMethod("FormatParameter")
            .Invoke(null, new object[] { parameter.Key, 6d });
        Near(double.Parse(formatted), initialKd, "analysis displayed Kd");
        var range = (string)catalog.GetMethod("FormatParameterRange").Invoke(null, new object[] { parameter });
        var bounds = range.Trim('[', ']').Split(',');
        Near(double.Parse(bounds[0]), initialKd / 1000, "lower Kd bound");
        Near(double.Parse(bounds[1]), initialKd * 1000, "upper Kd bound");

        var draftType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisParameterDraft", true);
        var draft = Activator.CreateInstance(draftType, true);
        Set(draft, "Key", parameter.Key);
        Set(draft, "RawText", "");
        Set(draft, "InternalValue", 6d);
        Set(draft, "IsValid", true);
        var optionType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisOptionDraft", true);
        var options = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(AttributeKey), optionType));
        var viewType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisParameterItemView", true);
        using (var view = (NSView)Activator.CreateInstance(viewType, new[] { parameter, draft, options, (object)false }))
        {
            var input = Field<NSTextField>(view, "input");
            Check(input.PlaceholderString.Contains(formatted), "untouched analysis Kd");
            Near(parameter.Value, 6, "untouched internal value");
            input.StringValue = enteredKd.ToString();
            Invoke(view, "InputChanged");
            Check((bool)Get(draft, "IsValid"), "Kd input accepted");
            Near((double)Get(draft, "InternalValue"), 5, "analysis Kd converted to log10 Ka");
            foreach (var invalid in new[] { "0", "-1" })
            {
                input.StringValue = invalid;
                Invoke(view, "InputChanged");
                Check(!(bool)Get(draft, "IsValid"), "nonpositive Kd rejected");
            }
        }

        foreach (var mode in new[] { AdjustmentViewMode.Analysis, AdjustmentViewMode.Designer })
        using (var view = new ParameterValueAdjustmentView(new CGRect(0, 0, 600, 60), parameter, mode))
        {
            view.ShowValueAsInput();
            var input = Field<NSTextField>(view, "Input");
            Near(double.Parse(input.StringValue), initialKd, "adjustment displayed Kd");
            input.StringValue = enteredKd.ToString();
            Invoke(view, "Input_Changed");
            Near(view.Value, 5, "adjustment Kd converted to log10 Ka");
            if (mode == AdjustmentViewMode.Designer)
            {
                var slider = Field<NSSlider>(view, "Slider");
                slider.DoubleValue = 0;
                Invoke(view, "Slider_Activated");
                Near(view.Value, 3, "left slider internal value");
                Near(double.Parse(input.StringValue), initialKd * 1000, "left slider Kd");
                slider.DoubleValue = 1;
                Invoke(view, "Slider_Activated");
                Near(view.Value, 9, "right slider internal value");
                Near(double.Parse(input.StringValue), initialKd / 1000, "right slider Kd");
            }
        }
    }

    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);
    static object Get(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
    static void Invoke(object target, string name) => target.GetType().GetMethod(name, Instance).Invoke(target, new object[] { null, EventArgs.Empty });
    static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) <= 1e-10 * Math.Max(1, Math.Abs(expected)), message + ": " + actual + " != " + expected);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
