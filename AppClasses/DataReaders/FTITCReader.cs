using System;
using System.IO;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.AppClasses.AnalysisClasses.Models;
using System.Threading.Tasks;

namespace DataReaders
{
    class FTITCReader : FTITCFormat
    {
        static List<ITCDataContainer> Data { get; set; }

        public static async Task<ITCDataContainer[]> ReadPath(string path)
        {
            AppEventHandler.PrintAndLog("Loading File " + path, 0);
            var watch = System.Diagnostics.Stopwatch.StartNew();

            Data = new List<ITCDataContainer>();

            using (var reader = (new StreamReader(path)))
            {
                string line;

                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    var startms = watch.ElapsedMilliseconds;
                    AppEventHandler.PrintAndLog($"Read {line} Start: {watch.ElapsedMilliseconds}");

                    var input = line.Split(new[] { ':' }, 3);

                    if (input[0] == "FILE")
                    {
                        if (input[1] == ExperimentHeader) Data.Add(await ReadExperimentDataFile(reader, line));
                        else if (input[1] == TandemExperimentHeader) Data.Add(await ReadTandemExperimentDataFile(reader, line));
                        else if (input[1] == AnalysisResultHeader) Data.Add(await ReadAnalysisResult(reader, line));
                    }

                    AppEventHandler.PrintAndLog($"Total time: {watch.ElapsedMilliseconds - startms}");
                }
            }

            watch.Stop();

            CurrentAccessedAppDocumentPath = path;

            return Data.ToArray();
        }

        static async Task<ExperimentData> ReadTandemExperimentDataFile(StreamReader reader, string firstline)
        {
            AppEventHandler.PrintAndLog("Loading Tandem Experiment Data...", 1);

            string[] a = firstline.Split(new[] { ':' }, 3);
            var exp = new ExperimentData(DecodeText(a[2]));

            StatusBarManager.SetStatus($"Loading {exp.Name}", 0);
            await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.

            ReadExperimentData(reader, exp);

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            return exp;
        }

        static async Task<ExperimentData> ReadExperimentDataFile(StreamReader reader, string firstline)
        {
            AppEventHandler.PrintAndLog("Loading Experiment Data...", 1);

            string[] a = firstline.Split(new[] { ':' }, 3);
            var exp = new ExperimentData(DecodeText(a[2]));

            StatusBarManager.SetStatus($"Loading {exp.Name}", 0);
            await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.

            ReadExperimentData(reader, exp);

            RawDataReader.ProcessInjections(exp);

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            exp.CalculateExperimentHeatDirection();
            return exp;
        }

        static ExperimentData ReadExperimentData(StreamReader reader, ExperimentData exp)
        {
            SolutionInterface sol = null;

            string line;

            while ((line = reader.ReadLine()) != EndFileHeader)
            {
                string[] v = SplitKeyValue(line);
                string key = v[0];
                string value = v.Length > 1 ? v[1] : string.Empty;

                switch (key)
                {
                    case ID: exp.SetID(value); break;
                    case AssignedName: exp.Name = DecodeText(value); break;
                    case Date: exp.Date = DTParse(value); break;
                    case SourceFormat: exp.DataSourceFormat = (ITCDataFormat)IParse(value); break;
                    case Comments: exp.Comments = DecodeText(value); break;
                    case SyringeConcentration: exp.SyringeConcentration = FWEParse(value); break;
                    case CellConcentration: exp.CellConcentration = FWEParse(value); break;
                    case CellVolume: exp.CellVolume = DParse(value); break;
                    case StirringSpeed: exp.StirringSpeed = DParse(value); break;
                    case TargetTemperature: exp.TargetTemperature = DParse(value); break;
                    case MeasuredTemperature: exp.MeasuredTemperature = DParse(value); break;
                    case InitialDelay: exp.InitialDelay = DParse(value); break;
                    case TargetPowerDiff: exp.TargetPowerDiff = DParse(value); break;
                    case FeedBackMode: exp.FeedBackMode = (FeedbackMode)IParse(value); break;
                    case Include: exp.Include = BParse(value); break;
                    case Instrument: exp.Instrument = (ITCInstrument)IParse(value); break;
                    case "LIST" when value == InjectionList:
                        ReadInjectionList(exp, reader); break;
                    case "LIST" when value == DataPointList:
                        ReadDataList(exp, reader); break;
                    case "LIST" when value == ExperimentAttributes:
                        ReadAttributes(exp, reader); break;
                    case "LIST" when value == SegmentList:
                        ReadSegmentList(exp, reader); break;
                    case "OBJECT" when value == Processor:
                        ReadProcessor(exp, reader); break;
                    case "OBJECT" when value == ExperimentSolutionHeader:
                        sol = ReadSolution(reader, reader.ReadLine(), exp);
                        exp.UpdateSolution(sol.Model);
                        break;
                        //case "OBJECT" when v[1] == SolutionHeader: exp.UpdateSolution(ReadSolution(reader, line).Model); break; //Not certain about implementation
                }
            }

            return exp;
        }

        private static void ReadProcessor(ExperimentData exp, StreamReader reader)
        {
            var p = new DataProcessor(exp);

            string line = reader.ReadLine();
            string[] v = SplitKeyValue(line);

            if (v[0] != ProcessorType) return;

            p.InitializeBaseline((BaselineInterpolatorTypes)int.Parse(v[1]));

            while ((line = reader.ReadLine()) != EndObjectHeader)
            {
                v = SplitKeyValue(line);

                switch (v[0])
                {
                    case SplineHandleMode: (p.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)IParse(v[1]); break;
                    case SplineAlgorithm: (p.Interpolator as SplineInterpolator).Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)IParse(v[1]); break;
                    case SplineLocked: if (BParse(v[1])) p.Lock(); break;
                    case SplineFraction: (p.Interpolator as SplineInterpolator).FractionBaseline = FParse(v[1]); break;
                    case "LIST" when v[1] == SplinePointList: ReadSplineList(p.Interpolator as SplineInterpolator, reader); break;
                    case PolynomiumDegree: (p.Interpolator as PolynomialLeastSquaresInterpolator).Degree = IParse(v[1]); break;
                    case PolynomiumLimit: (p.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit = DParse(v[1]); break;
                }
            }

            exp.SetProcessor(p);

            p.ProcessData(replace: false, invalidate: false);
        }

        static void ReadSplineList(SplineInterpolator interpolator, StreamReader reader)
        {
            var splinepoints = new List<SplineInterpolator.SplinePoint>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var _spdat = SplitCsv(line);
                splinepoints.Add(new SplineInterpolator.SplinePoint(DParse(_spdat[0]), DParse(_spdat[1]), IParse(_spdat[2]), DParse(_spdat[3])));
            }

            interpolator.SetSplinePoints(splinepoints) ;
        }

        private static void ReadAttributes(ExperimentData exp, StreamReader reader)
        {
            var attributes = ReadAttributeOptions(reader);

            foreach (var att in attributes)
                exp.Attributes.Add(att);
        }

        private static List<ExperimentAttribute> ReadAttributeOptions(StreamReader reader)
        {
            var options = new List<ExperimentAttribute>();

            AppEventHandler.Print("Reading Attributes...", 1);

            string line;
            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var dat = line.Split(';');

                var opt = ExperimentAttribute.FromKey((AttributeKey)IParse(dat[1]));

                for (int i = 2; i < dat.Length; i++)
                {
                    var d = dat[i].Split(new[] { ':' }, 2);
                    string type = d[0];
                    string val = d.Length > 1 ? d[1] : string.Empty;

                    switch (type)
                    {
                        case "B": opt.BoolValue = BParse(val); break;
                        case "I": opt.IntValue = IParse(val); break;
                        case "D": opt.DoubleValue = DParse(val); break;
                        case "FWE": opt.ParameterValue = FWEParse(val); break;
                        case "S": opt.StringValue = DecodeText(val); break;
                        case "name": opt.OptionName = DecodeText(val); break;
                    }
                }

                AppEventHandler.Print($"{opt.Key} {opt}", 2);

                options.Add(opt);
            }

            return options;
        }

        static void ReadInjectionList(ExperimentData exp, StreamReader reader)
        {
            var injections = new List<InjectionData>();

            string line;

            AppEventHandler.Print("Reading Injections...", 1);
            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var inj = InjectionData.FromFTITCLine(exp, line);
                AppEventHandler.Print(inj.ToString(), 2);
                injections.Add(inj);
            }

            exp.Injections = injections;
        }

        static void ReadDataList(ExperimentData exp, StreamReader reader)
        {
            var datapoints = new List<DataPoint>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var dp = SplitCsv(line);
                datapoints.Add(new DataPoint(FParse(dp[0]), FParse(dp[1]), FParse(dp[2]), shieldt: FParse(dp[3])));
            }

            exp.DataPoints = datapoints;
        }

        static void ReadSegmentList(ExperimentData exp, StreamReader reader)
        {
            string line;

            while ((line = reader.ReadLine()) != EndListHeader) exp.AddSegment(TandemExperimentSegment.FromFile(line));

            exp.InvalidateSegmentLookup();
        }

        static async Task<AnalysisResult> ReadAnalysisResult(StreamReader reader, string firstline)
        {
            AppEventHandler.PrintAndLog("Loading Analysis Result...", 1);

            try
            {
                string line = firstline;
                string[] info = firstline.Split(new[] { ':' }, 3)[2].Split(',').Select(DecodeText).ToArray();
                string comments = "";
                string dateinfo = "";
                string name = "";
                DateTime date = DateTime.Now;

                while (!(line = reader.ReadLine()).Contains(GlobalSolutionHeader))
                {
                    var dat = SplitKeyValue(line);
                    string value = dat.Length > 1 ? dat[1] : string.Empty;

                    switch (dat[0])
                    {
                        case Comments: comments = DecodeText(value); break;
                        case Date: dateinfo = value; break;
                        case AssignedName: name = DecodeText(value); break;
                    }
                }

                if (!string.IsNullOrEmpty(dateinfo)) date = DTParse(dateinfo);

                StatusBarManager.SetStatus($"Loading {(info.Length > 1 ? info[1] : "Analysis Result")}", 0);
                await Task.Delay(1); //Necessary to update UI.

                var sol = ReadGlobalSolution(reader);
                
                string guid = info[0];
                string filename = info.Length > 1 ? info[1] : sol.SolutionName;
                AnalysisResult result = new AnalysisResult(sol);
                result.SetID(guid);
                result.SetFileName(filename);
                result.Name = name;
                result.Comments = comments;
                result.SetDate(date);

                return result;
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog(ex.Message);
                AppEventHandler.PrintAndLog(ex.StackTrace);
                AppEventHandler.DisplayHandledException(new HandledException(HandledException.Severity.Error,"File Reading Error", $"Analysis Result reading error.\nFile: {firstline}"));

                return null;
            }
        }

        static GlobalSolution ReadGlobalSolution(StreamReader reader)
        {
            bool useErrorWeightedFitting = false;

            string line = reader.ReadLine();
            var mdl = (AnalysisModel)IParse(SplitKeyValue(line)[1]);
            GlobalModelFactory factory = new GlobalModelFactory(mdl);
            var datas = new List<ExperimentData>();
            var solutions = new List<SolutionInterface>();
            SolverConvergence legacyConv = null;
            SolverConvergence snapshotConv = null;

            while ((line = reader.ReadLine()) != EndFileHeader)
            {
                var v = line.Split(':');
                switch (v[0])
                {
                    case SolWeightedError: useErrorWeightedFitting = BParse(v[1]); break;
                    case "LIST" when v[1] == DataRef:
                        {
                            string dref;
                            while ((dref = reader.ReadLine()) != EndListHeader)
                            {
                                datas.Add(Data.Find(d => d.UniqueID == dref) as ExperimentData);
                            }

                            factory.InitializeModel(datas);
                        }
                        break;
                    case "LIST" when v[1] == SolConstraints:
                        {
                            string line2;
                            while ((line2 = reader.ReadLine()) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var con = (VariableConstraint)IParse(dat[2]);

                                factory.Model.Parameters.SetConstraintForParameter(par, con);
                            }
                        }
                        break;
                    case "LIST" when v[1] == SolParams:
                        {
                            string line2;
                            while ((line2 = reader.ReadLine()) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var val = DParse(dat[2]);

                                factory.Model.Parameters.AddorUpdateGlobalParameter(par, val);
                            }
                        }
                        break;
                    case "LIST" when v[1] == SolutionList:
                        {
                            string solline;
                            while ((solline = reader.ReadLine()) != EndListHeader)
                            {
                                var sol = ReadSolution(reader, solline);
                                if (sol == null) break;
                                factory.Model.Models.Find(mdl => mdl.Data.UniqueID == sol.Data.UniqueID).Solution = sol;

                                solutions.Add(sol);
                            }
                        }
                        break;
                    case "OBJECT" when v[1] == SolConvergence:
                        {
                            legacyConv = ReadConvergenceObject(reader);
                            reader.ReadLine();

                            break;
                        }
                    case "OBJECT" when v[1] == SolConvergenceSnapshot:
                        snapshotConv = ReadConvergenceSnapshotObject(reader);
                        break;
                    case "OBJECT" when v[1] == MdlCloneOptions:
                        {
                            var mco = new ModelCloneOptions();
                            string mcoline;
                            while ((mcoline = reader.ReadLine()) != EndObjectHeader)
                            {
                                var vv = SplitKeyValue(mcoline);
                                switch (vv[0])
                                {
                                    case SolErrorMethod: mco.ErrorEstimationMethod = (ErrorEstimationMethod)IParse(vv[1]); break;
                                    case SolCloneConcentrationVariance: mco.IncludeConcentrationErrorsInBootstrap = BParse(vv[1]); break;
                                    case SolCloneAutoVariance: mco.EnableAutoConcentrationVariance = BParse(vv[1]); break;
                                    case SolCloneAutoVarianceValue: mco.AutoConcentrationVariance = DParse(vv[1]); break;
                                }
                            }

                            factory.Model.ModelCloneOptions = mco;
                        }
                        break;
                }
            }

            if (solutions.Count > 0)
                factory.Model.Solution = new GlobalSolution(new GlobalSolver()
                {
                    Model = factory.Model, ErrorEstimationMethod = solutions[0].ErrorMethod
                }, solutions, snapshotConv ?? legacyConv);

            factory.Model.Solution.UseWeightedFitting = useErrorWeightedFitting;

            return factory.Model.Solution;
        }

        static SolutionInterface ReadSolution(StreamReader reader, string firstline, ExperimentData experimentData = null)
        {
            try
            {
                SingleModelFactory factory = null;
                string guid = DecodeText(firstline.Split(new[] { ':' }, 3)[2]);
                string dataref = SplitKeyValue(reader.ReadLine())[1];
                string parentID = "";
                bool useErrorWeightedFitting = false;
                var mdltype = (AnalysisModel)IParse(SplitKeyValue(reader.ReadLine())[1]);

                factory = new SingleModelFactory(mdltype);
                if (experimentData == null)
                    factory.InitializeModel(Data.Find(d => d.UniqueID == dataref) as ExperimentData);
                else factory.ConstructModel(experimentData);
                SolverConvergence legacyConv = null;
                SolverConvergence snapshotConv = null;
                double reference_loss_value = double.NaN;
                List<Parameter> parameters = null;
                List<SolutionInterface> bsols = null;

                string line;
                while ((line = reader.ReadLine()) != EndFileHeader)
                {
                    var v = line.Split(':');
                    switch (v[0])
                    {
                        //case SolErrorMethod: factory.Model.MCO = (ErrorEstimationMethod)IParse(v[1]); break;
                        case SolWeightedError: useErrorWeightedFitting = BParse(v[1]); break;
                        case SolParent: parentID = DecodeText(v[1]); break;
                        case SolLoss: reference_loss_value = DParse(v[1]); break;
                        case "LIST" when v[1] == SolParams:
                            parameters = new List<Parameter>();
                            string line2;
                            while ((line2 = reader.ReadLine()) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var val = DParse(dat[2]);

                                parameters.Add(new Parameter(par, val));
                            }
                            break;
                        case "LIST" when v[1] == SolBootstrapSolutions:
                            bsols = new List<SolutionInterface>();
                            var bsol = "";
                            while ((bsol = reader.ReadLine()) != EndListHeader)
                            {
                                bsols.Add(ReadSolution(reader, bsol, factory.Model.Data));
                            }
                            break;
                        case "LIST" when v[1] == SolBootstrapParameters:
                            bsols = ReadBootstrapParameterList(factory.Model, reader);
                            break;
                        case "LIST" when v[1] == MdlOptions: ReadModelOptions(factory.Model, reader); break;
                        case "OBJECT" when v[1] == SolConvergence:
                            legacyConv = ReadConvergenceObject(reader);
                            reader.ReadLine();
                            break;
                        case "OBJECT" when v[1] == SolConvergenceSnapshot:
                            snapshotConv = ReadConvergenceSnapshotObject(reader);
                            break;

                    }
                }

                foreach (var par in parameters)
                    factory.Model.Parameters.AddOrUpdateParameter(par.Key, par.Value);

                var solution = SolutionInterface.FromModel(factory.Model, snapshotConv ?? legacyConv);
                solution.UseWeightedFitting = useErrorWeightedFitting;
                solution.SetID(guid);
                if (!string.IsNullOrWhiteSpace(parentID)) solution.ParentSolutionID = parentID;

                factory.Model.Solution = solution;

                // If a loss was stored and it does not correspond to the loss calculated for the model, something changed and we invalidate the solution 
                // var currloss = solution.Model.Loss();
                // if (!double.IsNaN(reference_loss_value))
                //    if (Math.Abs(reference_loss_value - currloss) > 0.0001)
                //        solution.Invalidate();

                if (bsols != null) solution.SetBootstrapSolutions(bsols);

                return factory.Model.Solution;
            }
            catch (Exception ex)
            {
                ex.Source = "Solution Reading Error: " + firstline;
                AppEventHandler.DisplayHandledException(ex);

                return null;
            }
        }

        private static List<SolutionInterface> ReadBootstrapParameterList(Model mdl, StreamReader reader)
        {
            var solutions = new List<SolutionInterface>();

            string line;
            while ((line = reader.ReadLine()) != EndListHeader)
            {
                while (line != EndListHeader)
                {
                    var dat = line.Split(':');
                    var par = (ParameterType)int.Parse(dat[1]);
                    var val = DParse(dat[2]);

                    mdl.Parameters.AddOrUpdateParameter(par, val);

                    line = reader.ReadLine();
                }

                solutions.Add(SolutionInterface.FromModel(mdl, null));
            }

            return solutions;
        }

        private static void ReadModelOptions(Model mdl, StreamReader reader)
        {
            List<ExperimentAttribute> options = ReadAttributeOptions(reader);

            foreach (var att in options)
            {
                if (mdl.ModelOptions.ContainsKey(att.Key))
                {
                    mdl.ModelOptions[att.Key] = att;
                }
                else mdl.ModelOptions.Add(att.DictionaryEntry);
            }
        }

        private static SolverConvergence ReadConvergenceObject(StreamReader reader)
        {
            SolverConvergence conv;
            var dat = reader.ReadLine().Split(';');
            var dict = new Dictionary<string, string>();
            foreach (var d in dat.Where(s => !string.IsNullOrEmpty(s)))
            {
                var parts = SplitKeyValue(d);
                dict.Add(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
            }

            conv = SolverConvergence.FromSaveLegacy(
                IParse(dict[SolIterations]),
                DParse(dict[SolLoss]),
                TSParse(dict[SolConvTime]),
                TSParse(dict[SolConvBootstrapTime]),
                (SolverAlgorithm)IParse(dict[SolConvAlgorithm]),
                DecodeText(dict[SolConvMsg]),
                BParse(dict[SolConvFailed]));
            return conv;
        }

        private static SolverConvergence ReadConvergenceSnapshotObject(StreamReader reader)
        {
            var dict = ReadObjectDictionary(reader);

            var snapshot = new SolverConvergenceSnapshot()
            {
                SchemaVersion = dict.ContainsKey(SolConvSchemaVersion)
                    ? IParse(dict[SolConvSchemaVersion])
                    : SolverConvergenceSnapshot.CurrentSchemaVersion,
                Iterations = dict.ContainsKey(SolIterations) ? IParse(dict[SolIterations]) : 0,
                Loss = dict.ContainsKey(SolLoss) ? DParse(dict[SolLoss]) : 0,
                TimeSeconds = dict.ContainsKey(SolConvTime) ? DParse(dict[SolConvTime]) : 0,
                ErrorEstimationTimeSeconds = dict.ContainsKey(SolConvBootstrapTime) ? DParse(dict[SolConvBootstrapTime]) : 0,
                Algorithm = dict.ContainsKey(SolConvAlgorithm)
                    ? (SolverAlgorithm)IParse(dict[SolConvAlgorithm])
                    : default,
                Termination = dict.ContainsKey(SolConvTermination)
                    ? (SolverTermination)IParse(dict[SolConvTermination])
                    : SolverTermination.Unknown,
                ErrorEstimationOutcome = dict.ContainsKey(SolConvErrorOutcome)
                    ? (ErrorEstimationOutcome)IParse(dict[SolConvErrorOutcome])
                    : ErrorEstimationOutcome.None,
                FailureReason = dict.ContainsKey(SolConvFailureReason) ? DecodeText(dict[SolConvFailureReason]) : string.Empty,
                ErrorEstimationSummary = dict.ContainsKey(SolConvErrorSummary) ? DecodeText(dict[SolConvErrorSummary]) : string.Empty,
            };

            return SolverConvergence.FromSnapshot(snapshot);
        }

        private static Dictionary<string, string> ReadObjectDictionary(StreamReader reader)
        {
            var dict = new Dictionary<string, string>();

            string line;
            while ((line = reader.ReadLine()) != EndObjectHeader)
            {
                int idx = line.IndexOf(':');

                if (idx < 0)
                {
                    dict[line] = string.Empty;
                    continue;
                }

                var key = line.Substring(0, idx);
                var value = line.Substring(idx + 1);

                dict[key] = value;
            }

            return dict;
        }
    }
}
