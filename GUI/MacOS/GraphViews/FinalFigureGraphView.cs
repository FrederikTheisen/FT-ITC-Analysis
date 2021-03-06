// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;
using CoreGraphics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace AnalysisITC
{
	public partial class FinalFigureGraphView : NSView
	{
        public static event EventHandler Invalidated;

        public static event EventHandler PlotSizeChanged;

        static EnergyUnit energyUnit = EnergyUnit.KiloJoule; 
        public static EnergyUnit EnergyUnit
        {
            get => energyUnit;
            set
            {
                if (value.IsSI()) energyUnit = EnergyUnit.KiloJoule; //TODO update this vlaue based on program preferences
                else energyUnit = EnergyUnit.KCal;
            }
        }

        static string poweraxistitle = "";
        public static string PowerAxisTitle
        {
            get => poweraxistitle != "" ? poweraxistitle : "Differential Power (<unit>)";
            set
            {
                if (value == "") return;
                if (!value.Contains("<unit>")) value += " (<unit>)";

                poweraxistitle = value;
            }
        }
        public static bool PowerAxisTitleIsChanged => poweraxistitle == "";

        static string timeaxistitle = "";
        public static string TimeAxisTitle
        {
            get => timeaxistitle != "" ? timeaxistitle : "Time (<unit>)";
            set
            {
                if (value == "") return;
                if (!value.Contains("<unit>")) value += " (<unit>)";

                timeaxistitle = value;
            }
        }
        public static bool TimeAxisTitleIsChanged => timeaxistitle == "";

        static string enthalpyaxistitle = "";
        public static string EnthalpyAxisTitle
        {
            get => enthalpyaxistitle != "" ? enthalpyaxistitle : "<unit> of injectant";
            set
            {
                if (value == "") return;
                if (!value.Contains("<unit>")) value = "<unit> " + value;

                enthalpyaxistitle = value;
            }
        }
        public static bool EnthalpyAxisTitleAxisTitleIsChanged => enthalpyaxistitle == "";

        static string molarratioaxistitle = "";
        public static string MolarRatioAxisTitle
        {
            get => molarratioaxistitle != "" ? molarratioaxistitle : "Molar Ratio";
            set => molarratioaxistitle = value;
        }
        public static bool MolarRatioAxisTitleIsChanged => molarratioaxistitle == "";

        public static float Width { get; set; } = 6;
        public static float Height { get; set; } = 10;

        public static bool SanitizeTicks { get; set; } = true;
        public static int FitXTickCount { get; set; } = 7;
        public static int FitYTickCount { get; set; } = 7;
        public static int DataXTickCount { get; set; } = 7;
        public static int DataYTickCount { get; set; } = 7;

        public static bool UseUnifiedHeatAxis { get; set; } = false;
        public static bool UseUnifiedMolarRatioAxis { get; set; } = false;
        public static bool DrawZeroLine { get; set; } = true;
        public static bool ShowErrorBars { get; set; } = true;
        public static bool ShowBadDataErrorBars { get; set; } = false;
        public static bool DrawConfidence { get; set; } = true;
        public static bool DrawFitParameters { get; set; } = false;
        public static bool ShowBadData { get; set; } = true;
        public static float SymbolSize { get; set; } = CGGraph.SymbolSize;
        public static int SymbolShape { get; set; } = 0;

        public static bool UnifiedPowerAxis { get; set; } = false;
        public static bool DrawBaseline { get; set; } = false;
        public static TimeUnit TimeAxisUnit { get; set; } = TimeUnit.Minute;

        public static void Invalidate() => Invalidated?.Invoke(null, null);

        FinalFigure graph;
        public CGPoint Center { get; set; } = new CGPoint();

        public FinalFigureGraphView (IntPtr handle) : base (handle)
		{
            DataManager.SelectionDidChange += DataManager_SelectionDidChange;
            Invalidated += FinalFigureGraphView_Invalidated;

            LayerContentsRedrawPolicy = NSViewLayerContentsRedrawPolicy.OnSetNeedsDisplay;
		}

        private void FinalFigureGraphView_Invalidated(object sender, EventArgs e)
        {
            InitializeGraph();

            this.NeedsDisplay = true;
        }

        private void DataManager_SelectionDidChange(object sender, ExperimentData e)
        {
            InitializeGraph();

            this.NeedsDisplay = true;
        }

        public static FinalFigure SetupForExport(ExperimentData experiment)
        {
            var _ = (new FinalFigure(experiment, new NSView(new CGRect(0, 0, 10, 10)))).PrintBox;

            var _graph = new FinalFigure(experiment, new NSView(_))
            {
                PlotDimensions = new CGSize(Width, Height),
                SanitizeTicks = SanitizeTicks,

                PowerAxisTitle = PowerAxisTitle,
                TimeAxisTitle = TimeAxisTitle,
                UseUnifiedDataAxes = UnifiedPowerAxis,
                ShouldDrawBaseline = DrawBaseline,

                EnthalpyAxisTitle = EnthalpyAxisTitle,
                MolarRatioAxisTitle = MolarRatioAxisTitle,
                UseUnifiedAnalysisAxes = UseUnifiedHeatAxis,
                //graph.UseUnifiedMolarRatioAxis = UseUnifiedMolarRatioAxis;
                ShowBadDataPoints = ShowBadData,
                ShowBadDataErrorBars = ShowBadDataErrorBars,
                ShowErrorBars = ShowErrorBars,
                DrawConfidence = DrawConfidence,
                DrawZeroLine = DrawZeroLine,
                DrawFitParameters = DrawFitParameters,
                SymbolShape = (CGGraph.SymbolShape)SymbolShape,
                SymbolSize = SymbolSize,
            };

            _graph.SetTimeUnit(TimeAxisUnit);
            _graph.SetEnergyUnit(EnergyUnit);
            _graph.SetTickNumber(DataXTickCount, DataYTickCount, FitXTickCount, FitYTickCount);

            return _graph;
        }

        public static void Export(bool all)
        {
            var datas = all ? DataManager.IncludedData : new ExperimentData[] { DataManager.Current };

            var dlg = new NSOpenPanel();
            dlg.Title = "Save PDF File";
            dlg.AllowedFileTypes = new string[] { "pdf" };
            dlg.CanChooseDirectories = true;
            dlg.CanCreateDirectories = true;
            dlg.CanChooseFiles = false;


            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, (result) =>
            {
                if (result == 1)
                {
                    foreach (var data in datas)
                    {
                        var g = FinalFigureGraphView.SetupForExport(data);
                        var filename = Path.GetFileNameWithoutExtension(data.FileName);

                        //= new NSUrl(dlg.Url.RelativePath  + "/" + filename + ".pdf");

                        var path = NSUrl.CreateFileUrl(dlg.Url.RelativePath + "/" + filename + ".pdf", null);

                        //var path = NSUrl.CreateFileUrl(new string[] { dlg.Url, filename, ".pdf" });
                        Console.WriteLine(path);

                        var x = new CGContextPDF(path);
                        x.BeginPage(new CGRect(new CGPoint(0, 0), g.PrintBox.Size));
                        g.Draw(x, new CGPoint(g.PrintBox.Width / 2, g.PrintBox.Height / 2));
                        x.EndPage();
                        x.Close();
                        path.Dispose();
                    }
                }
            });


        }

        void InitializeGraph()
        {
            if (StateManager.CurrentState != ProgramState.Publish) return;
            if (DataManager.Current == null) return;
            graph = new FinalFigure(DataManager.Current, this)
            {
                PlotDimensions = new CGSize(Width, Height),
                SanitizeTicks = SanitizeTicks,

                PowerAxisTitle = PowerAxisTitle,
                TimeAxisTitle = TimeAxisTitle,
                UseUnifiedDataAxes = UnifiedPowerAxis,
                ShouldDrawBaseline = DrawBaseline,

                EnthalpyAxisTitle = EnthalpyAxisTitle,
                MolarRatioAxisTitle = MolarRatioAxisTitle,
                UseUnifiedAnalysisAxes = UseUnifiedHeatAxis,
                //graph.UseUnifiedMolarRatioAxis = UseUnifiedMolarRatioAxis;
                ShowBadDataPoints = ShowBadData,
                ShowBadDataErrorBars = ShowBadDataErrorBars,
                ShowErrorBars = ShowErrorBars,
                DrawConfidence = DrawConfidence,
                DrawZeroLine = DrawZeroLine,
                DrawFitParameters = DrawFitParameters,
                SymbolShape = (CGGraph.SymbolShape)SymbolShape,
                SymbolSize = SymbolSize,
            };

            graph.SetTimeUnit(TimeAxisUnit);
            graph.SetEnergyUnit(EnergyUnit);
            graph.SetTickNumber(DataXTickCount, DataYTickCount, FitXTickCount, FitYTickCount);

            SetFrameSize(graph.PrintBox.Size); 

            PlotSizeChanged?.Invoke(graph, null);
        }

        public void Export()
        {
            //Print(this);

            var path = NSFileManager.DefaultManager.GetUrl(NSSearchPathDirectory.DesktopDirectory, NSSearchPathDomain.All, NSUrl.FromFilename("test.pdf"), true, out NSError error);

            var url = NSUrl.FromFilename(new NSUrl("test.pdf", path.Path).Path);

            var x = new CGContextPDF(url);
            x.BeginPage(graph.PrintBox);

            graph.Draw(x, new CGPoint(Frame.Width / 2, Frame.Height / 2));
            x.EndPage();
            x.Close();
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            if (StateManager.CurrentState != ProgramState.Publish) return;
            if (DataManager.Current == null) return;
            if (graph == null) { InitializeGraph(); }

            base.DrawRect(dirtyRect);

            var cg = NSGraphicsContext.CurrentContext.CGContext;

            graph.Draw(cg, new CGPoint(Frame.Width / 2, Frame.Height / 2));
        }
    }
}
