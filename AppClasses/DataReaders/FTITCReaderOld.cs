using System;
using System.IO;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DataReaders
{
    /// <summary>
    /// Obsolete class
    /// </summary>
    class FTITCReaderOld : FTITCFormat
    {
        public static ITCDataContainer[] ReadPath(string path)
        {
            var data = new List<ITCDataContainer>();

            var file = string.Join("", File.ReadAllLines(path));

            Regex regex = new Regex(OldReaderPattern(ExperimentHeader), RegexOptions.Singleline | RegexOptions.Compiled);
            var v = regex.Matches(file);

            foreach (var m in v.AsEnumerable())
            {
                var result = m.Value;

                data.Add(ProcessExperimentData(result.Substring(ExperimentHeader.Length + 2, result.Length - (2 * ExperimentHeader.Length + 5))));
            }

            return data.ToArray();
        }

        static string GContent(string header, string data)
        {
            Regex regex = new Regex(OldReaderPattern(header), RegexOptions.Singleline | RegexOptions.Compiled);
            var v = regex.Matches(data);

            if (v.Count == 0) return null;

            var result = v.First().Value;

            return result.Substring(header.Length + 2, result.Length - (2 * header.Length + 5));
        }

        static ExperimentData ProcessExperimentData(string data)
        {
            var exp = new ExperimentData(GContent(FileName, data));
            exp.SetID(GContent(ID, data));
            exp.Date = DateTime.Parse(GContent(Date, data));
            //exp.Include = GContent(Include, data) == "1";
            exp.SyringeConcentration = FWEParse(GContent(SyringeConcentration, data));
            exp.CellConcentration = FWEParse(GContent(CellConcentration, data));
            exp.StirringSpeed = double.Parse(GContent(StirringSpeed, data));
            exp.TargetTemperature = double.Parse(GContent(TargetTemperature, data));
            exp.MeasuredTemperature = double.Parse(GContent(MeasuredTemperature, data));
            exp.InitialDelay = double.Parse(GContent(InitialDelay, data));
            exp.TargetPowerDiff = double.Parse(GContent(TargetPowerDiff, data));
            exp.Include = GContent(Include, data) == "1";
            //exp.IntegrationLengthMode = (InjectionData.IntegrationLengthMode)int.Parse(GContent(UseIntegrationFactorLength, data));
            //exp.IntegrationLengthFactor = float.Parse(GContent(IntegrationLengthFactor, data));
            exp.CellVolume = double.Parse(GContent(CellVolume, data));
            exp.CellVolume = double.Parse(GContent(CellVolume, data));
            exp.FeedBackMode = (FeedbackMode)int.Parse(GContent(FeedBackMode, data));
            exp.Instrument = GContent(Instrument, data) != null ? (ITCInstrument)int.Parse(GContent(Instrument, data)) : ITCInstrument.Unknown;

            var datapoints = new List<DataPoint>();

            var dpdata = GContent(DataPointList, data).Split(";");
            foreach (var dp in dpdata)
            {
                var _dp = dp.Split(',');
                datapoints.Add(new DataPoint(float.Parse(_dp[0]), float.Parse(_dp[1]), float.Parse(_dp[2]), shieldt: float.Parse(_dp[3])));
            }

            exp.DataPoints = datapoints;

            var injections = new List<InjectionData>();

            var injdata = GContent(InjectionList, data).Split(";");
            foreach (var inj in injdata)
            {
                injections.Add(new InjectionData(exp, inj));
            }

            exp.Injections = injections;

            string processordata = GContent(Processor, data);
            {
                if (!string.IsNullOrEmpty(processordata))
                {
                    var processor = new DataProcessor(exp);
                    processor.InitializeBaseline((BaselineInterpolatorTypes)int.Parse(GContent(ProcessorType, processordata)));

                    switch (processor.BaselineType)
                    {
                        case BaselineInterpolatorTypes.None: break;
                        default:
                        case BaselineInterpolatorTypes.Spline:
                            (processor.Interpolator as SplineInterpolator).Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)int.Parse(GContent(SplineAlgorithm, processordata));
                            (processor.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)int.Parse(GContent(SplineHandleMode, processordata));
                            (processor.Interpolator as SplineInterpolator).FractionBaseline = float.Parse(GContent(SplineFraction, processordata));
                            //(processor.Interpolator as SplineInterpolator).IsLocked = GContent(SplineLocked, processordata) == "1";
                            var splinepoints = new List<SplineInterpolator.SplinePoint>();

                            var spdata = GContent(SplinePointList, data).Split(";");
                            foreach (var sp in spdata)
                            {
                                var _spdat = sp.Split(',');
                                splinepoints.Add(new SplineInterpolator.SplinePoint(double.Parse(_spdat[0]), double.Parse(_spdat[1]), int.Parse(_spdat[2]), double.Parse(_spdat[3])));
                            }

                            (processor.Interpolator as SplineInterpolator).SetSplinePoints(splinepoints);
                            break;
                        case BaselineInterpolatorTypes.Polynomial:
                            (processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree = int.Parse(GContent(PolynomiumDegree, processordata));
                            (processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit = int.Parse(GContent(PolynomiumLimit, processordata));
                            break;
                    }

                    exp.SetProcessor(processor);

                    processor.ProcessData(replace: false);
                }
            }


            return exp;
        }
    }
}
