using System;
using AppKit;
using Foundation;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;

namespace AnalysisITC
{
    public class Exporter
    {
        static char Delimiter = ',';
        static char BlankChar = ' ';
        static ExportAccessoryViewSettings ExportSettings;

        public static void Export(ExportType type)
        {
            ExportSettings = type == ExportType.Data
                ? ExportAccessoryViewSettings.DataDefault()
                : ExportAccessoryViewSettings.PeaksDefault();

            var storyboard = NSStoryboard.FromName("Main", null);
            var viewController = (ExportAccessoryViewController)storyboard.InstantiateControllerWithIdentifier("ExportAccessoryViewController");
            viewController.Setup(ExportSettings);

            // Use OpenPanel as "choose destination folder"
            var dlg = NSOpenPanel.OpenPanel;
            dlg.Title = "Export";
            dlg.AccessoryView = viewController.View;
            dlg.RespondsToSelector(new ObjCRuntime.Selector("setAccessoryViewDisclosed:"));
            dlg.PerformSelector(new ObjCRuntime.Selector("setAccessoryViewDisclosed:"), NSNumber.FromBoolean(true), 0);
            dlg.CanChooseDirectories = true;
            dlg.CanChooseFiles = false;
            dlg.AllowsMultipleSelection = false;
            dlg.CanCreateDirectories = true;

            dlg.Prompt = "Export";

            var parent = NSApplication.SharedApplication.MainWindow;

            dlg.BeginSheet(parent, async (result) =>
            {
                if (result != (int)NSModalResponse.OK) return;

                var folderUrl = dlg.Url;
                if (folderUrl == null) return;

                // Create the list of output paths (could be many later).
                var outputPaths = GetPlannedOutputPaths(folderUrl.Path, ExportSettings);

                // Overwrite control (single prompt)
                if (!ConfirmOverwriteIfNeeded(parent, outputPaths))
                    return;

                StatusBarManager.StartInderminateProgress();

                try
                {
                    var path = Path.GetDirectoryName(outputPaths[0]);

                    StatusBarManager.SetStatusScrolling($"Saving to {path}...");

                    switch (ExportSettings.Export)
                    {
                        case ExportType.Data:
                            await WriteDataFile(path);
                            break;

                        case ExportType.Peaks:
                            await WritePeakFile(path, ExportColumns.SelectionMinimal);
                            break;

                        case ExportType.ITCsim:
                            await WriteITCsimFile(path);
                            break;

                        default:
                        case ExportType.CSV:
                            await WritePeakFile(path, ExportSettings.Columns);
                            break;

                        case ExportType.MicroCal:
                            await WriteMicroCalExportFile(path);
                            break;

                        case ExportType.PYTC:
                            await WritePytcExportFile(path);
                            break;
                    }
                }
                finally
                {
                    // If you have a "stop progress" method, call it here.
                }
            });
        }

        static List<string> GetPlannedOutputPaths(string folderPath, ExportAccessoryViewSettings settings)
        {
            // If you export many files, return many here based on data names.
            string ext = settings.Export.GetProperties().Extension;

            var paths = new List<string>();
            foreach (var data in settings.Data)
            {
                var fileName = $"{Path.GetFileNameWithoutExtension(data.Name)}.{ext}";

                paths.Add(Path.Combine(folderPath, fileName));
            }

            return paths;
        }

        static bool ConfirmOverwriteIfNeeded(NSWindow parent, IEnumerable<string> outputPaths)
        {
            var existing = outputPaths.Where(File.Exists).Distinct().ToList();
            if (existing.Count == 0) return true;

            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Warning,
                MessageText = "File already exists.",
                InformativeText = existing.Count == 1
                    ? $"This export will overwrite:\n{Path.GetFileName(existing[0])}"
                    : $"This export will overwrite {existing.Count} files."
            };

            alert.AddButton("Overwrite");
            alert.AddButton("Cancel");

            var response = parent != null ? alert.RunSheetModal(parent) : alert.RunModal();
            return response == (int)NSAlertButtonReturn.First;
        }

        static void SetDelimiter(NSUrl url)
        {
            switch (url.PathExtension)
            {
                case "csv": Delimiter = ','; BlankChar = ' ';  break;
                case "txt": Delimiter = ' '; BlankChar = '.';  break;
            }
        }

        static string GetDataFileName()
        {
            return Path.GetFileNameWithoutExtension(ExportSettings.Data[0].Name);
        }

        static async Task WriteDataFile(string path)
        {
            await Task.Run(async () =>
            {
                var lines = ExportSettings.UnifyTimeAxis ? GetUnifiedDataLines(ExportSettings.Data) : GetDataLines(ExportSettings.Data);

                var filename = GetDataFileName() + ExportSettings.Export.GetProperties().DotExtension();

                using (var writer = new StreamWriter(Path.Combine(path, filename)))
                {
                    foreach (var line in lines)
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting data file", 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static List<string> GetUnifiedDataLines(List<ExperimentData> data)
        {
            //Get time axis
            var mostcommonstep = FindMostCommonStep(data);
            var newvalues = new Dictionary<string, List<float>>();
            var xaxis = new List<float>();
            var min = data.Min(d => d.DataPoints.First().Time);
            var max = data.Max(d => d.DataPoints.Last().Time);

            //Add points to new x axis
            for (float i = min; i <= max; i += mostcommonstep) xaxis.Add(i); 

            //Implemented unified x axis
            foreach (var dat in data)
            {
                var dps = ExportSettings.ExportBaselineCorrectDataPoints ? dat.BaseLineCorrectedDataPoints : dat.DataPoints;
                var points = new List<float>();
                var prevtime = 0f;
                foreach (var t in xaxis)
                {
                    var group = dps.Where(dp => dp.Time > prevtime && dp.Time <= t);
                    float newdp;

                    if (group.Count() == 0) //Are we averaging a number of datapoints?
                    {
                        if (prevtime >= dps.Last().Time || t < dps.First().Time) newdp = float.NaN; //Outside data set?
                        else //Interpolate from datapoints
                        {
                            var p1 = dps.Where(dp => dp.Time > prevtime).First();
                            var p2 = dps.Where(dp => dp.Time < t).Last();

                            var weight = (t - p2.Time) / (p2.Time - p1.Time);

                            newdp = weight * p2.Power + (1 - weight) * p1.Power;
                        }
                    }
                    //Average datapoints in window. Probably not 100% accurate.
                    else newdp = dps.Where(dp => dp.Time > prevtime && dp.Time <= t).Select(dp => dp.Power).Average();

                    points.Add(newdp);

                    prevtime = t;
                }

                newvalues[dat.Name] = points;
            }

            var lines = new List<string>();
            var header = "time" + Delimiter;
            foreach (var dat in newvalues) header += dat.Key + Delimiter;
            lines.Add(header.TrimEnd(Delimiter));

            for (int i = 0; i < xaxis.Count; i++)
            {
                var x = xaxis[i];
                var line = x.ToString("F1") + Delimiter;

                foreach (var dat in newvalues)
                {
                    var v = dat.Value[i];
                    if (float.IsNaN(v)) line += BlankChar + Delimiter;
                    else line += dat.Value[i].ToString() + Delimiter;
                }

                lines.Add(line.TrimEnd(Delimiter));
            }

            return lines;
        }

        static List<string> GetDataLines(List<ExperimentData> data)
        {
            var lines = new List<string>();
            var header = "";
            foreach (var dat in data) header += "time" + Delimiter + dat.Name + Delimiter;
            lines.Add(header.TrimEnd(Delimiter));

            int index = 0;

            while (data.Any(d => d.DataPoints.Count > index))
            {
                string line = "";

                foreach (var d in data)
                {
                    var dps = ExportSettings.ExportBaselineCorrectDataPoints ? d.BaseLineCorrectedDataPoints : d.DataPoints;

                    if (dps.Count > index)
                    {
                        var dp = dps[index];
                        line += dp.Time.ToString() + Delimiter + dp.Power.ToString() + Delimiter;
                    }
                    else line += BlankChar.ToString() + Delimiter + BlankChar + Delimiter;
                }

                lines.Add(line.TrimEnd(Delimiter));

                index++;
            }

            return lines;
        }

        static float FindMostCommonStep(List<ExperimentData> data)
        {
            // Flatten the datasets into a single list of x-axis values
            List<float> allXValues = new List<float>();
            foreach (var dataset in data.Select(d => d.DataPoints))
            {
                allXValues.AddRange(dataset.Select(dp => dp.Time));
            }

            // Determine the most frequently used step
            var stepFrequencies = allXValues
                .Select((x, i) => i > 0 ? x - allXValues[i - 1] : 0)
                .GroupBy(step => step)
                .OrderByDescending(group => group.Count());
            float mostCommonStep = stepFrequencies.First().Key;

            return mostCommonStep;
        }

        static async Task WritePeakFile(string path, ExportColumns columns)
        {
            await Task.Run(async () =>
            {
                foreach (var data in ExportSettings.Data)
                {
                    var dataname = data.Name;
                    var ext = ExportType.Peaks.GetProperties().DotExtension();
                    var output = Path.Combine(path, dataname + ext);

                    var lines = GetColumns(data, columns);

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting peak file", 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static async Task WriteITCsimFile(string path)
        {
            await Task.Run(async () =>
            {
                foreach (var data in ExportSettings.Data)
                {
                    var dataname = data.Name;
                    var ext = ExportType.ITCsim.GetProperties().DotExtension();
                    var output = Path.Combine(path, dataname + ext);

                    var lines = GetColumns(data, ExportColumns.SelectionITCsim);

                    lines.AddRange(GetMetaData(data));

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting " + Utilities.MarkdownStrings.ITCsimName, 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static async Task WriteMicroCalExportFile(string path)
        {
            await Task.Run(async () =>
            {
                foreach (var data in ExportSettings.Data)
                {
                    var dataname = data.Name;
                    var ext = ExportType.MicroCal.GetProperties().DotExtension();
                    var output = Path.Combine(path, dataname + ext);

                    var lines = new List<string>()
                    {
                        "DH,INJV,Xt,Mt,XMt,NDH,DY,Fit"
                    };

                    string DH; // Peak area
                    string INJV; // Inj vol
                    string Xt = "0.0"; // Actual titration conc
                    string Mt = (1000 * data.CellConcentration).Value.ToString(); // Actual cell conc
                    string XMt; // Ratio
                    string NDH = "--"; // Enthalpy
                    string DY = "--"; // Fit residual
                    string Fit = "--";

                    foreach (var inj in data.Injections)
                    {
                        string line = "";

                        DH = inj.PeakArea.Value.ToString();
                        INJV = (inj.Volume * 1000000).ToString();
                        XMt = inj.Ratio.ToString();
                        NDH = (inj.PeakArea.Value / (1000000 * inj.InjectionMass)).ToString();
                        if (data.Solution != null && data.Solution.IsValid)
                        {
                            Fit = data.Model.EvaluateEnthalpy(inj.ID, true).ToString();
                        }

                        line = DH + ","
                            + INJV + ","
                            + Xt + ","
                            + Mt + ","
                            + XMt + ","
                            + NDH + ","
                            + DY + ","
                            + Fit;

                        Xt = (1000 * inj.ActualTitrantConcentration).ToString();
                        Mt = (1000 * inj.ActualCellConcentration).ToString();

                        if (data.Solution != null && data.Solution.IsValid)
                        {
                            DY = (inj.Enthalpy - data.Model.EvaluateEnthalpy(inj.ID, true)).ToString();
                        }

                        lines.Add(line);
                    }

                    lines.Add(" ,--," + Xt + "," + Mt + ",--, , , ");

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting file", 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static async Task WritePytcExportFile(string path)
        {
            await Task.Run(async () =>
            {
                foreach (var data in ExportSettings.Data)
                {
                    var dataname = data.Name;
                    var ext = ExportType.PYTC.GetProperties().DotExtension();
                    var output = Path.Combine(path, dataname + ext);

                    var lines = new List<string>
                    {
                        "10", // ?
                        "0," + data.InjectionCount.ToString() + ",0,0,0",
                        data.MeasuredTemperature.ToString("F3") + "," + (1000 * data.CellConcentration).Value.ToString("F4") + "," + (1000 * data.SyringeConcentration).Value.ToString("F4") + "," + (1000 * data.CellVolume).ToString("F5"),
                        "0", // ?
                        "0", // ?
                    };

                    foreach (var inj in data.Injections)
                    {
                        string line = (1000000 * inj.Volume).ToString("F2") + "," + inj.PeakArea.Energy.ToUnit(EnergyUnit.MicroCal).Value.ToString("F5");

                        lines.Add(line);
                    }

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting file for pytc", 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static List<string> GetColumns(ExperimentData data, ExportColumns columns)
        {
            var lines = new List<string>();

            // Build column header
            string header = "";

            for (int i = 1; i < 9999; i *= 2)
            {
                if (!Enum.IsDefined(typeof(ExportColumns), i)) { break; } // We are through the list of valid enums, break
                if (!columns.HasFlag((ExportColumns)i)) { continue; } // Enum not in selection, try next
                if (header.Length > 0) header += Delimiter.ToString();

                header += ExportColumnHandler.GetColumnHeader((ExportColumns)i);
            }

            lines.Add(header);

            // Build file
            for (int j = 0; j < data.InjectionCount; j++)
            {
                var line = "";

                for (int i = 1; i < 9999; i *= 2)
                {
                    if (!Enum.IsDefined(typeof(ExportColumns), i)) { break; } // We are through the list of valid enums, break
                    if (!columns.HasFlag((ExportColumns)i)) { continue; } // Enum not in selection, try next
                    if (line.Length > 0) line += Delimiter.ToString();

                    line += ExportColumnHandler.GetColumnValue((ExportColumns)i, data, j);
                }

                lines.Add(line);
            }

            return lines;
        }

        static List<string> GetMetaData(ExperimentData data)
        {
            var lines = new List<string>
            {
                "#ITCSIM METADATA LIST",
                "#EXPINFO CELLCONC " + (1000000*data.CellConcentration).ToString("F2") + " uM",
                "#EXPINFO SYRINGECONC " + (1000000*data.SyringeConcentration).ToString("F2") + " uM",
                "#EXPINFO CELLVOLUME " + data.CellVolume.ToString("F9") + " L"
            };

            return lines;
        }

        public static void CopyToClipboard(AnalysisResult analysis, ConcentrationUnit kdunit, EnergyUnit eunit, bool usekelvin)
        {
            NSPasteboard.GeneralPasteboard.ClearContents();

            var solution = analysis.Solution;
            var delimiter = ',';
            var lines = new List<string>()
            {
                string.Join(delimiter, Header())
            };

            foreach (var sol in solution.Solutions)
            {
                var line = new List<string>
                {
                    sol.Data.Name,
                    (usekelvin ? sol.TempKelvin : sol.Temp).ToString("F2")
                };

                if (analysis.IsElectrostaticsAnalysisDependenceEnabled)
                    line.Add((1000 * BufferAttribute.GetIonicStrength(sol.Data)).ToString("F2"));

                if (analysis.IsProtonationAnalysisEnabled)
                    line.Add(BufferAttribute.GetProtonationEnthalpy(sol.Data).ToString(eunit, "F1", withunit: false));

                foreach (var par in sol.ReportParameters)
                {
                    switch (par.Key)
                    {
                        case ParameterType.Nvalue1:
                        case ParameterType.Nvalue2:
                            line.Add(par.Value.ToString("F3")); break;
                        case ParameterType.Affinity1:
                        case ParameterType.Affinity2:
                            line.Add(par.Value.AsConcentration(kdunit, withunit: false)); break;
                        default:
                            line.Add(new Energy(par.Value).ToString(eunit, formatter: "G3", withunit: false)); break;
                    }
                }
                lines.Add(string.Join(delimiter, line).Replace('±', delimiter));
            }

            // Add line with averages
            var averageline = new List<string>
            {
                "mean",
                solution.Solutions.Average(sol => usekelvin ? sol.TempKelvin : sol.Temp).ToString("F2"),
            };
            if (analysis.IsElectrostaticsAnalysisDependenceEnabled)
                averageline.Add("-");

            if (analysis.IsProtonationAnalysisEnabled)
                averageline.Add("-");

            foreach (var par in solution.Solutions[0].ReportParameters)
            {
                var avg = new FloatWithError(solution.Solutions.Select(sol => sol.ReportParameters[par.Key]).ToList());

                switch (par.Key)
                {
                    case ParameterType.Nvalue1:
                    case ParameterType.Nvalue2:
                        averageline.Add(avg.ToString("F3")); break;
                    case ParameterType.ApparentAffinity:
                    case ParameterType.Affinity1:
                    case ParameterType.Affinity2:
                        averageline.Add(avg.AsConcentration(kdunit, withunit: false)); break;
                    default:
                        averageline.Add(new Energy(avg).ToString(eunit, formatter: "G3", withunit: false)); break;
                }
            }

            lines.Add(string.Join(delimiter, averageline).Replace('±', delimiter));

            var paste = string.Join(Environment.NewLine, lines);

            NSPasteboard.GeneralPasteboard.SetStringForType(paste, "NSStringPboardType");

            StatusBarManager.SetStatus("Results copied to clipboard", 3333);

            List<string> Header()
            {
                List<string> header = new() { "exp", "temperature" };

                if (analysis.IsElectrostaticsAnalysisDependenceEnabled) header.Add("IS(mM)");
                if (analysis.IsProtonationAnalysisEnabled) header.Add("∆Hbufferprotonation(" + eunit.GetUnit() + ")");

                var options = solution.Solutions[0].ModelOptions;

                foreach (var par in solution.IndividualModelReportParameters)
                {
                    var s = ParameterTypeAttribute.TableHeader(options, par, solution.Solutions[0].ParametersConformingToKey(par).Count > 1, eunit, kdunit.GetName());

                    header.Add(s + "_value");
                    header.Add(s + "_sd");
                }

                return header;
            }
        }

        class ExportColumnHandler
        {
            public static string GetColumnHeader(ExportColumns column)
            {
                switch (column)
                {
                    case ExportColumns.MolarRatio: return "MolarRatio";
                    case ExportColumns.Included: return "Included";
                    case ExportColumns.Peak: return "PeakHeat";
                    case ExportColumns.Fit: return "Fit";
                    case ExportColumns.InjectionVolume: return "InjVolume";
                    case ExportColumns.InjectionDelay: return "InjDelay";
                    case ExportColumns.CellConc: return "[cell]";
                    case ExportColumns.SyrConc: return "[syr]";
                    case ExportColumns.PeakError: return "PeakHeatError";
                    case ExportColumns.IntegrationLength: return "PeakIntegrationLength";
                    case ExportColumns.Temperature: return "Temperature";
                    default: return "unknown_column_selection_" + column.ToString();
                }
            }

            public static string GetColumnValue(ExportColumns column, ExperimentData data, int i)
            {
                if (data == null) throw new Exception("No data selected");
                if (data.Injections == null) throw new Exception("Data does not contain injection information");
                if (data.Injections.Count < i) return "";

                var inj = data.Injections[i];

                switch (column)
                {
                    case ExportColumns.MolarRatio: return inj.Ratio.ToString("F5");
                    case ExportColumns.Included: return inj.Include ? "1" : "0";
                    case ExportColumns.InjectionVolume: return inj.Volume.ToString("E2");
                    case ExportColumns.InjectionDelay: return inj.Delay.ToString();
                    case ExportColumns.CellConc: return inj.ActualCellConcentration.ToString("F8");
                    case ExportColumns.SyrConc: return inj.ActualTitrantConcentration.ToString("F8");
                    case ExportColumns.Peak: return ExportSettings.ExportOffsetCorrected ? inj.OffsetEnthalpy.ToString("F3") : inj.Enthalpy.ToString("F3");
                    case ExportColumns.PeakError: return inj.SD.ToString("F2");
                    case ExportColumns.Temperature: return inj.Temperature.ToString("F2");
                    case ExportColumns.IntegrationLength: return inj.IntegrationEndOffset.ToString("F1");
                    case ExportColumns.Fit:
                        if (data.Solution != null) return data.Model.EvaluateEnthalpy(i, !ExportSettings.ExportOffsetCorrected).ToString("F3");
                        else return BlankChar.ToString();
                    default: return BlankChar.ToString();
                }
            }
        }
    }
}
