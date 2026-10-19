using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AppKit;
using CoreGraphics;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.UI.MacOS.CustomViews;

static class EnergyEditorUnitTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly Native = typeof(OptionAdjustmentView).Assembly;

    static int Main(string[] args)
    {
        NSApplication.Init();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var previous = AppSettings.EnergyUnitFamily;
        try
        {
            // Independent SI expectations: 1 kJ = 1000 J; 1 cal = 4.184 J.
            Run(EnergyUnitFamily.Joules, -40000, 1000, "kJ/mol");
            Run(EnergyUnitFamily.Joules, -40, 1, "J/mol");
            Run(EnergyUnitFamily.Joules, 0, 1000, "kJ/mol");
            Run(EnergyUnitFamily.Calories, -41840, 4184, "kcal/mol");
            Run(EnergyUnitFamily.Calories, -41.84, 4.184, "cal/mol");
            Run(EnergyUnitFamily.Calories, 0, 4184, "kcal/mol");
            Console.WriteLine("PASS: native energy editors retain units through edits, rebuilds, repeated application and sliders");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { AppSettings.EnergyUnitFamily = previous; }
    }

    static ExperimentAttribute Option(double value)
    {
        var option = ExperimentAttribute.FromKey(AttributeKey.PreboundLigandEnthalpy);
        option.ParameterValue = new FloatWithError(value, 0);
        option.BoolValue = false;
        return option;
    }

    static void Run(EnergyUnitFamily family, double initial, double scale, string label)
    {
        AppSettings.EnergyUnitFamily = family;
        AnalysisDraft(initial, scale, label);
        DesignerOption(initial, scale, label);
        DesignerParameter(initial, scale, label);
    }

    static void AnalysisDraft(double initial, double scale, string label)
    {
        var type = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisOptionDraft", true);
        var draft = Activator.CreateInstance(type, true);
        Set(draft, "Option", Option(initial));
        var view = AnalysisView(draft);
        Check(HasUnit(view, label), "initial analysis unit " + label);
        var value = Field<NSTextField>(view, "valueField");
        var error = Field<NSTextField>(view, "errorField");
        foreach (var text in new[] { "-0.05", "0", "-2000", "-0.05" })
        {
            value.StringValue = text;
            Invoke(view, "ParameterValueChanged", value, EventArgs.Empty);
            var expected = double.Parse(text, CultureInfo.InvariantCulture) * scale;
            Near(((ExperimentAttribute)Get(draft, "Option")).ParameterValue.Value, expected, "analysis central value");
            error.StringValue = "0.01";
            Invoke(view, "ParameterValueChanged", error, EventArgs.Empty);
            Near(((ExperimentAttribute)Get(draft, "Option")).ParameterValue.Value, expected, "analysis SD edit preserves central value");
            Near(((ExperimentAttribute)Get(draft, "Option")).ParameterValue.SD, 0.01 * scale, "analysis SD scale");
        }

        // Exercise the actual merge path, which replaces option drafts while retaining text.
        var ownerType = Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisInspectorDraft", true);
        var owner = Activator.CreateInstance(ownerType, true);
        ((IDictionary)Get(owner, "Options"))[AttributeKey.PreboundLigandEnthalpy] = draft;
        var model = new CompetitiveBinding(new ExperimentData("energy-editor.itc"));
        model.ModelOptions.Add(AttributeKey.PreboundLigandEnthalpy, Option(initial));
        var context = Activator.CreateInstance(typeof(AnalysisContext), Instance, null,
            new object[] { AnalysisModel.CompetitiveBinding, model }, CultureInfo.InvariantCulture);
        Invoke(owner, "MergeContext", context, AnalysisSessionState.CreateDefault());
        draft = ((IDictionary)Get(owner, "Options"))[AttributeKey.PreboundLigandEnthalpy];
        view = AnalysisView(draft);
        Check(HasUnit(view, label), "retained draft label keeps its original scale");
        Check(Field<NSTextField>(view, "valueField").StringValue == "-0.05", "rebuild retains typed text");
        Invoke(view, "UpdateParameterOptionState");
        Near(((ExperimentAttribute)Get(draft, "Option")).ParameterValue.Value, -0.05 * scale, "rebuilt draft parsing");
        var fresh = Activator.CreateInstance(type, true);
        Set(fresh, "Option", ((ExperimentAttribute)Get(draft, "Option")).Copy());
        var reopened = AnalysisView(fresh);
        var calorie = AppSettings.EnergyUnitFamily == EnergyUnitFamily.Calories;
        Check(HasUnit(reopened, calorie ? "cal/mol" : "J/mol"), "fresh editor selects the small-value unit");
        Near(double.Parse(Field<NSTextField>(reopened, "valueField").StringValue, CultureInfo.InvariantCulture),
            -0.05 * scale / (calorie ? 4.184 : 1), "fresh editor reformats text with its label");
    }

    static NSView AnalysisView(object draft)
    {
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(AttributeKey), draft.GetType());
        var options = (IDictionary)Activator.CreateInstance(dictionaryType);
        options[AttributeKey.PreboundLigandEnthalpy] = draft;
        return (NSView)Activator.CreateInstance(Native.GetType("AnalysisITC.UI.MacOS.CustomViews.AnalysisOptionItemView", true),
            new object[] { draft, options, new Func<AttributeKey, bool>(_ => false), false });
    }

    static void DesignerOption(double initial, double scale, string label)
    {
        var option = Option(initial);
        using (var view = new OptionAdjustmentView(new CGRect(0, 0, 600, 30), option, AdjustmentViewMode.Designer))
        {
            var field = Field<ValueWithErrorTextField>(view, "InputValueWithErrorField");
            Check(HasUnit(view, label), "designer option label");
            foreach (var text in new[] { "-0.05", "0", "-2000", "-0.05" })
            {
                field.ValueText = text;
                field.ErrorText = "0.01";
                view.ApplyOptions();
                view.ApplyOptions();
                Near(option.ParameterValue.Value, double.Parse(text, CultureInfo.InvariantCulture) * scale, "repeated designer option application");
                Near(option.ParameterValue.SD, 0.01 * scale, "designer option SD");
            }
            var slider = Field<NSSlider>(view, "Slider");
            slider.DoubleValue = 0.25; // [-100000, 100000] J/mol gives -50000 J/mol.
            Invoke(view, "Slider_Activated", slider, EventArgs.Empty);
            view.ApplyOptions();
            // The option field rounds to five decimal places in the captured unit.
            Check(Math.Abs(option.ParameterValue.Value + 50000) < 0.03, "option slider keeps label scale");
            Check(HasUnit(view, label), "option slider preserves label");
        }
    }

    static void DesignerParameter(double initial, double scale, string label)
    {
        var parameter = new Parameter(ParameterType.Enthalpy1, initial);
        using (var view = new ParameterValueAdjustmentView(new CGRect(0, 0, 600, 30), parameter, AdjustmentViewMode.Designer))
        {
            var field = Field<NSTextField>(view, "Input");
            Check(HasUnit(view, label), "designer parameter label");
            foreach (var text in new[] { "-0.05", "0", "-2000", "-0.05" })
            {
                field.StringValue = text;
                Invoke(view, "Input_Changed", field, EventArgs.Empty);
                parameter.Update(view.Value); // The simulation mutates the same parameter.
                Near(view.Value, double.Parse(text, CultureInfo.InvariantCulture) * scale, "parameter reread after model update");
            }
            var slider = Field<NSSlider>(view, "Slider");
            slider.DoubleValue = 0.25;
            Invoke(view, "Slider_Activated", slider, EventArgs.Empty);
            // G3 formatting intentionally rounds slider text.
            Check(Math.Abs(view.Value + 50000) < 250, "parameter slider keeps label scale");
            Check(HasUnit(view, label), "parameter slider preserves label");
        }
    }

    static bool HasUnit(NSView view, string unit)
    {
        var field = view as NSTextField;
        if (field != null)
        {
            var index = field.StringValue.IndexOf(unit, StringComparison.Ordinal);
            if (index >= 0 && (index == 0 || !char.IsLetter(field.StringValue[index - 1])))
                return true;
        }
        foreach (var child in view.Subviews)
            if (HasUnit(child, unit)) return true;
        return false;
    }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);
    static object Get(object target, string name) => target.GetType().GetProperty(name, Instance).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name, Instance).SetValue(target, value);
    static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Instance).Invoke(target, args);
    static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) <= Math.Max(1e-10, Math.Abs(expected) * 1e-12), message + ": " + actual + " != " + expected);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
