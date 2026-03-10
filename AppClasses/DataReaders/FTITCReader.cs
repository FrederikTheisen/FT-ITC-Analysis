using System;
using System.IO;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.AppClasses.Analysis2;
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

                    var input = line.Split(':');

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

            string[] a = firstline.Split(':');
            var exp = new ExperimentData(a[2]);

            StatusBarManager.SetStatus($"Loading {exp.FileName}", 0);
            await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.

            ReadExperimentData(reader, exp);

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            return exp;
        }

        static async Task<ExperimentData> ReadExperimentDataFile(StreamReader reader, string firstline)
        {
            AppEventHandler.PrintAndLog("Loading Experiment Data...", 1);

            string[] a = firstline.Split(':');
            var exp = new ExperimentData(a[2]);

            StatusBarManager.SetStatus($"Loading {exp.FileName}", 0);
            await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.

            ReadExperimentData(reader, exp);

            RawDataReader.ProcessInjections(exp);

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            exp.CalculatePeakHeatDirection();
            return exp;
        }

        static ExperimentData ReadExperimentData(StreamReader reader, ExperimentData exp)
        {
            SolutionInterface sol = null;

            string line;

            while ((line = reader.ReadLine()) != EndFileHeader)
            {
                string[] v = line.Split(':');

                switch (v[0])
                {
                    case ID: exp.SetID(v[1]); break;
                    case Date: exp.Date = DateTime.Parse(line[5..]); break;
                    case SourceFormat: exp.DataSourceFormat = (ITCDataFormat)IParse(v[1]); break;
                    case Comments: exp.Comments = v[1]; break;
                    case SyringeConcentration: exp.SyringeConcentration = FWEParse(v[1]); break;
                    case CellConcentration: exp.CellConcentration = FWEParse(v[1]); break;
                    case CellVolume: exp.CellVolume = DParse(v[1]); break;
                    case StirringSpeed: exp.StirringSpeed = DParse(v[1]); break;
                    case TargetTemperature: exp.TargetTemperature = DParse(v[1]); break;
                    case MeasuredTemperature: exp.MeasuredTemperature = DParse(v[1]); break;
                    case InitialDelay: exp.InitialDelay = DParse(v[1]); break;
                    case TargetPowerDiff: exp.TargetPowerDiff = DParse(v[1]); break;
                    case FeedBackMode: exp.FeedBackMode = (FeedbackMode)int.Parse(v[1]); break;
                    case Include: exp.Include = BParse(v[1]); break;
                    case Instrument: exp.Instrument = (ITCInstrument)IParse(v[1]); break;
                    case "LIST" when v[1] == InjectionList:
                        ReadInjectionList(exp, reader); break;
                    case "LIST" when v[1] == DataPointList:
                        ReadDataList(exp, reader); break;
                    case "LIST" when v[1] == ExperimentAttributes:
                        ReadAttributes(exp, reader); break;
                    case "LIST" when v[1] == SegmentList:
                        ReadSegmentList(exp, reader);  break;
                    case "OBJECT" when v[1] == Processor:
                        ReadProcessor(exp, reader); break;
                    case "OBJECT" when v[1] == ExperimentSolutionHeader:
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
            string[] v = line.Split(':');

            if (v[0] != ProcessorType) return;

            p.InitializeBaseline((BaselineInterpolatorTypes)int.Parse(v[1]));

            while ((line = reader.ReadLine()) != EndObjectHeader)
            {
                v = line.Split(':');

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
                var _spdat = line.Split(',');
                splinepoints.Add(new SplineInterpolator.SplinePoint(double.Parse(_spdat[0]), double.Parse(_spdat[1]), int.Parse(_spdat[2]), double.Parse(_spdat[3])));
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

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var dat = line.Split(';');

                var opt = ExperimentAttribute.FromKey((AttributeKey)IParse(dat[1]));

                for (int i = 2; i < dat.Length; i++)
                {
                    var d = dat[i].Split(':');
                    string type = d[0];
                    string val = d[1];

                    switch (type)
                    {
                        case "B": opt.BoolValue = BParse(val); break;
                        case "I": opt.IntValue = IParse(val); break;
                        case "D": opt.DoubleValue = DParse(val); break;
                        case "FWE": opt.ParameterValue = FWEParse(val); break;
                        case "S": opt.StringValue = val; break;
                        case "name": opt.OptionName = val; break;
                    }
                }

                options.Add(opt);
            }

            return options;
        }

        static void ReadInjectionList(ExperimentData exp, StreamReader reader)
        {
            var injections = new List<InjectionData>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader) injections.Add(InjectionData.FromFTITCLine(exp, line));

            exp.Injections = injections;
        }

        static void ReadDataList(ExperimentData exp, StreamReader reader)
        {
            var datapoints = new List<DataPoint>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var dp = line.Split(',');
                datapoints.Add(new DataPoint(float.Parse(dp[0]), float.Parse(dp[1]), float.Parse(dp[2]), shieldt: float.Parse(dp[3])));
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
                var info = firstline.Split(':')[2].Split(',');
                var comments = reader.ReadLine().Split(':')[1];
                var dateinfo = reader.ReadLine().Substring(Date.Length + 1);
                var date = DateTime.Parse(dateinfo);

                StatusBarManager.SetStatus($"Loading {(info.Length > 1 ? info[1] : "Analysis Result")}", 0);
                await Task.Delay(1); //Necessary to update UI.

                var sol = ReadGlobalSolution(reader);
                
                string guid = info[0];
                string name = info.Length > 1 ? info[1] : sol.SolutionName;
                AnalysisResult result = new AnalysisResult(sol);
                result.SetID(guid);
                result.FileName = name;
                result.Comments = comments;
                result.SetDate(date);

                return result;
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog(ex.StackTrace);
                AppEventHandler.DisplayHandledException(new HandledException(HandledException.Severity.Error,"File Reading Error", $"Analysis Result reading error.\nFile: {firstline}"));

                return null;
            }
        }

        static GlobalSolution ReadGlobalSolution(StreamReader reader)
        {
            bool useErrorWeightedFitting = false;

            reader.ReadLine(); //Header is empty
            string line = reader.ReadLine();
            var mdl = (AnalysisModel)IParse(line.Split(':')[1]);
            GlobalModelFactory factory = new GlobalModelFactory(mdl);
            var datas = new List<ExperimentData>();
            var solutions = new List<SolutionInterface>();
            SolverConvergence conv = null;

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
                            conv = ReadConvergenceObject(reader);
                            reader.ReadLine();

                            break;
                        }
                    case "OBJECT" when v[1] == MdlCloneOptions:
                        {
                            var mco = new ModelCloneOptions();
                            string mcoline;
                            while ((mcoline = reader.ReadLine()) != EndObjectHeader)
                            {
                                var vv = mcoline.Split(':');
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
                }, solutions, conv);

            factory.Model.Solution.UseWeightedFitting = useErrorWeightedFitting;

            return factory.Model.Solution;
        }

        static SolutionInterface ReadSolution(StreamReader reader, string firstline, ExperimentData experimentData = null)
        {
            try
            {
                SingleModelFactory factory = null;
                string guid = firstline.Split(':')[2];
                string dataref = reader.ReadLine().Split(':')[1];
                string parentID = "";
                bool useErrorWeightedFitting = false;
                var mdltype = (AnalysisModel)IParse(reader.ReadLine().Split(':')[1]);

                factory = new SingleModelFactory(mdltype);
                if (experimentData == null)
                    factory.InitializeModel(Data.Find(d => d.UniqueID == dataref) as ExperimentData);
                else factory.ConstructModel(experimentData);
                SolverConvergence conv = null;
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
                        case SolParent: parentID = v[1]; break;
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
                        case "LIST" when v[1] == MdlOptions: ReadModelOptions(factory.Model, reader); break;
                        case "OBJECT" when v[1] == SolConvergence: conv = ReadConvergenceObject(reader); break;

                    }
                }

                foreach (var par in parameters)
                    factory.Model.Parameters.AddOrUpdateParameter(par.Key, par.Value);

                var solution = SolutionInterface.FromModel(factory.Model, conv);
                solution.UseWeightedFitting = useErrorWeightedFitting;
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
                dict.Add(d.Split(':')[0], d.Split(':')[1]);

            conv = SolverConvergence.FromSave(
                IParse(dict[SolIterations]),
                DParse(dict[SolLoss]),
                TSParse(dict[SolConvTime]),
                TSParse(dict[SolConvBootstrapTime]),
                (SolverAlgorithm)IParse(dict[SolConvAlgorithm]),
                dict[SolConvMsg],
                BParse(dict[SolConvFailed]));
            return conv;
        }
    }
}
