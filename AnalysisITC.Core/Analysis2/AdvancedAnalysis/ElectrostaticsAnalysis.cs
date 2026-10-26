using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    public class ElectrostaticsAnalysis : AdvancedAnalysis
    {
        readonly object inputLock = new object();
        ComponentInputs saltInputs, ionicInputs;
        bool saltInputsChanged, ionicInputsChanged;
        LinearFitWithError counterIonReleaseFit;
        IonicStrengthDependenceFit ionicStrengthDependenceFit;
        int counterIonReleaseIterations;

        public bool Calculated { get { InvalidateChangedInputs(); return saltInputs != null || ionicInputs != null; } }
        public bool CompletedIncludeBufferInIonicStrengthCalc { get; private set; }
        public Energy ElectrostaticStrength { get; private set; } = new(0);
        public FloatWithError CounterIonRelease => CounterIonReleaseFit?.Slope ?? FloatWithError.NaN;
        public FloatWithError Kd0 => IonicStrengthDependenceFit?.Kd0 ?? FloatWithError.NaN;
        public FloatWithError KdInf { get; private set; } = FloatWithError.NaN;

        public IonicStrengthDependenceFit IonicStrengthDependenceFit { get { InvalidateChangedInputs(); return ionicStrengthDependenceFit; } }
        public LinearFitWithError CounterIonReleaseFit { get { InvalidateChangedInputs(); return counterIonReleaseFit; } }
        public int CounterIonReleaseIterations { get { InvalidateChangedInputs(); return counterIonReleaseIterations; } }
        public override int CompletedIterations
        {
            get { InvalidateChangedInputs(); return base.CompletedIterations; }
            internal set => base.CompletedIterations = value;
        }
        public override DateTime? CompletedAtUtc
        {
            get { InvalidateChangedInputs(); return base.CompletedAtUtc; }
            internal set => base.CompletedAtUtc = value;
        }
        public bool InputsChanged { get { InvalidateChangedInputs(); return saltInputsChanged || ionicInputsChanged; } }

        public bool HasIonicStrengthResult => Calculated && IonicStrengthDependenceFit != null
            && ElectrostaticsAnalysisAvailability.IsUsable(IonicStrengthDependenceFit.Kd0)
            && IonicStrengthDependenceFit.Kd0.Value > 0
            && ElectrostaticsAnalysisAvailability.IsUsable(IonicStrengthDependenceFit.SaltSensitivity)
            && (!IonicStrengthDependenceFit.UsesCurvature || ElectrostaticsAnalysisAvailability.IsUsable(IonicStrengthDependenceFit.Curvature));
        public bool HasCounterIonResult => Calculated && CounterIonReleaseFit != null
            && ElectrostaticsAnalysisAvailability.IsUsable(CounterIonReleaseFit.Slope)
            && ElectrostaticsAnalysisAvailability.IsUsable(CounterIonReleaseFit.Intercept);

        public string IonicStrengthUnavailableReason => HasIonicStrengthResult ? string.Empty
            : ionicInputsChanged ? ElectrostaticsAnalysisAvailability.InputsChangedReason
            : !Data.IsElectrostaticsAnalysisDependenceEnabled ? Data.ElectrostaticsAnalysisUnavailableReason
            : (Calculated ? "No usable ionic-strength fit was obtained. " : string.Empty)
                + ElectrostaticsAnalysisAvailability.IonicStrengthRequirement(Data.SaltAnalysisMinimumSpan);
        public string CounterIonUnavailableReason
        {
            get
            {
                if (HasCounterIonResult) return string.Empty;
                if (saltInputsChanged) return ElectrostaticsAnalysisAvailability.InputsChangedReason;
                var reason = ElectrostaticsAnalysisAvailability.GetSaltDependenceMetadataReason(Data.Solution.Solutions, out _);
                if (reason.Length > 0) return reason;
                if (!Data.IsElectrostaticsAnalysisDependenceEnabled) return Data.ElectrostaticsAnalysisUnavailableReason;
                return GetSaltDependencePoints().Select(point => point.X).Distinct().Count() < 3
                    ? ElectrostaticsAnalysisAvailability.CounterIonRequirement
                    : Calculated ? "No salt-dependence result is available. Recalculate the analysis."
                    : "Run the analysis to calculate the salt-dependence slope.";
            }
        }

        public string SaltDependenceCaption
        {
            get
            {
                var reason = ElectrostaticsAnalysisAvailability.GetSaltDependenceMetadataReason(Data.Solution.Solutions, out var salt);
                return reason.Length == 0 && salt.HasValue ? "Salt dependence (" + salt.Value.GetProperties().Name + ")" : "Salt dependence";
            }
        }

        public override async Task<bool> PerformAnalysisAsync()
        {
            InvalidateChangedInputs();
            if (!Data.IsElectrostaticsAnalysisDependenceEnabled) return false;
            try { return await base.PerformAnalysisAsync(); }
            finally { InvalidateChangedInputs(); }
        }

        public ElectrostaticsAnalysis(AnalysisResult result) : base(result)
        {
        }

        public double GetPlotIonicStrength(ExperimentData data)
            => HasIonicStrengthResult
                ? BufferAttribute.GetIonicStrength(data, CompletedIncludeBufferInIonicStrengthCalc)
                : Data.GetSaltAnalysisIonicStrength(data);

        public List<Tuple<double, FloatWithError>> GetDataPoints(DissocFitMode mode)
        {
            var dataPoints = new List<Tuple<double, FloatWithError>>();

            switch (mode)
            {
                case DissocFitMode.AffinityVsSalt: // Displace a bit around to show all points
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        double ions = 0.0;

                        ions = 1000 * sol.Data.Attributes.Find(att => att.Key == AttributeKey.Salt)?.ParameterValue ?? 0.0;

                        var kd = sol.ReportParameters[ParameterType.Affinity1];
                        dataPoints.Add(new Tuple<double, FloatWithError>(ions, kd));
                    }
                    break;
                case DissocFitMode.CounterIonRelease:
                    dataPoints.AddRange(GetSaltDependencePoints().Select(point => Tuple.Create(point.X, point.Y)));
                    break;
                case DissocFitMode.DebyeHuckel: // Join data points
                    foreach (var sol in Data.Solution.Solutions)
                    {
                        dataPoints.Add(new Tuple<double, FloatWithError>(
                            GetPlotIonicStrength(sol.Data),
                            sol.ReportParameters[ParameterType.Affinity1]));
                    }
                    break;
            }

            return dataPoints;
        }

        protected override void Calculate()
        {
            // Calculate from these captured values, rather than reading mutable inputs
            // again during the run. Only matching components can be published.
            var nextSaltInputs = CaptureInputs(ionic: false, includeBuffer: false);
            var nextIonicInputs = CaptureInputs(ionic: true, includeBuffer: Data.SaltAnalysisIncludesBuffer);
            var ionTransfer = CalculateIonTransfer(nextSaltInputs);
            if (ResultAnalysisController.TerminateAnalysisFlag.Up) return;

            var ionicStrength = CalculateIonicStrengthDependence(nextIonicInputs);
            if (ResultAnalysisController.TerminateAnalysisFlag.Up) return;

            lock (inputLock)
            {
                counterIonReleaseFit = ionTransfer.Fit;
                counterIonReleaseIterations = ionTransfer.Iterations;
                ionicStrengthDependenceFit = ionicStrength.Fit;
                base.CompletedIterations = ionicStrength.Iterations;
                CompletedIncludeBufferInIonicStrengthCalc = nextIonicInputs.IncludeBuffer;
                saltInputs = nextSaltInputs;
                ionicInputs = nextIonicInputs;
                saltInputsChanged = ionicInputsChanged = false;
                InvalidateChangedInputs();
            }
        }

        // All fitting and plotting consumers use these same coordinates and complete intervals.
        public List<(SolutionInterface Solution, double X, FloatWithError Y)> GetSaltDependencePoints()
        {
            var inputs = CaptureInputs(ionic: false, includeBuffer: false);
            if (!inputs.SingleSaltValid) return new List<(SolutionInterface, double, FloatWithError)>();
            return inputs.Members.Select((member, index) => (member, index)).Where(item => item.member.Included)
                .Select(item => (Data.Solution.Solutions[item.index], Math.Log(item.member.X), FWEMath.Log(item.member.Affinity)))
                .ToList();
        }

        IonTransferResult CalculateIonTransfer(ComponentInputs inputs)
        {
            AppEventHandler.PrintAndLog("Performing Salt-dependence Analysis...");

            var dps = inputs.SingleSaltValid
                ? inputs.Members.Where(member => member.Included).Select(member => (Math.Log(member.X), FWEMath.Log(member.Affinity))).ToList()
                : new List<(double, FloatWithError)>();
            if (dps.Select(point => point.Item1).Distinct().Count() < 3)
                return new IonTransferResult(FloatWithError.NaN, null, 0);

            var result = FitLinear(dps.Select(dp => dp.Item1).ToArray(), dps.Select(dp => dp.Item2.Value).ToArray());
            if (result == null) return new IonTransferResult(FloatWithError.NaN, null, 0);
            var results = new List<LinearFit>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var _dps = GetErrorData(dps);

                var fitted = FitLinear(_dps.Select(dp => dp.Item1).ToArray(), _dps.Select(dp => dp.Item2).ToArray());
                if (fitted != null) results.Add(fitted);

                ResultAnalysisController.ReportCalculationProgress(i + 1, description: "Salt Dependence");
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            var slope = new FloatWithError(results.Select(r => r.Slope), result.Slope);
            var intercept = new FloatWithError(results.Select(r => r.Intercept), result.Intercept);

            return new IonTransferResult(slope, new LinearFitWithError(slope, intercept, 0), results.Count);
        }

        IonicStrengthResult CalculateIonicStrengthDependence(ComponentInputs inputs)
        {
            AppEventHandler.PrintAndLog("Performing Electrostatics Analysis...");

            var dps = inputs.Members.Where(member => member.Included).Select(member => (member.X, member.Affinity)).ToList();

            if (dps.Count < 3)
            {
                return new IonicStrengthResult(null, 0);
            }

            var point = IonicStrengthDependence.FitIonicStrengthDependence(
                dps.Select(dp => dp.Item1).ToArray(),
                dps.Select(dp => dp.Item2.Value).ToArray());

            if (point == null)
            {
                return new IonicStrengthResult(null, 0);
            }

            var results = new List<IonicStrengthDependence>();

            for (int i = 0; i < ResultAnalysisController.CalculationIterations; i++)
            {
                var _dps = GetErrorData(dps);

                var r = IonicStrengthDependence.FitIonicStrengthDependence(
                    _dps.Select(dp => dp.Item1).ToArray(),
                    _dps.Select(dp => dp.Item2).ToArray());

                if (r != null) results.Add(r);

                ResultAnalysisController.ReportCalculationProgress(i + 1, description: "Ionic Strength");
                if (ResultAnalysisController.TerminateAnalysisFlag.Up) break;
            }

            // Fall back to point estimate if bootstrap failed completely
            var kd0 = results.Count > 0
                ? new FloatWithError(results.Select(r => r.Kd0), point.Kd0)
                : new FloatWithError(point.Kd0);

            var sensitivity = results.Count > 0
                ? new FloatWithError(results.Select(r => r.SaltSensitivity), point.SaltSensitivity)
                : new FloatWithError(point.SaltSensitivity);

            var curvature = results.Count > 0
                ? new FloatWithError(results.Select(r => r.Curvature), point.Curvature)
                : new FloatWithError(point.Curvature);

            var fit = new IonicStrengthDependenceFit(kd0, sensitivity, curvature, point.UsesCurvature);

            AppEventHandler.PrintAndLog($"Kd0 = {kd0}", 1);
            AppEventHandler.PrintAndLog($"sensitivity = {sensitivity}", 1);
            AppEventHandler.PrintAndLog($"curvature = {curvature}", 1);
            return new IonicStrengthResult(fit, results.Count);
        }

        ComponentInputs CaptureInputs(bool ionic, bool includeBuffer)
        {
            var inputs = new ComponentInputs { IncludeBuffer = includeBuffer };
            foreach (var solution in Data.Solution.Solutions)
            {
                var data = solution.Data;
                var validSalt = SaltAttribute.TryGetSingleSaltConcentration(data, out var salt, out var concentration);
                var hasSalt = data.Attributes.Any(attribute => attribute.Key == AttributeKey.Salt);
                inputs.HasSaltMetadata &= hasSalt;
                var x = ionic ? BufferAttribute.GetIonicStrength(data, includeBuffer) : concentration;
                var included = FWEMath.IsFinite(x) && (ionic ? x >= 0 : x > 0)
                    && ElectrostaticsAnalysisAvailability.HasUsableAffinity(solution);
                var affinity = included ? solution.ReportParameters[ParameterType.Affinity1] : FloatWithError.NaN;
                inputs.Members.Add(new InputMember
                {
                    SolutionId = solution.Guid, ExperimentId = data.UniqueID,
                    Valid = ionic ? hasSalt : validSalt, Salt = ionic ? null : salt, X = x, Included = included,
                    // Copy the interval as well: FloatWithError contains a mutable endpoint array.
                    Affinity = new FloatWithError(affinity.Value, affinity.SD, affinity.Lower, affinity.Upper)
                });
            }
            return inputs;
        }

        void InvalidateChangedInputs()
        {
            lock (inputLock)
            {
                if (saltInputs != null && !saltInputs.EquivalentTo(CaptureInputs(ionic: false, includeBuffer: false)))
                {
                    saltInputs = null;
                    counterIonReleaseFit = null;
                    counterIonReleaseIterations = 0;
                    saltInputsChanged = true;
                }
                if (ionicInputs != null && !ionicInputs.EquivalentTo(CaptureInputs(ionic: true, includeBuffer: CompletedIncludeBufferInIonicStrengthCalc)))
                {
                    ionicInputs = null;
                    ionicStrengthDependenceFit = null;
                    ionicInputsChanged = true;
                }
                if (ionicInputsChanged) base.CompletedIterations = 0;
                if (saltInputs == null && ionicInputs == null)
                {
                    base.CompletedAtUtc = null;
                    CompletedErrorEstimationMethod = null;
                }
            }
        }

        protected override void CommitRunState() => InvalidateChangedInputs();

        protected override object CaptureCommittedState()
        {
            lock (inputLock)
            {
                InvalidateChangedInputs();
                return new CommittedState
                {
                    IncludeBuffer = CompletedIncludeBufferInIonicStrengthCalc,
                    CounterIonReleaseFit = counterIonReleaseFit,
                    CounterIonReleaseIterations = counterIonReleaseIterations,
                    IonicStrengthDependenceFit = ionicStrengthDependenceFit,
                    SaltInputs = saltInputs, IonicInputs = ionicInputs,
                    SaltInputsChanged = saltInputsChanged, IonicInputsChanged = ionicInputsChanged,
                };
            }
        }

        protected override void RestoreCommittedState(object state)
        {
            lock (inputLock)
            {
                var previous = state as CommittedState;
                CompletedIncludeBufferInIonicStrengthCalc = previous?.IncludeBuffer == true;
                counterIonReleaseFit = previous?.CounterIonReleaseFit;
                counterIonReleaseIterations = previous?.CounterIonReleaseIterations ?? 0;
                ionicStrengthDependenceFit = previous?.IonicStrengthDependenceFit;
                saltInputs = previous?.SaltInputs;
                ionicInputs = previous?.IonicInputs;
                saltInputsChanged = previous?.SaltInputsChanged == true;
                ionicInputsChanged = previous?.IonicInputsChanged == true;
            }
        }

        internal void RestoreResult(
            IonicStrengthDependenceFit ionicStrengthFit,
            LinearFitWithError counterIonReleaseFit,
            int ionicStrengthIterations,
            int counterIonReleaseIterations,
            DateTime? completedAtUtc,
            ErrorEstimationMethod errorMethod,
            bool includeBufferInIonicStrengthCalc)
        {
            lock (inputLock)
            {
                ionicStrengthDependenceFit = ionicStrengthFit;
                this.counterIonReleaseFit = counterIonReleaseFit;
                this.counterIonReleaseIterations = counterIonReleaseIterations;
                CompletedIncludeBufferInIonicStrengthCalc = includeBufferInIonicStrengthCalc;
                saltInputs = CaptureInputs(ionic: false, includeBuffer: false);
                ionicInputs = CaptureInputs(ionic: true, includeBuffer: includeBufferInIonicStrengthCalc);
                saltInputsChanged = ionicInputsChanged = false;
                RestoreRunMetadata(ionicStrengthIterations, completedAtUtc, errorMethod);
            }
        }

        sealed class CommittedState
        {
            public bool IncludeBuffer { get; set; }
            public LinearFitWithError CounterIonReleaseFit { get; set; }
            public int CounterIonReleaseIterations { get; set; }
            public IonicStrengthDependenceFit IonicStrengthDependenceFit { get; set; }
            public ComponentInputs SaltInputs { get; set; }
            public ComponentInputs IonicInputs { get; set; }
            public bool SaltInputsChanged { get; set; }
            public bool IonicInputsChanged { get; set; }
        }

        // Transient evidence only; existing files continue to restore their saved outputs.
        sealed class ComponentInputs
        {
            public bool IncludeBuffer;
            public bool HasSaltMetadata = true;
            public readonly List<InputMember> Members = new List<InputMember>();
            public bool SingleSaltValid => Members.All(member => member.Valid)
                && Members.Where(member => member.Salt.HasValue).Select(member => member.Salt).Distinct().Count() <= 1;
            public bool EquivalentTo(ComponentInputs other) => IncludeBuffer == other.IncludeBuffer
                && HasSaltMetadata == other.HasSaltMetadata && Members.Count == other.Members.Count
                && Members.Zip(other.Members, (left, right) => left.Key.Equals(right.Key)).All(equal => equal);
        }

        sealed class InputMember
        {
            public string SolutionId, ExperimentId;
            public bool Valid, Included;
            public Salt? Salt;
            public double X;
            public FloatWithError Affinity;
            public (string, string, bool, Salt?, double, bool, (double, double, double, double)) Key
                => (SolutionId, ExperimentId, Valid, Salt, X, Included, (Affinity.Value, Affinity.SD, Affinity.Lower, Affinity.Upper));
        }

        readonly struct IonTransferResult
        {
            public IonTransferResult(FloatWithError counterIonRelease, LinearFitWithError fit, int iterations)
            { CounterIonRelease = counterIonRelease; Fit = fit; Iterations = iterations; }
            public FloatWithError CounterIonRelease { get; }
            public LinearFitWithError Fit { get; }
            public int Iterations { get; }
        }

        readonly struct IonicStrengthResult
        {
            public IonicStrengthResult(IonicStrengthDependenceFit fit, int iterations)
            { Fit = fit; Iterations = iterations; }
            public IonicStrengthDependenceFit Fit { get; }
            public int Iterations { get; }
        }

        LinearFit FitLinear(double[] x, double[] y)
        {
            try
            {
                var fit = MathNet.Numerics.Fit.Line(x, y);

                return FWEMath.IsFinite(fit.A) && FWEMath.IsFinite(fit.B)
                    ? new LinearFit(fit.B, fit.A, 0) : null;
            }
            catch
            {
                return null;
            }
        }

        public enum DissocFitMode 
        {
            [Description("Affinity vs Salt")]
            AffinityVsSalt,
            [Description("Debye-Hückel")]
            DebyeHuckel,
            [Description(ElectrostaticsAnalysisAvailability.SaltDependenceModeLabel)]
            CounterIonRelease,
        }
    }
}
