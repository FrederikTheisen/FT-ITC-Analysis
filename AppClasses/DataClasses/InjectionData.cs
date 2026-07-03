using System;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Data
{
    public class InjectionData
    {
        public ExperimentData Experiment { get; private set; }
        public int ID { get; private set; }

        float time = -1;

        public float Time { get => time; set { time = value; InitializeIntegrationTimes(); } }
        public double Volume { get; private set; }
        public float Duration { get; private set; }
        public float Delay { get; private set; }
        public float Filter { get; private set; } = 5;
        public double Temperature { get; set; }
        public double InjectionMass => Experiment.SyringeConcentration * this.Volume; // { get; set; }

        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }

        /// <summary>
        /// The X axis variable to plot
        /// </summary>
        public double Ratio { get; set; }

        bool include = true;
        public bool Include
        {
            get => include;
            set
            {
                if (include == value) return;

                include = value;
                Experiment?.MarkModified();
            }
        }
        public float IntegrationStartDelay { get; private set; } = 0;
        public float IntegrationEndOffset { get; private set; } = 90;
        public float IntegrationStartTime => Time + IntegrationStartDelay;
        public float IntegrationEndTime => Time + IntegrationEndOffset;
        public float IntegrationLength => IntegrationEndOffset - IntegrationStartDelay;
        float MinimumIntegrationTime => 2 * (float)Experiment.TimeStep;

        public PeakHeatDirection HeatDirection { get; set; } = PeakHeatDirection.Unknown;

        public bool IsIntegrated { get; set; } = false;
        public FloatWithError RawPeakArea { get; private set; } = new();
        public FloatWithError PeakArea { get; private set; } = new();
        public double PeakAreaError => PeakArea.SD;
        public Energy Enthalpy2 => new(PeakArea / InjectionMass);
        public double Enthalpy => PeakArea / InjectionMass;
        public double SD => PeakArea.SD / InjectionMass;

        public double OffsetEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return Enthalpy;
                //else return Enthalpy - Experiment.Solution.Offset; //A1 pattern
                else return Enthalpy - Experiment.Solution.Offset;
            }
        }

        public double ResidualEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return 0;
                if (InjectionMass == 0) return 0;

                return Experiment.Model.Residual(this) / InjectionMass;
            }
        }

        private InjectionData() { }

        public static InjectionData FromTAFileLine(ExperimentData experiment, int id, double v, DataPoint dp, InjectionData prev)
        {
            float delay = 0.0f;
            if (prev != null)
            {
                delay = dp.Time - prev.Time;
                prev.Delay = delay;
            }


            return new InjectionData()
            {
                Experiment = experiment,
                ID = id,
                Include = id != 0,
                Volume = v,
                Time = dp.Time,
                Temperature = dp.Temperature,
                Delay = delay, // Delay guess until next injection is processed
                Duration = 2 * (float)v, // Not known
                Filter = 0.0f, // Not known, not used
            };
        }

        public static InjectionData FromFTITCLine(ExperimentData experiment, string line)
        {
            var parameters = line.Split(',');

            var inj = new InjectionData()
            {
                Experiment = experiment,
                ID = FTITCFormat.IParse(parameters[0]),
                Include = parameters[1] == "1",
                Time = FTITCFormat.FParse(parameters[2]),
                Volume = FTITCFormat.DParse(parameters[3]),
                Delay = FTITCFormat.FParse(parameters[4]),
                Duration = FTITCFormat.FParse(parameters[5]),
                Temperature = FTITCFormat.DParse(parameters[6]),
                IntegrationStartDelay = FTITCFormat.FParse(parameters[7]),
                IntegrationEndOffset = FTITCFormat.FParse(parameters[8]),
            };

            // Newer files contain additional information for the injections to handle tandem experiment data
            if (parameters.Count() >= 11)
            {
                inj.ActualCellConcentration = FTITCFormat.DParse(parameters[9]);
                inj.ActualTitrantConcentration = FTITCFormat.DParse(parameters[10]);
                inj.Ratio = inj.ActualTitrantConcentration / inj.ActualCellConcentration;
            }

            // Newer files contain additional information
            if (parameters.Count() >= 13)
            {
                var peakarea = FTITCFormat.DParse(parameters[11]);
                var peaksd = FTITCFormat.DParse(parameters[12]);

                inj.SetPeakArea(new FloatWithError(peakarea, peaksd));
            }

            return inj;
        }

        public static InjectionData FromPEAQFile(ExperimentData experiment, int id, bool include, double time, double volume, double delay, double duration, double temperature)
        {
            return new InjectionData()
            {
                Experiment = experiment,
                ID = id,
                Include = include,
                Time = (float)time,
                Volume = volume,
                Delay = (float)delay,
                Duration = (float)duration,
                Temperature = temperature
            };
        }

        public InjectionData(ExperimentData experiment, double volume)
        {
            Experiment = experiment;

            ID = experiment.InjectionCount;
            Volume = volume;
            Time = ID; // No meaningful time
        }

        public InjectionData(ExperimentData experiment, int id, double volume, double mass, bool include)
        {
            Experiment = experiment;
            ID = id;
            Volume = volume;
            //InjectionMass = mass;
            Include = include;
        }

        public InjectionData(ExperimentData experiment, string line, int id)
        {
            Experiment = experiment;

            ID = id;

            var data = line.Substring(1).Split(',');

            Volume = float.Parse(data[0]) / 1000000f;
            Duration = float.Parse(data[1]);
            Delay = float.Parse(data[2]);
            Filter = float.Parse(data[3]);
            Include = ID > 0;
        }

        public InjectionData(ExperimentData data, float volume, float delay, float filter, float duration)
        {
            Experiment = data;
            Volume = volume;
            Delay = delay;
            Filter = filter;
            Duration = duration;
        }

        private InjectionData(ExperimentData data, int id, float time, double volume, float delay, float duration, double temp)
        {
            Experiment = data;
            ID = id;
            Time = time;
            Volume = volume;
            Duration = duration;
            Temperature = temp;
            Delay = delay;

            InitializeIntegrationTimes();
        }

        public void InitializeIntegrationTimes()
        {
            IntegrationStartDelay = 0;
            IntegrationEndOffset = 0.8f * Delay;
        }

        public void SetIntegrationStartTime(float delay)
        {
            // start delay cannot be more negative than this number under any circumstances
            // -Delay should be start of previous injection
            // DataPoints first because there might not be datapoinst from the first few seconds
            var absoluteminimum = Math.Max(-Delay, Experiment.DataPoints.First().Time - Time + MinimumIntegrationTime);

            IntegrationStartDelay = Math.Clamp(delay, absoluteminimum, IntegrationEndOffset - MinimumIntegrationTime);
            Experiment?.MarkModified();
        }

        public void SetIntegrationLengthByPeakFitting()
        {
            var endOffset = PeakShapeIntegrationEstimator.EstimateEndOffset(Experiment, this, PeakShapeIntegrationEstimator.DefaultFitFactor);
            SetIntegrationLengthByTime(endOffset);
        }

        public void SetIntegrationLengthByFactor(float factor)
        {
            var endOffset = PeakShapeIntegrationEstimator.EstimateEndOffset(Experiment, this, factor);
            SetIntegrationLengthByTime(endOffset);
        }

        public void SetIntegrationLengthByTime(float time)
        {
            // Keep between the start delay and the injection scope
            IntegrationEndOffset = Math.Clamp(time, IntegrationStartDelay + MinimumIntegrationTime, Delay);
            Experiment?.MarkModified();
        }

        public void ToggleDataPointActive()
        {
            Include = !Include;
            Experiment?.OnInjectionIncludeChanged();
        }

        public void Integrate()
        {
            var data = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime).ToList();
            var area = 0.0;
            var t = IntegrationStartTime;

            foreach (var dp in data)
            {
                var dt = dp.Time - t;
                area += dp.Power * dt;

                t = dp.Time;
            }

            var sd = EstimateError2();
            var peakarea = new FloatWithError(area, sd);

            // Set the peak area and perform buffer subtraction if relevant
            SetPeakArea(peakarea);
        }

        /// <summary>
        /// Set the integrated area and set the IsIntegrated flag. Also performs buffer referencing.
        /// </summary>
        public void SetPeakArea(FloatWithError area)
        {
            RawPeakArea = area;
            UpdateCorrectedPeakArea();

            // Set heat direction based on the area and error
            HeatDirection =
                PeakArea > 3 * PeakArea.SD ? PeakHeatDirection.Endothermal :
                PeakArea < -3 * PeakArea.SD ? PeakHeatDirection.Exothermal :
                PeakHeatDirection.Unknown;

            IsIntegrated = true;
        }

        public void UpdateCorrectedPeakArea()
        {
            // RawPeakArea is the integrated heat; PeakArea is the value after optional subtraction.
            var bufferSubtraction = Experiment?.BufferSubtractionSettings;

            if (bufferSubtraction?.ReferenceExperiment != null)
            {
                var model = BufferSubtractionCalculator.BuildModel(bufferSubtraction.ReferenceExperiment, bufferSubtraction);
                PeakArea = GetCorrectedPeakArea(model);
            }
            else
            {
                PeakArea = RawPeakArea;
            }
        }

        public void UpdateCorrectedPeakArea(BufferSubtractionModel subtractionModel)
        {
            PeakArea = GetCorrectedPeakArea(subtractionModel);
        }

        FloatWithError GetCorrectedPeakArea(BufferSubtractionModel subtractionModel)
        {
            var area = RawPeakArea;

            if (BufferSubtractionCalculator.TryGetReferenceHeat(this, subtractionModel, out var referenceHeat))
                area -= referenceHeat;

            return area;
        }

        private double EstimateError2()
        {
            // Set baseline fitting time points to non-integrated data points surrounding current peak.
            float baseline_start_time = ID == 0 ? 0 : Experiment.Injections[ID - 1].IntegrationEndTime;
            float baseline_end_time = Time + Delay;
            float baseline_exclude_start_time = IntegrationStartTime;

            // Start at previous inj integration end time, but take at least 10s
            baseline_start_time = Math.Min(baseline_start_time, baseline_exclude_start_time - 10);
            // Exclude integration but keep at least the last 10s of the injection scope
            float baseline_exclude_end_time = Math.Min(IntegrationEndTime, baseline_end_time - 10);

            // In case of strange values or very short injection scopes
            if (baseline_exclude_end_time <= baseline_exclude_start_time)
                baseline_exclude_end_time = baseline_exclude_start_time;

            // Collect the baseline points (excluding integrated points)
            var bl = Experiment.BaseLineCorrectedDataPoints.Where(dp =>
                dp.Time >= baseline_start_time
                && dp.Time <= baseline_end_time
                && !(dp.Time > baseline_exclude_start_time && dp.Time < baseline_exclude_end_time)).ToList();

            var med = Statistics.Median(bl.Select(dp => dp.Power).ToList());
            var mad = Statistics.Median(bl.Select(dp => Math.Abs(dp.Power - med)).ToList());
            var sigma0 = 1.4826f * mad;
            if (sigma0 <= 0) sigma0 = 1e-12f;
            var k = 6.0f;
            var cap = k * sigma0;

            var blpoints = bl.Count;

            if (blpoints < 2) return 0;

            // Calculate the RMSD
            double ss = 0;
            int n1 = 0;
            int n2 = 0;
            foreach (var dp in bl)
            {
                var p = dp.Power;

                if (Math.Abs(p) > cap) p = cap;

                ss += p * p;

                if (dp.Time <= baseline_exclude_start_time) n1++;
                else n2++;
            }

            var sigma_p = Math.Sqrt(ss / (blpoints - 1));

            var intpoints = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime).ToList();
            int n_samples_integration = intpoints.Count;

            if (n_samples_integration < 2) return 0;

            float dt = (float)Experiment.TimeStep;
            var r1 = Statistics.EstimateAutoCorrelation(bl, 2 * dt);

            var sumVarInt = Statistics.Ar1SumVarFactor(n_samples_integration, r1);
            var sigma_q = sigma_p * dt * Math.Sqrt(sumVarInt);

            var sumVar = Statistics.Ar1SumVarFactor(n1, r1) + Statistics.Ar1SumVarFactor(n2, r1);
            var sigma_q_bl = (sigma_p * Math.Sqrt(sumVar) / blpoints) * IntegrationLength;

            return Math.Sqrt(sigma_q * sigma_q + sigma_q_bl * sigma_q_bl);
        }

        public InjectionData Copy(ExperimentData data)
        {
            var inj = new InjectionData(data, ID, Time, Volume, Delay, Duration, Temperature)
            {
                //InjectionMass = Volume * data.SyringeConcentration,
                ActualCellConcentration = ActualCellConcentration,
                ActualTitrantConcentration = ActualTitrantConcentration,
                Ratio = Ratio,
                Include = Include,
                IntegrationStartDelay = IntegrationStartDelay,
                IntegrationEndOffset = IntegrationEndOffset
            };

            if (IsIntegrated) inj.SetPeakArea(RawPeakArea);

            return inj;
        }

        public InjectionData CopyWithNewID(ExperimentData data, int id, float timeshift)
        {
            var inj = new InjectionData()
            {
                Experiment = data,
                ID = id,
                Include = this.Include,
                Time = this.Time + timeshift,
                Volume = this.Volume,
                Delay = this.Delay,
                Duration = this.Duration,
                Temperature = this.Temperature,
                IntegrationStartDelay = this.IntegrationStartDelay,
                IntegrationEndOffset = this.IntegrationEndOffset,
            };

            return inj;
        }

        public enum IntegrationLengthMode
        {
            Time,
            Factor,
            Fit
        }

        public override string ToString()
        {
            return $"{ID} : {Ratio:F2} : {Enthalpy2.ToFormattedString(EnergyUnit.KiloJoule)}";
        }
    }
}
