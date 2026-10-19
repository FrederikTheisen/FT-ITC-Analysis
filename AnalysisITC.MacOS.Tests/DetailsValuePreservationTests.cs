// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh DetailsValuePreservationTests
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.CustomViews;

static class DetailsValuePreservationTests
{
    // Exercise Apply without requiring a presented sheet or invoking AppKit dismissal.
    sealed class DetailsController : ExperimentDetailsPopoverController
    {
        public bool Dismissed;
        public override void DismissViewController(NSViewController controller) { Dismissed = true; }
    }
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int failures, cases;

    static int Main()
    {
        NSApplication.Init();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        BufferAttribute.Init();
        Run("edit buffer pH", () => BufferEdit(true));
        Run("edit buffer concentration", () => BufferEdit(false));
        foreach (var text in new[] { "74", "-1", "14.000001", "invalid", "NaN", "Infinity", "-Infinity", "", "   " })
            Run("reject pH and retry " + text, () => RejectedBufferPh(text));
        foreach (var pH in new[] { 0.0, 14.0 })
            Run("pH bound reopen " + pH, () => BufferPhBound(pH));
        Run("remember pH after all rows", RememberPhAfterAllRows);
        Run("remember pH after other form validation", RememberPhAfterFormValidation);
        Run("invalid buffer concentration preserves pH", InvalidBufferConcentration);
        Run("direct buffer apply remembers pH", DirectBufferApply);
        Run("new buffer pH default", NewBufferDefault);
        Console.WriteLine("Native Details pH: " + (cases - failures) + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        cases++;
        DataManager.Clear(DataClearMode.ResetSession);
        var pH = ExperimentAttributeView.LastValue_PH;
        var concentration = ExperimentAttributeView.LastValue_HighConcentration;
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + ": " + error); }
        finally
        {
            ExperimentAttributeView.LastValue_PH = pH;
            ExperimentAttributeView.LastValue_HighConcentration = concentration;
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static void BufferEdit(bool editPh)
    {
        var attribute = Attribute(AttributeKey.Buffer);
        var original = attribute.ParameterValue;
        using (var row = Row(attribute))
        {
            var field = editPh
                ? (NSTextField)typeof(ExperimentAttributeView).GetProperty("DoubleField", Private).GetValue(row, null)
                : Field<NSTextField>(row, "parameterValueField");
            field.StringValue = editPh ? "8.15" : "31.25";
            var after = ApplyRow(row);
            Check(after.DoubleValue == (editPh ? 8.15 : 7.456789123), "pH was not preserved/applied exactly");
            if (editPh) Same(original, after.ParameterValue);
            else
            {
                Check(after.ParameterValue.Value == 0.03125, "buffer concentration edit was lost");
                Check(after.ParameterValue.SD == original.SD, "hidden SD was lost");
            }
        }
    }

    static void RejectedBufferPh(string text)
    {
        var data = Data();
        var original = Attribute(AttributeKey.Buffer);
        data.Attributes.Add(original);
        var before = original.Copy();
        var revision = Revision(data);
        var heats = data.Injections.Select(i => i.PeakArea.Value).ToArray();
        ExperimentAttributeView.LastValue_PH = 6.2;
        using (var controller = Controller(data))
        {
            var row = Rows(controller).Single();
            var ph = PhField(row);
            Select(row, "EnumPopUpControl", (int)AnalysisITC.Core.Data.Buffer.Hepes);
            ph.StringValue = text;
            typeof(ExperimentAttributeView).GetMethod("CheckInput", Private).Invoke(row, new object[] { ph });
            Check(ph.TextColor.IsEqual(NSColor.SystemRed), "invalid pH was not marked red");
            Field<NSTextField>(row, "parameterValueField").StringValue = "50";
            SetComment(controller, "Pending comment");

            RejectApply(controller, ExperimentAttributeInputPolicy.BufferPhValidationMessage);

            Check(ph.StringValue == text, "rejected text was lost");
            Check(ReferenceEquals(original, data.Attributes.Single()), "rejection replaced live attributes");
            Check(original.IntValue == before.IntValue && original.DoubleValue == before.DoubleValue, "rejection changed live buffer");
            Same(before.ParameterValue, original.ParameterValue);
            Check(row.Option.IntValue == before.IntValue && row.Option.DoubleValue == before.DoubleValue, "rejection changed buffer draft values");
            Same(before.ParameterValue, row.Option.ParameterValue);
            Check(string.IsNullOrEmpty(data.Comments), "rejection committed comment");
            Check(Revision(data) == revision && heats.SequenceEqual(data.Injections.Select(i => i.PeakArea.Value)), "rejection changed processing");
            Check(ExperimentAttributeView.LastValue_PH == 6.2, "rejection changed remembered pH");

            ph.StringValue = "8.15";
            typeof(ExperimentAttributeView).GetMethod("CheckInput", Private).Invoke(row, new object[] { ph });
            Check(!ph.TextColor.IsEqual(NSColor.SystemRed), "corrected pH remained red");
            Apply(controller);
            var saved = data.Attributes.Single();
            Check(saved.DoubleValue == 8.15 && saved.IntValue == (int)AnalysisITC.Core.Data.Buffer.Hepes, "retry lost buffer or pH");
            Check(saved.ParameterValue.Value == 0.05 && saved.ParameterValue.SD == before.ParameterValue.SD, "retry lost concentration or SD");
            Check(data.Comments == "Pending comment" && ExperimentAttributeView.LastValue_PH == 8.15, "retry did not commit metadata and default");
        }
    }

    static void BufferPhBound(double pH)
    {
        var data = Data();
        data.Attributes.Add(Attribute(AttributeKey.Buffer));
        using (var controller = Controller(data))
        {
            PhField(Rows(controller).Single()).StringValue = pH.ToString(CultureInfo.CurrentCulture);
            Apply(controller);
        }
        Check(data.Attributes.Single().DoubleValue == pH && ExperimentAttributeView.LastValue_PH == pH, "bound was not saved exactly");
        var revision = Revision(data);
        using (var reopened = Controller(data))
        {
            Check(PhField(Rows(reopened).Single()).DoubleValue == pH, "bound changed on reopening");
            Apply(reopened);
        }
        Check(data.Attributes.Single().DoubleValue == pH && Revision(data) == revision, "untouched bound changed or invalidated processing");
    }

    static void RememberPhAfterAllRows()
    {
        var data = Data();
        data.Attributes.Add(Attribute(AttributeKey.Buffer));
        var second = Attribute(AttributeKey.Buffer);
        second.IntValue = (int)AnalysisITC.Core.Data.Buffer.Hepes;
        data.Attributes.Add(second);
        var revision = Revision(data);
        ExperimentAttributeView.LastValue_PH = 6.2;
        using (var controller = Controller(data))
        {
            var rows = Rows(controller);
            PhField(rows[0]).StringValue = "8.15";
            PhField(rows[1]).StringValue = "74";
            RejectApply(controller, ExperimentAttributeInputPolicy.BufferPhValidationMessage);
            Check(data.Attributes.All(a => a.DoubleValue == 7.456789123), "earlier row committed before later rejection");
            Check(Revision(data) == revision && ExperimentAttributeView.LastValue_PH == 6.2, "later rejection changed processing or default");
            PhField(rows[1]).StringValue = "7.1";
            Apply(controller);
        }
        Check(data.Attributes[0].DoubleValue == 8.15 && data.Attributes[1].DoubleValue == 7.1, "retry lost a buffer pH");
        Check(ExperimentAttributeView.LastValue_PH == 7.1, "last buffer did not supply remembered pH");
    }

    static void RememberPhAfterFormValidation()
    {
        var data = Data();
        data.Attributes.Add(Attribute(AttributeKey.Buffer));
        var revision = Revision(data);
        ExperimentAttributeView.LastValue_PH = 6.2;
        using (var controller = Controller(data))
        {
            PhField(Rows(controller).Single()).StringValue = "8.15";
            var volume = Field<NSTextField>(controller, "CellVolumeField");
            var originalText = volume.StringValue;
            volume.StringValue = "0";
            RejectApply(controller, null);
            Check(data.Attributes.Single().DoubleValue == 7.456789123 && Revision(data) == revision, "form rejection changed live state");
            Check(ExperimentAttributeView.LastValue_PH == 6.2, "form rejection changed remembered pH");
            volume.StringValue = originalText;
            Apply(controller);
        }
        Check(data.Attributes.Single().DoubleValue == 8.15 && ExperimentAttributeView.LastValue_PH == 8.15, "corrected form did not remember pH");
    }

    static void InvalidBufferConcentration()
    {
        var attribute = Attribute(AttributeKey.Buffer);
        var before = attribute.Copy();
        ExperimentAttributeView.LastValue_PH = 6.2;
        using (var row = Row(attribute))
        {
            PhField(row).StringValue = "8.15";
            Select(row, "EnumPopUpControl", (int)AnalysisITC.Core.Data.Buffer.Hepes);
            Field<NSTextField>(row, "parameterValueField").StringValue = "-1";
            var rejected = false;
            try { ApplyRow(row); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "invalid concentration was accepted");
            Check(attribute.IntValue == before.IntValue && attribute.DoubleValue == before.DoubleValue, "invalid concentration changed buffer draft");
            Same(before.ParameterValue, attribute.ParameterValue);
            Check(ExperimentAttributeView.LastValue_PH == 6.2, "invalid concentration changed remembered pH");
        }
    }

    static void DirectBufferApply()
    {
        var data = Data();
        ExperimentAttributeView.LastValue_PH = 6.2;
        using (var row = Row(Attribute(AttributeKey.Buffer)))
        {
            PhField(row).StringValue = "74";
            var rejected = false;
            try { row.ApplyOption(data); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && data.Attributes.Count == 0 && ExperimentAttributeView.LastValue_PH == 6.2, "direct invalid Apply committed state");
            PhField(row).StringValue = "8.15";
            row.ApplyOption(data);
        }
        Check(data.Attributes.Single().DoubleValue == 8.15 && ExperimentAttributeView.LastValue_PH == 8.15, "direct Apply did not remember pH");
    }

    static void NewBufferDefault()
    {
        ExperimentAttributeView.LastValue_PH = 7.4;
        using (var row = Row(new ExperimentAttribute()))
        {
            Select(row, "KeySelectionControl", (int)AttributeKey.Buffer);
            typeof(ExperimentAttributeView).GetMethod("ComboBox_Activated", Private).Invoke(row, new object[] { null, null });
            Select(row, "EnumPopUpControl", (int)AnalysisITC.Core.Data.Buffer.Tris);
            Check(PhField(row).DoubleValue == 7.4 && ApplyRow(row).DoubleValue == 7.4, "new buffer lost default pH");
        }
    }

    static ExperimentDetailsPopoverController Controller(ExperimentData data)
    {
        ExperimentDetailsPopoverController.Data = data;
        var controller = new DetailsController { View = new NSView(new CGRect(0, 0, 720, 600)) };
        controller.ViewDidLoad();
        return controller;
    }

    static ExperimentAttributeView Row(ExperimentAttribute attribute)
    {
        ExperimentDetailsPopoverController.Data = Data();
        return new ExperimentAttributeView(new CGRect(0, 0, 500, 22), attribute, true);
    }

    static ExperimentAttribute ApplyRow(ExperimentAttributeView row)
    {
        var staging = Data();
        row.ApplyOption(staging, stageOnly: true);
        return staging.Attributes.Single();
    }

    static ExperimentAttribute Attribute(AttributeKey key)
    {
        var attribute = ExperimentAttribute.Parameter(key, key.ToString(),
            new FloatWithError(12.345678901e-6, 1.2345678901e-6, 8.765432109e-6, 19.876543219e-6));
        attribute.IntValue = key == AttributeKey.Buffer ? (int)AnalysisITC.Core.Data.Buffer.Tris : (int)Salt.NaCl;
        attribute.DoubleValue = 7.456789123;
        return attribute;
    }

    static ExperimentData Data()
    {
        var data = new ExperimentData("Details regression")
        {
            CellVolume = 200.123456789e-6,
            CellConcentration = new FloatWithError(20.123456789e-6, 1.23456789e-6, 18e-6, 25e-6),
            SyringeConcentration = new FloatWithError(400.123456789e-6, 12.3456789e-6, 380e-6, 450e-6),
            MeasuredTemperature = 25.123456789,
            DateSource = ExperimentDateSource.DataFile,
        };
        for (var i = 0; i < 4; i++)
        {
            var injection = new InjectionData(data, i, 2e-6, 8e-10, true);
            injection.SetPeakArea(new FloatWithError(-1e-5 * (i + 1), 1e-9));
            data.Injections.Add(injection);
        }
        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        return data;
    }

    static void Same(FloatWithError expected, FloatWithError actual)
    {
        Check(expected.Value == actual.Value, "untouched value changed");
        Check(expected.SD == actual.SD, "untouched SD changed");
        Check(expected.Lower == actual.Lower && expected.Upper == actual.Upper, "untouched interval changed");
        Check(expected.IsAsymmetric == actual.IsAsymmetric, "asymmetry changed");
        Check(FloatWithError.IsNaN(expected) == FloatWithError.IsNaN(actual), "missing-value state changed");
    }

    static int Revision(ExperimentData data) => (int)typeof(ExperimentData).GetProperty("ProcessingRevision", Private).GetValue(data, null);

    static void SetComment(ExperimentDetailsPopoverController controller, string text)
    {
        using (var attributed = new NSAttributedString(text))
            Field<NSTextView>(controller, "CommentTextField").TextStorage.SetString(attributed);
    }

    static T Field<T>(object target, string name) => (T)(target is ExperimentDetailsPopoverController
        ? typeof(ExperimentDetailsPopoverController) : target.GetType()).GetField(name, Private).GetValue(target);

    static List<ExperimentAttributeView> Rows(ExperimentDetailsPopoverController controller) => Field<List<ExperimentAttributeView>>(controller, "attributeViews");

    static NSTextField PhField(ExperimentAttributeView row) => (NSTextField)typeof(ExperimentAttributeView).GetProperty("DoubleField", Private).GetValue(row, null);

    static void Select(ExperimentAttributeView row, string name, int tag)
    {
        var popup = (NSPopUpButton)typeof(ExperimentAttributeView).GetProperty(name, Private).GetValue(row, null);
        Check(popup.SelectItemWithTag(tag), "missing popup choice " + tag);
    }

    static void RejectApply(ExperimentDetailsPopoverController controller, string message)
    {
        var previous = AppEventHandler.ShowAppMessage;
        var messages = 0;
        AppEventHandler.ShowAppMessage = (_, error) =>
        {
            messages++;
            if (message != null) Check(error.Message == message, "unexpected validation message: " + error.Message);
        };
        try { typeof(ExperimentDetailsPopoverController).GetMethod("Apply", Private).Invoke(controller, new object[] { null }); }
        finally { AppEventHandler.ShowAppMessage = previous; }
        Check(!((DetailsController)controller).Dismissed && messages == 1, "invalid form was dismissed or did not show validation");
    }

    static void Apply(ExperimentDetailsPopoverController controller)
    {
        typeof(ExperimentDetailsPopoverController).GetMethod("Apply", Private).Invoke(controller, new object[] { null });
        Check(((DetailsController)controller).Dismissed, "Apply did not complete");
    }

    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
