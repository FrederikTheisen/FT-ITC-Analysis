using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.Analysis2.Models;
using DataReaders;
using Utilities;

namespace AnalysisITC
{
    public class ExperimentData : ITCDataContainer
    {
        public static EnergyUnit Unit => DataManager.Unit;
        public static Random Rand = new Random();

        public event EventHandler ProcessingUpdated;
        public event EventHandler SolutionChanged;

        private double measuredTemperature = double.NaN;

        public ITCInstrument Instrument { get; set; } = ITCInstrument.Unknown;
        public ITCDataFormat DataSourceFormat { get; set; }

        public List<DataPoint> DataPoints { get; set; } = new List<DataPoint>();
        public List<DataPoint> BaseLineCorrectedDataPoints { get; set; }
        public List<InjectionData> Injections { get; set; } = new List<InjectionData>();
        public List<TandemExperimentSegment> Segments { get; private set; }
        Dictionary<int, TandemExperimentSegment> _segmentStartLookup;

        public FloatWithError SyringeConcentration { get; set; }
        public FloatWithError CellConcentration { get; set; }
        public double CellVolume { get; set; }
        public double StirringSpeed { get; set; } = -1;
        public FeedbackMode FeedBackMode { get; set; }

        public double TargetTemperature { get; set; }
        public double InitialDelay { get; set; }
        public double TargetPowerDiff { get; set; }
        public double MeasuredTemperature
        {
            get
            {
                if (double.IsNaN(measuredTemperature)) return TargetTemperature;
                else return measuredTemperature;
            }
            internal set => measuredTemperature = value;
        }
        public int InjectionCount => Injections.Count;
        public PeakHeatDirection AverageHeatDirection { get; set; } = PeakHeatDirection.Unknown;
        public bool CanBeAnalyzed => Injections.All(inj => inj.IsIntegrated);
        //public InjectionData.IntegrationLengthMode IntegrationLengthMode { get; set; } = InjectionData.IntegrationLengthMode.Time;
        //public float IntegrationLengthFactor { get; set; } = 2;

        bool include = true;
        public bool Include
        {
            get
            {
                if (!Injections.All(inj => inj.IsIntegrated)) return false;
                else return include;
            }
            set => include = value;
        }
        public void ToggleInclude() { include = !include; DataManager.InvokeUpdateDataViewCells(); }

        public double MeasuredTemperatureKelvin => 273.15 + MeasuredTemperature;
        public double TimeStep
        {
            get
            {
                if (DataPoints.Count < 2) return 1;

                return (DataPoints.Last().Time - DataPoints.First().Time) / (DataPoints.Count - 1);
            }
        }

        public DataProcessor Processor { get; private set; }
        public List<ExperimentAttribute> Attributes { get; } = new List<ExperimentAttribute>();
        public Model Model { get; set; }
        public SolutionInterface Solution => Model?.Solution;
        public ExperimentData ReferenceExperiment
        {
            get
            {
                if (Attributes.Exists(att => att.Key == AttributeKey.BufferSubtraction))
                {
                    var reference = Attributes.Find(att => att.Key == AttributeKey.BufferSubtraction);

                    if (DataManager.Data.Exists(d => d.UniqueID == reference.StringValue))
                    {
                        return DataManager.Data.Find(d => d.UniqueID == reference.StringValue);
                    }
                }

                return null;
            }
        }
        public bool IsTandemExperiment => Segments != null ? Segments.Count > 0 : false;
        public TimeSpan Duration
        {
            get
            {
                if (DataPoints != null && DataPoints.Count > 1)
                {
                    return TimeSpan.FromSeconds(DataPoints.Last().Time - DataPoints.First().Time);
                }

                return TimeSpan.FromSeconds(0);
            }
        }
        public bool HasThermogram => DataPoints != null && DataPoints.Count > 1;
        public AnalysisXAxisType AxisType
        {
            get
            {
                // There is no possible way to get any meaningful X axis from concentrations
                if (CellConcentration < double.Epsilon && SyringeConcentration < double.Epsilon) return AnalysisXAxisType.ID;

                // We can just return the running concentration of titrant in the cell
                if (CellConcentration < double.Epsilon) return AnalysisXAxisType.TitrantConcentration;

                // Using dissociation model, use dissociation axis
                if (Model != null && Model.ModelType == AnalysisModel.Dissociation) return AnalysisXAxisType.TitrantConcentration;

                return AnalysisXAxisType.MolarRatio;
            }
        }

        public ExperimentData(string file)
        {
            SetFileName(file);

            Processor = new DataProcessor(this);
        }

        public void IterateCopyName()
        {
            var parts = FileName.Split('.');

            string basename = Name;
            if (basename.Contains(" [COPY"))
                basename = basename.Substring(0, basename.IndexOf(" [COPY"));

            int i = 1;
            var proposedname = $"{basename} [COPY{i}]";

            while (DataManager.Data.Exists(d => d.Name == proposedname))
            {
                i++;
                proposedname = $"{basename} [COPY{i}]";
            }

            Name = proposedname;
        }

        public void AddInjection(string dataline)
        {
            var inj = new InjectionData(this, dataline, Injections.Count);

            Injections.Add(inj);
        }

        public void FitIntegrationPeaks()
        {
            try
            {
                foreach (var inj in Injections)
                {
                    inj.SetIntegrationLengthByPeakFitting();
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationLengthByFactor(float factor)
        {
            try
            {
                foreach (var inj in Injections)
                {
                    inj.SetIntegrationLengthByFactor(factor);
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationLengthByTimes(float[] lengths)
        {
            int i = 0;
            foreach (var inj in Injections)
            {
                float length;
                if (i >= lengths.Length) length = lengths[^1];
                else length = lengths[i];

                inj.SetIntegrationLengthByTime(length);

                i++;
            }
        }

        public void SetIntegrationLengthByTime(float length)
        {
            try
            {
                foreach (var inj in Injections) { inj.SetIntegrationLengthByTime(length); }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationStartTimes(float[] delays)
        {
            int i = 0;
            foreach (var inj in Injections)
            {
                float delay;
                if (i >= delays.Length) delay = delays[^1];
                else delay = delays[i];

                inj.SetIntegrationStartTime(delay);

                i++;
            }
        }

        public void SetIntegrationStartTime(float delay)
        {
            foreach (var inj in Injections) { inj.SetIntegrationStartTime(delay); }
        }

        public void CalculatePeakHeatDirection()
        {
            bool positive = false;
            bool negative = false;

            foreach (var inj in Injections)
            {
                if (!inj.Include) continue;

                if (inj.HeatDirection == PeakHeatDirection.Endothermal) positive = true;
                else if (inj.HeatDirection == PeakHeatDirection.Exothermal) negative = true;
            }

            if (positive && negative) AverageHeatDirection = PeakHeatDirection.Both;
            else if (positive) AverageHeatDirection = PeakHeatDirection.Endothermal;
            else if (negative) AverageHeatDirection = PeakHeatDirection.Exothermal;
            else AverageHeatDirection = PeakHeatDirection.Unknown;
        }

        public void SetReferenceExperiment(ExperimentData reference)
        {
            if (ReferenceExperiment != null) ReferenceExperiment.ProcessingUpdated -= Reference_ProcessingUpdated;

            // Check for self referencing.
            if (reference.UniqueID == this.UniqueID) throw new HandledException(HandledException.Severity.Warning, "Buffer Subtraction Error", "Attempting to set reference experiment to itself");
            if (reference.ReferenceExperiment != null) throw new HandledException(HandledException.Severity.Warning, "Buffer Subtraction Error", "Reference experiment already contains a buffer subtraction");

            // Clear previous setting
            Attributes.RemoveAll(att => att.Key == AttributeKey.BufferSubtraction);

            // Add reference experiment
            Attributes.Add(ExperimentAttribute.ExperimentReference("Reference", reference.UniqueID));

            Reference_ProcessingUpdated(null, null);

            reference.ProcessingUpdated += Reference_ProcessingUpdated;
        }

        private void Reference_ProcessingUpdated(object sender, EventArgs e)
        {
            // Reintegrate peaks
            foreach (var inj in Injections)
                inj.UpdateCorrectedPeakArea();
        }

        public void SetProcessor(DataProcessor processor)
        {
            Processor = processor;
        }

        List<InjectionData> GetBootstrappedResiduals(ExperimentData clone)
        {
            if (Solution == null) return Injections;

            var syntheticdata = new List<InjectionData>();

            var residuals = new List<(double,double)>();

            foreach (var inj in Injections.Where(inj => inj.Include))
            {
                var fit = Model.EvaluateEnthalpy(inj.ID, withoffset: true);
                residuals.Add((inj.Enthalpy - fit, inj.SD));
            }

            residuals.Shuffle();
            int resindex = 0;

            foreach (var inj in Injections)
            {
                var res = (0.0,0.0);

                if (inj.Include) { res = residuals[resindex]; resindex++; }

                var fit = Model.EvaluateEnthalpy(inj.ID, withoffset: true);
                var resarea = res.Item1 * inj.InjectionMass;
                var ressdarea = res.Item2 * inj.InjectionMass;
                var fitarea = fit * inj.InjectionMass;

                var syn_inj = new InjectionData(clone, inj.ID, inj.Volume, inj.InjectionMass, inj.Include)
                {
                    Temperature = inj.Temperature,
                    ActualCellConcentration = inj.ActualCellConcentration,
                    ActualTitrantConcentration = inj.ActualTitrantConcentration,
                    Ratio = inj.Ratio
                };
                syn_inj.SetPeakArea(new FloatWithError(fitarea + resarea, ressdarea));

                syntheticdata.Add(syn_inj);
            }

            return syntheticdata;
        }

        void AddConcentrationVariance(List<InjectionData> injections, ModelCloneOptions options = null)
        {
            var sd_cell = CellConcentration.FractionSD;
            var sd_syringe = SyringeConcentration.FractionSD;

            if (options.EnableAutoConcentrationVariance)
            {
                if (sd_cell < 0.001) sd_cell = options.AutoConcentrationVariance;
                if (sd_syringe < 0.001) sd_syringe = options.AutoConcentrationVariance;
            }

            var cell = 1 + (2 * Rand.NextDouble() - 1) * sd_cell;
            var syringe = 1 + (2 * Rand.NextDouble() - 1) * sd_syringe;

            foreach (var inj in injections)
            {
                inj.ActualCellConcentration *= cell;
                inj.ActualTitrantConcentration *= syringe;
            }
        }

        public virtual ExperimentData GetSynthClone(ModelCloneOptions options)
        {
            var clone = new ExperimentData(FileName)
            {
                CellVolume = CellVolume,
                MeasuredTemperature = MeasuredTemperature,
                CellConcentration = CellConcentration,
                SyringeConcentration = SyringeConcentration,
            };
            List<InjectionData> syninj;

            switch (options.ErrorEstimationMethod)
            {
                default:
                case ErrorEstimationMethod.BootstrapResiduals: syninj = GetBootstrappedResiduals(clone); break;
                case ErrorEstimationMethod.LeaveOneOut when options.IsGlobalClone: syninj = Injections; break;
                case ErrorEstimationMethod.LeaveOneOut:
                    {
                        syninj = new List<InjectionData>();
                        foreach (var inj in Injections)
                        {
                            // If using leave one out or the data point was excluded from the original fit
                            bool discard = inj.ID == options.DiscardedDataPoint || !inj.Include;
                            var sinj = new InjectionData(clone, inj.ID, inj.Volume, inj.InjectionMass, !discard)
                            {
                                Temperature = inj.Temperature,
                                ActualCellConcentration = inj.ActualCellConcentration,
                                ActualTitrantConcentration = inj.ActualTitrantConcentration,
                                Ratio = inj.Ratio,
                            };
                            sinj.SetPeakArea(new FloatWithError(inj.PeakArea, inj.PeakArea.SD));
                            syninj.Add(sinj);
                        }
                        break;
                    }
            }

            if (options.IncludeConcentrationErrorsInBootstrap) AddConcentrationVariance(syninj, options);

            clone.Injections = syninj;

            clone.Segments = Segments?
                .Select(s => new TandemExperimentSegment(s.FirstInjectionID, s.SegmentInitialActiveCellConc, s.SegmentInitialActiveTitrantConc))
                .ToList();

            clone.InvalidateSegmentLookup();

            foreach (var opt in Attributes) clone.Attributes.Add(opt);

            clone.SetID(UniqueID);

            return clone;
        }

        public void UpdateProcessing(bool invalidate = true)
        {
            if (Solution != null && invalidate) Solution.Invalidate();

            CalculatePeakHeatDirection();

            ProcessingUpdated?.Invoke(Processor, null);
        }

        public void UpdateSolution(Model mdl = null)
        {
            if (mdl != null) Model = mdl;

            SolutionChanged?.Invoke(this, null);
        }

        public void AddSegment(TandemExperimentSegment segment)
        {
            if (Segments == null) Segments = new List<TandemExperimentSegment>();

            Console.WriteLine("Adding Segment:\n  ID: "
                + segment.FirstInjectionID.ToString() + "\n  Cell Conc:    "
                + segment.SegmentInitialActiveCellConc.ToString() + "\n  Titrant Conc: "
                + segment.SegmentInitialActiveTitrantConc.ToString());

            Segments.Add(segment);
        }

        void EnsureSegmentStartLookup()
        {
            if (_segmentStartLookup != null) return;
            _segmentStartLookup = Segments?
                .GroupBy(s => s.FirstInjectionID)
                .ToDictionary(g => g.Key, g => g.First()) ?? new Dictionary<int, TandemExperimentSegment>();
        }

        public void InvalidateSegmentLookup()
        {
            _segmentStartLookup = null;
        }

        public List<string> GetInfoString()
        {
            var info = new List<string>();

            string injdescription = "";

            for (int i = 0; i < this.Injections.Count; i++)
            {
                var curr = this.Injections[i];
                var next = i < this.InjectionCount - 1 ? this.Injections[i + 1] : null;
                var prev = i > 0 ? this.Injections[i - 1] : null;

                if (this.Injections.Where(inj => inj.ID > i).All(inj => inj.Volume == curr.Volume))
                {
                    injdescription += "#" + (i + 1).ToString() + "-" + this.Injections.Count.ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
                    break;
                }
                else if (next != null && curr.Volume != next.Volume)
                    injdescription += "#" + (i + 1).ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
                else if (prev != null && curr.Volume != prev.Volume)
                    injdescription += "#" + (i + 1).ToString() + ": " + (1000000 * curr.Volume).ToString("F1") + " µl, ";
            }

            injdescription = injdescription.Substring(0, injdescription.Length - 2);

            info.Add("**Filename:** " + this.FileName);
            info.Add("  **Format:** " + this.DataSourceFormat.GetProperties().Name);
            info.Add("  **Date:** " + this.UILongDateWithTime);
            if (this.Duration > TimeSpan.FromSeconds(1))
                info.Add($"  **Duration:** {this.Duration.ToReadableString()}");
            info.Add("**Instrument:** " + this.Instrument.GetProperties().Name);
            info.Add($"  **Cell Volume:** {1000000 * this.CellVolume:F1} µl");
            if (DataReaders.ITCInstrument.MicroCal.HasFlag(this.Instrument))
                info.Add("  **Feedback Mode:** " + this.FeedBackMode.GetProperties().Name);
            if (this.StirringSpeed > -1)
                info.Add("  **Stirring Speed:** " + this.StirringSpeed.ToString() + " rpm");
            info.Add("**Temperature:**");
            info.Add($"  **Target:** {this.TargetTemperature:G4} °C");
            if (this.MeasuredTemperature != this.TargetTemperature)
                info.Add($"  **Measured:** {this.DataPoints.Min(dp => dp.Temperature):F4} - {this.DataPoints.Max(dp => dp.Temperature):F4} °C | Mean = {this.MeasuredTemperature:G4} °C");
            info.Add($"**Injections:** {this.InjectionCount} [{injdescription}]");
            info.Add($"**Concentrations:** Cell: {this.CellConcentration.AsConcentration(ConcentrationUnit.µM)} | Syringe: {this.SyringeConcentration.AsConcentration(ConcentrationUnit.µM)}");

            if (!string.IsNullOrEmpty(this.Comments)) info.Add("**Comment:** " + this.Comments);

            return info;
        }
    }

    public class TandemExperimentSegment
    {
        public int FirstInjectionID { get; private set; }          // ID of first injection in this segment

        /// <summary>
        /// Segment starting concentrations in cell
        /// </summary>
        public double SegmentInitialActiveCellConc { get; private set; }         // M_pre in cell (mol/L)
        public double SegmentInitialActiveTitrantConc { get; private set; }      // L_pre in cell (mol/L)

        public TandemExperimentSegment() { }

        public TandemExperimentSegment(int ID, double activecellconc, double activetitrantconc)
        {
            FirstInjectionID = ID;
            SegmentInitialActiveCellConc = activecellconc;
            SegmentInitialActiveTitrantConc = activetitrantconc;
        }

        public static TandemExperimentSegment FromFile(string line)
        {
            var items = line.Split(',');

            return new TandemExperimentSegment()
            {
                FirstInjectionID = int.Parse(items[0]),
                SegmentInitialActiveCellConc = double.Parse(items[1]),
                SegmentInitialActiveTitrantConc = double.Parse(items[2])
            };
        }

        public void UpdateConcentrations(double activecellconc, double activetitrantconc)
        {
            Console.WriteLine("Updating TandemSegment Concentrations: \n" + activecellconc.ToString() + "\n" + activetitrantconc.ToString());

            SegmentInitialActiveCellConc = activecellconc;
            SegmentInitialActiveTitrantConc = activetitrantconc;
        }
    }

    public enum PeakHeatDirection
    {
        Unknown,
        Exothermal,
        Endothermal,
        Both,
    }
}
