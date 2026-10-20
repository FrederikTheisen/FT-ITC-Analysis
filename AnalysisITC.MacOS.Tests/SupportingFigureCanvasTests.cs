using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AppKit;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.UI.MacOS.Drawing;

static class SupportingFigureCanvasTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Assembly App = typeof(FinalFigureGraphView).Assembly;

    static int Main(string[] args)
    {
        NSApplication.Init();
        DataManager.Clear(DataClearMode.ResetSession);
        try
        {
            var first = new ExperimentData("first.itc");
            var second = new ExperimentData("second.itc");
            DataManager.AddData(new ITCDataContainer[] { first, second });
            var state = new SupportingFigureSelectionState();
            var window = Window(first, state);
            var composition = Composition(window);
            composition.Clear();
            composition.AddRange(new[] { second, first });
            Field<NSTextField>(window, "plotWidthField").StringValue = "6";
            Field<NSTextField>(window, "plotHeightField").StringValue = "8";
            Field<NSTextField>(window, "columnsField").StringValue = "2";
            Field<NSTextField>(window, "rowsField").StringValue = "4";
            Field<NSTextField>(window, "fontSizeField").StringValue = "9";
            Field<NSTextField>(window, "symbolSizeField").StringValue = "5";
            Field<NSSegmentedControl>(window, "strokeWidthControl").SelectedSegment = 0;
            Field<NSSwitch>(window, "panelLettersSwitch").State = 0;
            Field<NSSwitch>(window, "panelTitlesSwitch").State = 1;
            Field<NSSwitch>(window, "groupResultsSwitch").State = 0;
            Field<NSSwitch>(window, "informationBoxesSwitch").State = 0;
            Field<NSPopUpButton>(window, "zoomPopup").SelectItem(0);
            Close(window);

            var reopened = Window(first, state);
            Check(Composition(reopened).SequenceEqual(new[] { second, first }), "Reopening lost figure order");
            var options = Options(reopened);
            Check(options.PlotWidthCentimeters == 6 && options.PlotHeightCentimeters == 8, "Reopening lost plot size");
            Check(options.Columns == 2 && options.Rows == 4, "Reopening lost grid");
            Check(options.FontSize == 9 && options.SymbolSize == 5 && options.StrokeWidth == 0.5, "Reopening lost typography or line weight");
            Check(!options.ShowPanelLetters && options.ShowPanelTitles && !options.GroupResultFigures
                && !options.ShowInformationBoxes, "Reopening lost display settings");
            Check(Field<NSPopUpButton>(reopened, "zoomPopup").IndexOfSelectedItem == 0, "Reopening lost Fit zoom");
            Field<NSPopUpButton>(reopened, "zoomPopup").SelectItem(2);
            Composition(reopened).Clear();
            Close(reopened);
            var empty = Window(first, state);
            Check(Composition(empty).Count == 0, "Reopening lost the empty composition");
            Check(Options(empty).PlotWidthCentimeters == 6 && Options(empty).Rows == 4, "Empty composition lost settings");
            Check(Field<NSPopUpButton>(empty, "zoomPopup").IndexOfSelectedItem == 2, "Reopening lost 50% zoom");
            Close(empty);

            DataManager.Clear(DataClearMode.ResetSession);
            DataManager.AddData(first);
            var replacement = Window(first, state);
            Check(Composition(replacement).SequenceEqual(new[] { first }), "New document retained old selection");
            Check(Options(replacement).PlotWidthCentimeters == 5 && Options(replacement).Columns == 3,
                "New document retained old settings");
            Check(Field<NSPopUpButton>(replacement, "zoomPopup").IndexOfSelectedItem == 4, "New document retained old zoom");
            Close(replacement);

            var directory = Environment.GetEnvironmentVariable("FTITC_SUPPORTING_FIGURE_QA_DIRECTORY");
            var temporaryOutput = string.IsNullOrEmpty(directory);
            if (temporaryOutput) directory = Path.Combine(Path.GetTempPath(), "native-supporting-culture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            {
                var rendererType = App.GetType("AnalysisITC.UI.MacOS.Drawing.CoreGraphicsFigureCanvasRenderer", true);
                var renderer = Activator.CreateInstance(rendererType, true);
                first.DataPoints.Add(new DataPoint(0f, 0f, 25f));
                first.DataPoints.Add(new DataPoint(30f, 1.25f, 25f));
                first.DataPoints.Add(new DataPoint(60f, -0.75f, 25f));
                var originalCulture = CultureInfo.CurrentCulture;
                try
                {
                    foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
                    {
                        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                        var document = PublicationFigureCanvasBuilder.Build(new[] { first },
                            new PublicationFigureOptions { ShowThermogram = true, ShowAxisTitles = true },
                            new PublicationFigureCanvasOptions { Columns = 1, Rows = 1 });
                        var plan = rendererType.GetMethod("CreatePlan", Instance).Invoke(renderer, new object[] { document });
                        using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData", Instance).Invoke(renderer, new[] { plan }))
                        {
                            Check(pdf.Length > 0, cultureName + " supporting figure PDF is empty");
                            File.WriteAllBytes(Path.Combine(directory, "native-culture-" + cultureName + ".pdf"), pdf.ToArray());
                        }
                    }
                }
                finally { CultureInfo.CurrentCulture = originalCulture; }
                if (!temporaryOutput)
                {
                    foreach (var size in new[] { 5, 8, 10, 24 })
                    {
                        var document = PublicationFigureCanvasBuilder.Build(new[] { first },
                            new PublicationFigureOptions { ShowThermogram = false, ShowAxisTitles = false },
                            new PublicationFigureCanvasOptions
                            {
                                Columns = 1, Rows = 1, FontSize = size, ShowPanelTitles = true,
                                ShowInformationBoxes = false
                            });
                        var plan = rendererType.GetMethod("CreatePlan", Instance).Invoke(renderer, new object[] { document });
                        using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData", Instance).Invoke(renderer, new[] { plan }))
                            File.WriteAllBytes(Path.Combine(directory, "native-base-" + size + ".pdf"), pdf.ToArray());
                    }
                }
                if (temporaryOutput) Directory.Delete(directory, true);
            }
            Console.WriteLine("Native supporting figure close/reopen checks passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
        }
    }

    static NSWindowController Window(ITCDataContainer selected, SupportingFigureSelectionState state)
        => (NSWindowController)Activator.CreateInstance(
            App.GetType("AnalysisITC.SupportingFigureCanvasWindowController", true),
            new object[] { new PublicationFigureOptions(), selected, state });

    static List<ITCDataContainer> Composition(NSWindowController window)
        => (List<ITCDataContainer>)window.GetType().GetField("composition", Instance).GetValue(window);

    static T Field<T>(NSWindowController window, string name)
        => (T)window.GetType().GetField(name, Instance).GetValue(window);

    static PublicationFigureCanvasOptions Options(NSWindowController window)
    {
        var arguments = new object[] { null, null };
        Check((bool)window.GetType().GetMethod("TryCurrentCanvasOptions", Instance).Invoke(window, arguments),
            "Restored options are invalid");
        return (PublicationFigureCanvasOptions)arguments[0];
    }

    static void Close(NSWindowController window)
    {
        window.Window.PerformClose(window);
        window.Dispose();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
