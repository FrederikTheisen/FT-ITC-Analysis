using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    public sealed class UncertaintyPresentationRow
    {
        public string Label { get; }
        public string Value { get; }
        public string Tooltip { get; }
        internal UncertaintyPresentationRow(string label, string value, string tooltip)
        { Label = label; Value = value; Tooltip = tooltip; }
    }

    public sealed class UncertaintyPresentationSection
    {
        public string Title { get; }
        public IReadOnlyList<UncertaintyPresentationRow> Rows { get; }
        internal UncertaintyPresentationSection(string title, List<UncertaintyPresentationRow> rows)
        { Title = title; Rows = rows.AsReadOnly(); }
    }

    /// <summary>Shared saved-evidence presentation for desktop result views and completion messages.</summary>
    public static class UncertaintyEstimationPresentation
    {
        public static IReadOnlyList<FittingCompletionMessage> CompletionMessages(SolverInterface solver,
            SolverConvergence convergence, TimeSpan elapsed, bool resultUpdate = false, GlobalSolution solution = null,
            IEnumerable<BindingAssessmentMember> memberAssessments = null)
        {
            solution ??= solver is GlobalSolver global ? global.Model?.Solution
                : solver is Solver local && local.Model?.Solution != null ? GlobalSolution.FromSingleExperimentSolver(local) : null;
            var messages = new List<FittingCompletionMessage>();
            if (!string.IsNullOrWhiteSpace(convergence?.ResultUpdateRetentionReason))
                return new[] { new FittingCompletionMessage(convergence.ResultUpdateRetentionReason, 5000) };
            if (convergence == null || convergence.Failed)
            {
                var reason = convergence == null ? "Completion information unavailable."
                    : string.IsNullOrWhiteSpace(convergence.FailureReason) ? convergence.Message : convergence.FailureReason;
                return new[] { new FittingCompletionMessage((resultUpdate ? "Result update failed: " : "Analysis failed: ") + reason, 5000) };
            }
            var cancelled = convergence.Stopped || convergence.ErrorEstimationOutcome == ErrorEstimationOutcome.Cancelled
                || solution?.Solutions.Any(member => member?.Convergence?.ErrorEstimationOutcome == ErrorEstimationOutcome.Cancelled) == true;
            var duration = FittingStatusPresentation.Duration(elapsed);
            var text = cancelled ? resultUpdate ? "Update cancelled; the existing result was kept" : "Analysis cancelled after " + duration
                : (resultUpdate ? "Result updated in " : "Analysis complete in ") + duration;
            if (!cancelled && convergence.MaxIterationsReached)
            {
                var limit = convergence.Termination == SolverTermination.EvaluationLimit ? "evaluation"
                    : convergence.Termination == SolverTermination.TimeLimit ? "time" : "iteration";
                text += "; fit stopped at the " + limit + " limit";
            }
            messages.Add(new FittingCompletionMessage(text, cancelled && resultUpdate ? 5000 : 3000));
            if (resultUpdate && cancelled) return messages;
            var assessments = memberAssessments ?? (solution != null ? BindingAssessmentMember.ForFit(solution)
                : solver is Solver single ? BindingAssessmentMember.ForFit(single.Model?.Solution) : Array.Empty<BindingAssessmentMember>());
            var warning = ParameterBoundaryWarningFormatter.Format(convergence.ParameterBoundaryContacts, assessments);
            if (!string.IsNullOrWhiteSpace(warning)) messages.Add(new FittingCompletionMessage(warning, 5000));
            var method = solution?.ErrorEstimationMethod ?? solver?.ErrorEstimationMethod ?? ErrorEstimationMethod.None;
            if (method != ErrorEstimationMethod.None)
                messages.Add(new FittingCompletionMessage(CompletionOutcome(solution, convergence, method), 3000));
            return messages;
        }

        public static void PublishCompletion(SolverInterface solver, SolverConvergence convergence,
            TimeSpan elapsed, bool resultUpdate = false, GlobalSolution solution = null, Guid operationId = default,
            IEnumerable<BindingAssessmentMember> memberAssessments = null)
        {
            if (operationId != Guid.Empty && !Application.ProcessingActivity.IsCurrentOperation(operationId)) return;
            Application.StatusBarManager.ClearAppStatus();
            var messages = CompletionMessages(solver, convergence, elapsed, resultUpdate, solution, memberAssessments);
            foreach (var message in messages)
                Application.StatusBarManager.QueueStatus(message.Text, message.DurationMilliseconds);
            if (convergence?.Stopped == false && elapsed >= TimeSpan.FromSeconds(15) && messages.Count > 0)
                AnalysisITC.Platform.PlatformServices.AppNotificationService.ShowSystemNotificationIfBackground(
                    resultUpdate ? "Result update finished" : "Model fitting finished", messages[0].Text);
        }

        static string CompletionOutcome(GlobalSolution solution, SolverConvergence convergence, ErrorEstimationMethod method)
        {
            var name = method == ErrorEstimationMethod.BootstrapResiduals ? "Bootstrap"
                : method == ErrorEstimationMethod.LeaveOneOut ? "Leave-one-out" : "Profile likelihood";
            if (solution != null && Independent(solution))
            {
                var statuses = solution.Solutions.Select(member => MemberOutcome(member, method)).ToList();
                if (statuses.Distinct().Count() > 1)
                {
                    var order = new[] { "complete", "incomplete", "unavailable", "cancelled", "skipped (no binding)" };
                    return name + ": " + string.Join(", ", order.Where(status => statuses.Contains(status))
                        .Select(status => statuses.Count(value => value == status) + " " + status));
                }
                switch (statuses.FirstOrDefault())
                {
                    case "complete": return name + " complete";
                    case "skipped (no binding)": return name + " skipped: no binding detected";
                    case "cancelled": return name + " cancelled";
                    case "unavailable": return name + " unavailable";
                    case "incomplete" when method != ErrorEstimationMethod.ProfileLikelihood:
                        var members = solution.Solutions;
                        var requests = members.Select(member => ExpectedRefits(member, method)).ToList();
                        return IncompleteRefits(name, members.Sum(member => member?.BootstrapSolutions.Count ?? 0),
                            requests.All(request => request.HasValue) ? requests.Sum(request => request.Value) : (int?)null);
                }
            }
            var outcome = solution == null ? convergence.ErrorEstimationOutcome : Outcome(solution);
            var expected = method == ErrorEstimationMethod.BootstrapResiduals ? convergence.RequestedBootstrapIterations
                : solution?.Model?.ShouldFitIndividually == false ? solution.Model.Models.Count
                : ExpectedRefits(solution?.Solutions.FirstOrDefault(), method);
            expected ??= convergence.ErrorEstimationAttemptedRefits;
            var succeeded = solution?.BootstrapIterations ?? convergence.ErrorEstimationSucceededRefits ?? 0;
            if (method != ErrorEstimationMethod.ProfileLikelihood && expected > succeeded
                && outcome != ErrorEstimationOutcome.SkippedNoBinding && outcome != ErrorEstimationOutcome.Cancelled)
                outcome = succeeded > 0 ? ErrorEstimationOutcome.PartialFailure : ErrorEstimationOutcome.CompleteFailure;
            if (outcome == ErrorEstimationOutcome.SkippedNoBinding) return name + " skipped: no binding detected";
            if (outcome == ErrorEstimationOutcome.Cancelled) return name + " cancelled";
            if (outcome == ErrorEstimationOutcome.Completed) return name + " complete";
            if (outcome != ErrorEstimationOutcome.PartialFailure) return name + " unavailable";
            if (method == ErrorEstimationMethod.ProfileLikelihood && solution != null)
            {
                var summary = ProfileLikelihoodEstimator.Summarize(solution);
                return name + " incomplete: " + summary.EndpointsFound + " of " + summary.TotalSides + " interval endpoints found";
            }
            return IncompleteRefits(name, succeeded, expected);
        }

        static string IncompleteRefits(string name, int succeeded, int? total) =>
            name + " incomplete: " + succeeded + (total.HasValue ? " of " + total.Value : "") + " refits";

        static int? ExpectedRefits(SolutionInterface member, ErrorEstimationMethod method) =>
            method == ErrorEstimationMethod.LeaveOneOut
                ? member?.Data?.Injections.Count(injection => injection.Include)
                : member?.Convergence?.RequestedBootstrapIterations ?? member?.Convergence?.ErrorEstimationAttemptedRefits;

        static string MemberOutcome(SolutionInterface member, ErrorEstimationMethod method)
        {
            var outcome = member?.ProfileLikelihoodRun?.Outcome ?? member?.Convergence?.ErrorEstimationOutcome ?? ErrorEstimationOutcome.NotRun;
            if (outcome == ErrorEstimationOutcome.SkippedNoBinding) return "skipped (no binding)";
            if (outcome == ErrorEstimationOutcome.Cancelled) return "cancelled";
            if (method != ErrorEstimationMethod.ProfileLikelihood)
            {
                var count = member?.BootstrapSolutions.Count ?? 0;
                if (count == 0) return "unavailable";
                if (outcome is ErrorEstimationOutcome.PartialFailure or ErrorEstimationOutcome.CompleteFailure
                    || ExpectedRefits(member, method) > count) return "incomplete";
            }
            return outcome == ErrorEstimationOutcome.Completed ? "complete"
                : outcome == ErrorEstimationOutcome.PartialFailure ? "incomplete" : "unavailable";
        }

        public const string MethodTooltip = "Method selected to estimate fitted parameter uncertainty";
        public const string UpdateIterationsTooltip = "Requested refits for this complete rerun";
        const string MixedTooltip = "Some experiments skipped estimation because no binding was detected. Eligible experiments retain their own uncertainty; complete combined uncertainty is unavailable.";

        public static string Method(GlobalSolution solution) => solution?.ErrorEstimationMethod.Description() ?? "";

        public static string BootstrapIterations(GlobalSolution solution) =>
            solution?.ErrorEstimationMethod != ErrorEstimationMethod.BootstrapResiduals ? "Not applicable"
                : solution.Convergence?.RequestedBootstrapIterations?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unknown";

        static bool Independent(GlobalSolution solution) =>
            solution?.Model?.ShouldFitIndividually == true && solution.Solutions.Count > 1;

        static ErrorEstimationOutcome Outcome(GlobalSolution solution)
        {
            if (solution.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood)
                return ProfileLikelihoodEstimator.Summarize(solution).Outcome;
            var outcome = solution.Convergence?.ErrorEstimationOutcome ?? ErrorEstimationOutcome.NotRun;
            var members = solution.Solutions;
            if (members.Any(member => member?.Convergence?.ErrorEstimationOutcome == ErrorEstimationOutcome.Cancelled)
                || outcome == ErrorEstimationOutcome.Cancelled) return ErrorEstimationOutcome.Cancelled;
            var independent = Independent(solution);
            var eligible = independent
                ? members.Where(member => member?.Convergence?.ErrorEstimationOutcome != ErrorEstimationOutcome.SkippedNoBinding).ToList()
                : members;
            if (outcome == ErrorEstimationOutcome.SkippedNoBinding && (!independent || eligible.Count == 0)) return outcome;
            var usable = solution.BootstrapIterations > 0 || eligible.Any(member => member?.BootstrapSolutions.Count > 0);
            var missing = independent ? eligible.Any(member => member?.BootstrapSolutions.Count == 0)
                : solution.BootstrapIterations == 0;
            var failed = eligible.Any(member => member?.Convergence?.ErrorEstimationOutcome
                is ErrorEstimationOutcome.PartialFailure or ErrorEstimationOutcome.CompleteFailure);
            var shortRequest = solution.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
                && (independent ? eligible.Any(member => member?.Convergence?.RequestedBootstrapIterations > member?.BootstrapSolutions.Count)
                    : solution.Convergence?.RequestedBootstrapIterations > solution.BootstrapIterations);
            if (missing || failed || shortRequest || outcome is ErrorEstimationOutcome.PartialFailure or ErrorEstimationOutcome.CompleteFailure)
                return usable ? ErrorEstimationOutcome.PartialFailure : ErrorEstimationOutcome.CompleteFailure;
            return outcome;
        }

        static string Status(GlobalSolution solution, ErrorEstimationOutcome outcome)
        {
            if (outcome == ErrorEstimationOutcome.Completed && Independent(solution)
                && solution.Solutions.Any(member => member?.Convergence?.ErrorEstimationOutcome == ErrorEstimationOutcome.SkippedNoBinding))
                return "Mixed";
            return ProfileLikelihoodDisplayFormatter.Status(outcome);
        }

        public static UncertaintyPresentationSection Section(GlobalSolution solution)
        {
            if (solution == null || solution.ErrorEstimationMethod == ErrorEstimationMethod.None) return null;
            var method = solution.ErrorEstimationMethod;
            var outcome = Outcome(solution);
            var status = Status(solution, outcome);
            var rows = new List<UncertaintyPresentationRow>
            {
                new UncertaintyPresentationRow("Status", status, status == "Mixed" ? MixedTooltip
                    : outcome == ErrorEstimationOutcome.PartialFailure && method == ErrorEstimationMethod.BootstrapResiduals
                        ? "Some expected bootstrap refits are missing."
                        : "Availability of saved fitted parameter uncertainty")
            };
            var profile = method == ErrorEstimationMethod.ProfileLikelihood ? ProfileLikelihoodEstimator.Summarize(solution) : null;
            if (outcome != ErrorEstimationOutcome.SkippedNoBinding)
            {
                if (method == ErrorEstimationMethod.BootstrapResiduals)
                    rows.Add(new UncertaintyPresentationRow("Bootstraps", BootstrapIterations(solution), BootstrapTooltip(solution)));
                if (profile != null)
                    rows.Add(new UncertaintyPresentationRow("95% CI endpoints", ProfileLikelihoodDisplayFormatter.Endpoints(profile), "Found limits of the model-estimated confidence intervals"));
                rows.Add(new UncertaintyPresentationRow("Calculation time", solution.Convergence == null ? "Unknown"
                    : ProfileLikelihoodDisplayFormatter.Duration(profile?.Elapsed ?? solution.Convergence.ErrorEstimationTime), "Elapsed time for uncertainty estimation"));
            }
            var options = solution.ModelCloneOptions;
            if (method == ErrorEstimationMethod.BootstrapResiduals || options?.HasLegacyCombinedLeaveOneOut == true)
            {
                var historical = method == ErrorEstimationMethod.LeaveOneOut;
                var concentration = options == null ? "Unknown" : !options.IncludeConcentrationErrorsInBootstrap ? "Not included"
                    : options.EnableAutoConcentrationVariance
                        ? $"Included · Legacy automatic {(100 * options.AutoConcentrationVariance).ToString("0.0", CultureInfo.CurrentCulture)}%"
                        : "Included · Experiment SDs";
                rows.Add(new UncertaintyPresentationRow("Concentration uncertainty", concentration,
                    historical ? "Saved concentration sampling in historical combined leave-one-out calculation"
                        : "Saved concentration sampling setting for bootstrap refits"));
                rows.Add(new UncertaintyPresentationRow("Parameter unlocking", options == null ? "Unknown"
                    : options.UnlockBootstrapParameters ? "Enabled" : "Disabled",
                    historical ? "Saved parameter unlocking in historical combined leave-one-out calculation"
                        : "Saved setting for varying primary-fit locked parameters"));
            }
            return new UncertaintyPresentationSection(method == ErrorEstimationMethod.BootstrapResiduals ? "Bootstrap"
                : method == ErrorEstimationMethod.LeaveOneOut ? "Leave-one-out" : "Profile likelihood", rows);
        }

        static string BootstrapTooltip(GlobalSolution solution)
        {
            var request = solution.Convergence?.RequestedBootstrapIterations;
            var combined = solution.BootstrapIterations;
            var tooltip = request.HasValue ? $"Requested bootstrap refits: {request.Value:N0}." : "Requested bootstrap count was not saved.";
            if (!request.HasValue || request.Value != combined)
                tooltip += Independent(solution) ? $" Retained complete combined replicates: {combined:N0}."
                    : $" Retained complete replicates: {combined:N0}.";
            if (Independent(solution) && solution.Solutions.Any(member => member?.Convergence?.ErrorEstimationOutcome == ErrorEstimationOutcome.SkippedNoBinding))
                tooltip += " Eligible experiments retain their own refits; a complete combined set is unavailable.";
            return tooltip;
        }

        public static string Completion(GlobalSolution solution)
        {
            var section = Section(solution);
            if (section == null) return "";
            return section.Title + ": " + string.Join(" | ", section.Rows
                .Where(row => row.Label == "Status" || row.Label == "95% CI endpoints" || row.Label == "Calculation time")
                .Select(row => row.Label == "Status" ? row.Value : row.Label + ": " + row.Value));
        }

        public static string Completion(SolverInterface solver, SolverConvergence convergence)
        {
            if (solver is GlobalSolver global) return Completion(global.Model?.Solution);
            if (!(solver is Solver local) || local.Model?.Solution == null || solver.ErrorEstimationMethod == ErrorEstimationMethod.None) return "";
            var member = local.Model.Solution;
            var outcome = convergence?.ErrorEstimationOutcome ?? ErrorEstimationOutcome.NotRun;
            var run = member.ProfileLikelihoodRun;
            if (run?.Outcome == ErrorEstimationOutcome.Cancelled) outcome = ErrorEstimationOutcome.Cancelled;
            var title = solver.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals ? "Bootstrap"
                : solver.ErrorEstimationMethod == ErrorEstimationMethod.LeaveOneOut ? "Leave-one-out" : "Profile likelihood";
            if (outcome == ErrorEstimationOutcome.SkippedNoBinding)
                return title + ": " + SolverConvergence.SkippedForNoBindingDescription;
            if (solver.ErrorEstimationMethod != ErrorEstimationMethod.ProfileLikelihood && outcome != ErrorEstimationOutcome.Cancelled)
            {
                var usable = member.BootstrapSolutions.Count > 0;
                var missing = !usable || solver.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
                    && convergence?.RequestedBootstrapIterations > member.BootstrapSolutions.Count;
                if (missing || outcome is ErrorEstimationOutcome.PartialFailure or ErrorEstimationOutcome.CompleteFailure)
                    outcome = usable ? ErrorEstimationOutcome.PartialFailure : ErrorEstimationOutcome.CompleteFailure;
            }
            var text = title + ": " + ProfileLikelihoodDisplayFormatter.Status(outcome);
            if (solver.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood)
            {
                var endpoints = run?.Coordinates.Sum(coordinate => (coordinate.Lower.IsEndpointFound ? 1 : 0) + (coordinate.Upper.IsEndpointFound ? 1 : 0)) ?? 0;
                text += " | 95% CI endpoints: " + ProfileLikelihoodDisplayFormatter.Endpoints(endpoints,
                    2 * (run?.ParameterCount ?? local.Model.Parameters.GetFittedParameters().Length));
            }
            return text + " | Calculation time: " + ProfileLikelihoodDisplayFormatter.Duration(run?.Elapsed ?? convergence?.ErrorEstimationTime ?? TimeSpan.Zero);
        }

        public static string UpdateStatus(GlobalSolution solution, SolverConvergence convergence)
        {
            if (!string.IsNullOrWhiteSpace(convergence?.ResultUpdateRetentionReason)) return convergence.ResultUpdateRetentionReason;
            if (convergence == null) return "Result update unavailable";
            var status = $"{convergence.Algorithm.GetProperties().ShortName} | RMSD = {convergence.UnweightedRmsd:G4}";
            var uncertainty = Completion(solution);
            return string.IsNullOrEmpty(uncertainty) ? status : status + " | " + uncertainty;
        }

        public static string UpdateCompletion(GlobalSolution solution, SolverConvergence convergence)
            => !string.IsNullOrWhiteSpace(convergence?.ResultUpdateRetentionReason)
                ? convergence.ResultUpdateRetentionReason : Completion(solution);
    }
}
