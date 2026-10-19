// Run: bash AnalysisITC.MacOS.Tests/run-analysis-null-graph-tests.sh DetailsDraftPreservationTests DetailsValuePreservationTests
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AppKit;
using CoreGraphics;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.UI.MacOS.CustomViews;
using BufferKind = AnalysisITC.Core.Data.Buffer;

static class DetailsDraftPreservationTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int cases, failures;
    sealed class Controller : ExperimentDetailsPopoverController
    {
        public bool Dismissed;
        public override void DismissViewController(NSViewController controller) { Dismissed = true; }
    }

    static int Main()
    {
        NSApplication.Init();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        BufferAttribute.Init();
        foreach (var action in new[] { "add", "remove", "change-key", "PBS", "TBS" })
        {
            Run("preserve and apply " + action, () => PreserveAndApply(action));
            foreach (var text in new[] { "-", "invalid" })
                Run("invalid draft " + action + " " + text, () => InvalidDraft(action, text));
        }
        Run("key availability and precision", KeyAvailability);
        Run("replacement removal and cancel", ReplacementAndCancel);
        Run("empty attributes", EmptyAttributes);
        Console.WriteLine("Native Details drafts: " + (cases - failures) + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static void Run(string name, Action test)
    {
        cases++;
        DataManager.Clear(DataClearMode.ResetSession);
        var ph = ExperimentAttributeView.LastValue_PH;
        var concentration = ExperimentAttributeView.LastValue_HighConcentration;
        try { test(); Console.WriteLine("PASS: " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL: " + name + ": " + error); }
        finally
        {
            ExperimentAttributeView.LastValue_PH = ph;
            ExperimentAttributeView.LastValue_HighConcentration = concentration;
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static void PreserveAndApply(string action)
    {
        var data = Data();
        using (var controller = Open(data))
        {
            var species = Row(controller, AttributeKey.Species);
            var competitor = Row(controller, AttributeKey.PreboundLigandConc);
            var ionic = Row(controller, AttributeKey.IonicStrength);
            var originalIonic = ionic.Option.ParameterValue;
            Edit(controller);
            ChangeList(controller, action);
            Check(ReferenceEquals(species, Row(controller, AttributeKey.Species)), "species editor was replaced");
            Check(ReferenceEquals(competitor, Row(controller, AttributeKey.PreboundLigandConc)), "competitor editor was replaced");
            Check(Field<NSTextField>(species, "StringField").StringValue == "Edited species", "species text was lost");
            Check(Field<NSPopUpButton>(species, "EnumPopUpControl").SelectedTag == (int)ExperimentSpeciesLocation.Syringe, "species location was lost");
            Check(Field<NSTextField>(competitor, "parameterValueField").StringValue == "31.25", "competitor value was lost");
            Check(Field<NSTextField>(competitor, "parameterErrorField").StringValue == "2.5", "competitor SD was lost");
            Original(data);
            Apply(controller);
            Check(controller.Dismissed, "Apply did not complete");
            Check(Stored(data, AttributeKey.Species).StringValue == "Edited species", "species edit not saved");
            Check(Stored(data, AttributeKey.Species).IntValue == (int)ExperimentSpeciesLocation.Syringe, "species location not saved");
            Check(Stored(data, AttributeKey.PreboundLigandConc).ParameterValue.Value == 31.25e-6, "competitor edit not saved");
            Check(Stored(data, AttributeKey.PreboundLigandConc).ParameterValue.SD == 2.5e-6, "competitor SD not saved");
            if (action == "PBS" || action == "TBS")
            {
                Check(ReferenceEquals(ionic, Row(controller, AttributeKey.IonicStrength)), "ionic strength editor was replaced");
                Same(originalIonic, Stored(data, AttributeKey.IonicStrength).ParameterValue);
                Preset(data, action == "PBS");
            }
            else
            {
                Check(Stored(data, AttributeKey.Buffer).ParameterValue.Value == 0.05, "buffer concentration not saved");
                Check(Stored(data, AttributeKey.Buffer).DoubleValue == 8.15, "pH not saved");
                Check(Stored(data, AttributeKey.Buffer).IntValue == (int)BufferKind.Hepes, "buffer selection not saved");
                Check(Stored(data, AttributeKey.Salt).ParameterValue.Value == 0.175, "salt concentration not saved");
                Check(Stored(data, AttributeKey.Salt).IntValue == (int)Salt.KCl, "salt selection not saved");
            }
        }
    }

    static void InvalidDraft(string action, string text)
    {
        var data = Data();
        using (var controller = Open(data))
        {
            var competitor = Row(controller, AttributeKey.PreboundLigandConc);
            Field<NSTextField>(competitor, "parameterValueField").StringValue = text;
            ChangeList(controller, action);
            var after = Row(controller, AttributeKey.PreboundLigandConc);
            Check(Field<NSTextField>(after, "parameterValueField").StringValue == text, "invalid draft was lost");
            // Exercise the Apply validator directly to avoid opening a handled-error alert in this harness.
            var rejected = false;
            try { after.ApplyOption(new ExperimentData("Staging"), stageOnly: true); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "invalid draft was accepted");
            Original(data);
        }
    }

    static void KeyAvailability()
    {
        var data = Data();
        var exact = Stored(data, AttributeKey.PreboundLigandConc).ParameterValue;
        using (var controller = Open(data))
        {
            var buffer = Row(controller, AttributeKey.Buffer);
            var blank = Row(controller, AttributeKey.Null);
            var events = 0;
            buffer.KeyChanged += (s, e) => events++;
            Check(!Keys(blank).Contains(AttributeKey.PreboundLigandConc), "duplicate competitor allowed");
            Check(Keys(blank).Contains(AttributeKey.Buffer), "repeatable buffer unavailable");
            var ionic = Row(controller, AttributeKey.IonicStrength);
            Remove(ionic);
            Check(Keys(blank).Contains(AttributeKey.IonicStrength), "removed key unavailable");
            Select(blank, "KeySelectionControl", (int)AttributeKey.IonicStrength);
            Add(controller);
            var newBlank = Row(controller, AttributeKey.Null);
            Check(!Keys(newBlank).Contains(AttributeKey.IonicStrength), "duplicate ionic strength allowed");
            // Select the placeholder item, which has tag 0 (AttributeKey.Null).
            Select(blank, "KeySelectionControl", (int)AttributeKey.Null);
            Check(Keys(newBlank).Contains(AttributeKey.IonicStrength), "changed-away key unavailable");
            Check(events == 0, "menu refresh raised a type-change event");
            Check(Field<NSPopUpButton>(buffer, "KeySelectionControl").SelectedTag == (int)AttributeKey.Buffer, "selected key changed");
            Apply(controller);
            Check(controller.Dismissed, "Apply did not complete");
            Same(exact, Stored(data, AttributeKey.PreboundLigandConc).ParameterValue);
        }
    }

    static void ReplacementAndCancel()
    {
        var data = Data();
        using (var controller = Open(data))
        {
            Edit(controller);
            var competitor = Row(controller, AttributeKey.PreboundLigandConc);
            Select(competitor, "KeySelectionControl", (int)AttributeKey.Species);
            Check(Field<NSTextField>(competitor, "StringField").StringValue == "", "replaced row kept old text");
            Remove(Row(controller, AttributeKey.Buffer));
            Check(!Rows(controller).Any(r => r.Option.Key == AttributeKey.Buffer), "removed row remains");
            Check(Field<NSTextField>(Row(controller, AttributeKey.Species), "StringField").StringValue == "Edited species", "unrelated draft lost");
            Buttons(controller.View).Single(b => b.Title == "Cancel").PerformClick(controller);
            Check(controller.Dismissed, "Cancel did not complete");
            Original(data);
        }
    }

    static void EmptyAttributes()
    {
        using (var controller = Open(new ExperimentData("Empty")))
        {
            Check(!Field<NSTextField>(controller, "emptyAttributesLabel").Hidden, "empty label hidden");
            Add(controller);
            Check(Rows(controller).Count == 1, "new row missing");
            Check(Field<NSTextField>(controller, "emptyAttributesLabel").Hidden, "empty label remains");
            Remove(Row(controller, AttributeKey.Null));
            Check(Rows(controller).Count == 0, "removed final row remains");
            Check(!Field<NSTextField>(controller, "emptyAttributesLabel").Hidden, "empty label missing");
        }
    }

    static void Edit(Controller controller)
    {
        var buffer = Row(controller, AttributeKey.Buffer);
        Field<NSTextField>(buffer, "parameterValueField").StringValue = "50";
        Field<NSTextField>(buffer, "DoubleField").StringValue = "8.15";
        Select(buffer, "EnumPopUpControl", (int)BufferKind.Hepes);
        var salt = Row(controller, AttributeKey.Salt);
        Field<NSTextField>(salt, "parameterValueField").StringValue = "175";
        Select(salt, "EnumPopUpControl", (int)Salt.KCl);
        var species = Row(controller, AttributeKey.Species);
        Field<NSTextField>(species, "StringField").StringValue = "Edited species";
        Select(species, "EnumPopUpControl", (int)ExperimentSpeciesLocation.Syringe);
        Field<NSTextField>(Row(controller, AttributeKey.PreboundLigandConc), "parameterValueField").StringValue = "31.25";
        Field<NSTextField>(Row(controller, AttributeKey.PreboundLigandConc), "parameterErrorField").StringValue = "2.5";
    }

    static void Original(ExperimentData data)
    {
        Check(Stored(data, AttributeKey.Buffer).ParameterValue.Value == 0.02, "live buffer concentration changed before Apply");
        Check(Stored(data, AttributeKey.Buffer).DoubleValue == 7.4, "live pH changed before Apply");
        Check(Stored(data, AttributeKey.Buffer).IntValue == (int)BufferKind.Tris, "live buffer selection changed");
        Check(Stored(data, AttributeKey.Species).StringValue == "Original species", "live species changed");
        Check(Stored(data, AttributeKey.PreboundLigandConc).ParameterValue.Value == 12.345678901e-6, "live competitor changed");
    }

    static void Preset(ExperimentData data, bool pbs)
    {
        var buffers = data.Attributes.Where(a => a.Key == AttributeKey.Buffer).ToArray();
        Check(buffers.Select(a => a.IntValue).SequenceEqual(pbs ? new[] { (int)BufferKind.SodiumPhosphate, (int)BufferKind.PotassiumPhosphate } : new[] { (int)BufferKind.Tris }), "wrong preset buffer identities/order");
        Check(buffers.Select(a => a.ParameterValue.Value).SequenceEqual(pbs ? new[] { 0.01, 0.0018 } : new[] { 0.025 }), "wrong preset buffer concentrations");
        Check(buffers.All(a => a.DoubleValue == 7.4), "wrong preset pH");
        var salts = data.Attributes.Where(a => a.Key == AttributeKey.Salt).ToArray();
        Check(salts.Select(a => a.IntValue).SequenceEqual(new[] { (int)Salt.NaCl, (int)Salt.KCl }), "wrong preset salt identities/order");
        Check(salts.Select(a => a.ParameterValue.Value).SequenceEqual(new[] { 0.137, 0.0027 }), "wrong preset salt concentrations");
    }

    static ExperimentData Data()
    {
        var data = new ExperimentData("Draft regression") { CellVolume = 200e-6,
            CellConcentration = new FloatWithError(20e-6), SyringeConcentration = new FloatWithError(400e-6),
            MeasuredTemperature = 25, DateSource = ExperimentDateSource.DataFile };
        foreach (var key in new[] { AttributeKey.Buffer, AttributeKey.Salt, AttributeKey.Species,
            AttributeKey.PreboundLigandConc, AttributeKey.IonicStrength, AttributeKey.Null })
            data.Attributes.Add(ExperimentAttribute.FromKey(key));
        Stored(data, AttributeKey.Buffer).IntValue = (int)BufferKind.Tris;
        Stored(data, AttributeKey.Buffer).DoubleValue = 7.4;
        Stored(data, AttributeKey.Buffer).ParameterValue = new FloatWithError(0.02);
        Stored(data, AttributeKey.Salt).IntValue = (int)Salt.NaCl;
        Stored(data, AttributeKey.Salt).ParameterValue = new FloatWithError(0.15);
        Stored(data, AttributeKey.Species).StringValue = "Original species";
        Stored(data, AttributeKey.PreboundLigandConc).ParameterValue = new FloatWithError(12.345678901e-6, 1.23456789e-6, 8e-6, 20e-6);
        Stored(data, AttributeKey.IonicStrength).ParameterValue = new FloatWithError(0.123456789, 0.012345678, 0.1, 0.15);
        RawDataReader.ProcessInjections(data, DilutionMethod.MicroCal);
        return data;
    }

    static Controller Open(ExperimentData data)
    {
        ExperimentDetailsPopoverController.Data = data;
        var controller = new Controller { View = new NSView(new CGRect(0, 0, 720, 600)) };
        controller.ViewDidLoad();
        return controller;
    }
    static void ChangeList(Controller controller, string action)
    {
        if (action == "add") Add(controller);
        else if (action == "remove") Remove(Row(controller, AttributeKey.IonicStrength));
        else if (action == "change-key") Select(Row(controller, AttributeKey.IonicStrength), "KeySelectionControl", (int)AttributeKey.Null);
        else Select(Row(controller, AttributeKey.Buffer), "EnumPopUpControl", (int)(action == "PBS" ? BufferKind.PBS : BufferKind.TBS));
    }
    static void Select(ExperimentAttributeView row, string name, int tag)
    {
        var popup = Field<NSPopUpButton>(row, name);
        Check(popup.SelectItemWithTag(tag), "missing popup choice " + tag);
        Check(popup.SendAction(popup.Action, popup.Target), "popup action did not dispatch");
    }
    static IEnumerable<NSButton> Buttons(NSView view) => view.Subviews.SelectMany(child => Buttons(child)).Concat(view is NSButton ? new[] { (NSButton)view } : new NSButton[0]);
    static void Add(Controller controller) => Field<NSButton>(controller, "AddAttributeButton").PerformClick(controller);
    static void Remove(ExperimentAttributeView row) => row.Subviews.OfType<NSButton>().First(b => !(b is NSPopUpButton)).PerformClick(row);
    static void Apply(Controller controller) => Buttons(controller.View).Single(b => b.Title == "Apply").PerformClick(controller);
    static List<ExperimentAttributeView> Rows(Controller controller) => Field<List<ExperimentAttributeView>>(controller, "attributeViews");
    static ExperimentAttributeView Row(Controller controller, AttributeKey key) => Rows(controller).First(r => r.Option.Key == key);
    static ExperimentAttribute Stored(ExperimentData data, AttributeKey key) => data.Attributes.First(a => a.Key == key);
    static AttributeKey[] Keys(ExperimentAttributeView row) => Field<NSPopUpButton>(row, "KeySelectionControl").Menu.Items.Select(i => (AttributeKey)(int)i.Tag).ToArray();
    static T Field<T>(object target, string name)
    {
        var type = target is Controller ? typeof(ExperimentDetailsPopoverController) : target.GetType();
        var field = type.GetField(name, Private);
        return (T)(field != null ? field.GetValue(target) : type.GetProperty(name, Private).GetValue(target, null));
    }
    static void Same(FloatWithError expected, FloatWithError actual)
    {
        Check(expected.Value == actual.Value && expected.SD == actual.SD, "untouched value/SD lost precision");
        Check(expected.Lower == actual.Lower && expected.Upper == actual.Upper && expected.IsAsymmetric == actual.IsAsymmetric, "untouched uncertainty changed");
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
