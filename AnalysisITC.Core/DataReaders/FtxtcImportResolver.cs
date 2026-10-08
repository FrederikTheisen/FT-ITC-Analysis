using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Platform;

namespace AnalysisITC.Core.DataReaders
{
    public sealed class FtxtcDuplicateSummary
    {
        public FtxtcDuplicateSummary() { }

        public FtxtcDuplicateSummary(string fileName, int experimentCount, int resultCount, int reportCount,
            int solutionCount, IEnumerable<string> names)
        {
            FileName = fileName;
            ExperimentCount = experimentCount;
            ResultCount = resultCount;
            ReportCount = reportCount;
            SolutionCount = solutionCount;
            Names = (names ?? Enumerable.Empty<string>()).ToArray();
        }
        public string FileName { get; internal set; }
        public int ExperimentCount { get; internal set; }
        public int ResultCount { get; internal set; }
        public int ReportCount { get; internal set; }
        public int SolutionCount { get; internal set; }
        public IReadOnlyList<string> Names { get; internal set; } = Array.Empty<string>();
        internal bool HasDuplicates => ExperimentCount + ResultCount + ReportCount + SolutionCount > 0;
    }

    internal sealed class FtxtcImportContent
    {
        internal ITCDataContainer[] Containers { get; set; }
        internal AnalysisReport[] Reports { get; set; }
    }

    /// <summary>Resolves duplicate saved identities before publishing an incoming project.</summary>
    internal static class FtxtcImportResolver
    {
        internal static FtxtcImportContent Resolve(string path, IEnumerable<ITCDataContainer> incoming,
            IEnumerable<AnalysisReport> incomingReports)
        {
            var content = new FtxtcImportContent
            {
                Containers = incoming.Where(item => item != null).ToArray(),
                Reports = (incomingReports ?? Enumerable.Empty<AnalysisReport>()).ToArray(),
            };
            var existingRoots = DataManager.SourceItems.Concat<ITCDataContainer>(DataManager.Reports).ToArray();
            var existingIds = new HashSet<string>(existingRoots.Select(item => item.UniqueID), StringComparer.Ordinal);
            var graph = new Graph(content.Containers);
            var existingGraph = new Graph(DataManager.SourceItems);
            var existingFitIds = new HashSet<string>(existingGraph.Solutions.Select(solution => solution.Guid)
                .Concat(existingGraph.Globals.Select(global => global.UniqueID)), StringComparer.Ordinal);
            var duplicateRoots = content.Containers.Concat<ITCDataContainer>(content.Reports)
                .Where(item => existingIds.Contains(item.UniqueID)).ToArray();
            var summary = new FtxtcDuplicateSummary
            {
                FileName = Path.GetFileName(path),
                ExperimentCount = duplicateRoots.OfType<ExperimentData>().Count(),
                ResultCount = duplicateRoots.OfType<AnalysisResult>().Count(),
                ReportCount = duplicateRoots.OfType<AnalysisReport>().Count(),
                SolutionCount = graph.Solutions.Select(solution => solution.Guid)
                    .Concat(graph.Globals.Select(global => global.UniqueID)).Distinct(StringComparer.Ordinal)
                    .Count(existingFitIds.Contains),
                Names = duplicateRoots.Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray(),
            };
            if (!summary.HasDuplicates) return content;

            if (PlatformServices.FtxtcDuplicatePromptService.ChooseAction(summary) == FtxtcDuplicateAction.ImportCopies)
                Copy(content, graph, existingRoots, existingGraph);
            else
                Skip(content, existingIds);
            return content;
        }

        static void Skip(FtxtcImportContent content, ISet<string> existingIds)
        {
            content.Containers = content.Containers.Where(item => !existingIds.Contains(item.UniqueID)).ToArray();
            content.Reports = content.Reports.Where(item => !existingIds.Contains(item.UniqueID)).ToArray();
            var experiments = DataManager.Data.GroupBy(item => item.UniqueID, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            // Retain incoming result models rather than reparenting an existing
            // saved solution. Equal solution IDs keep their persisted identity.
            // Bootstrap models keep their sampled experimental inputs. Saved
            // primary and Null models use the retained project experiments.
            foreach (var model in new Graph(content.Containers).PrimaryModels)
                if (experiments.TryGetValue(model.Data.UniqueID, out var existing))
                    model.BindImportedExperiment(existing);
        }

        static void Copy(FtxtcImportContent content, Graph graph, IEnumerable<ITCDataContainer> existingRoots, Graph existingGraph)
        {
            var roots = content.Containers.Concat<ITCDataContainer>(content.Reports).ToArray();
            var reserved = new HashSet<string>(existingRoots.Select(item => item.UniqueID)
                .Concat(roots.Select(item => item.UniqueID))
                .Concat(graph.Globals.Concat(existingGraph.Globals).Select(global => global.UniqueID))
                .Concat(graph.Solutions.Concat(existingGraph.Solutions).Select(solution => solution.Guid)), StringComparer.Ordinal);
            var existingRootIds = new HashSet<string>(existingRoots.Select(item => item.UniqueID), StringComparer.Ordinal);
            var experimentMap = Map(roots.OfType<ExperimentData>().Select(item => item.UniqueID), existingRootIds, reserved);
            var resultMap = Map(roots.OfType<AnalysisResult>().Select(item => item.UniqueID), existingRootIds, reserved);
            var reportMap = Map(content.Reports.Select(item => item.UniqueID), existingRootIds, reserved);
            var globalMap = Map(graph.Globals.Select(global => global.UniqueID),
                new HashSet<string>(existingGraph.Globals.Select(global => global.UniqueID), StringComparer.Ordinal), reserved);
            var solutionMap = Map(graph.Solutions.Select(solution => solution.Guid),
                new HashSet<string>(existingGraph.Solutions.Select(solution => solution.Guid), StringComparer.Ordinal), reserved);

            foreach (var result in roots.OfType<AnalysisResult>()) result.RemapMemberSolutionIds(solutionMap);
            foreach (var experiment in graph.Experiments)
            {
                experiment.SetID(Replace(experiment.UniqueID, experimentMap));
                experiment.SetTandemSourceExperimentIds(experiment.TandemSourceExperimentIds.Select(id => Replace(id, experimentMap)));
                foreach (var attribute in experiment.Attributes)
                    RemapAttribute(attribute, experimentMap, resultMap, globalMap);
            }
            foreach (var result in roots.OfType<AnalysisResult>())
            {
                result.SetID(Replace(result.UniqueID, resultMap));
                foreach (var snapshot in result.ValiditySnapshot?.Experiments ?? Enumerable.Empty<ExperimentFitInputSnapshot>())
                {
                    snapshot.ExperimentID = Replace(snapshot.ExperimentID, experimentMap);
                    foreach (var attribute in snapshot.Attributes ?? new List<ExperimentAttributeSnapshot>())
                    {
                        if (attribute.Key == AttributeKey.BufferSubtraction)
                            attribute.StringValue = Replace(attribute.StringValue, experimentMap);
                        if (attribute.Key == AttributeKey.CompetitorResult)
                        {
                            attribute.StringValue = Replace(attribute.StringValue, resultMap);
                            attribute.SourceSolutionId = Replace(attribute.SourceSolutionId, globalMap);
                        }
                    }
                }
            }
            foreach (var global in graph.Globals)
            {
                global.SetID(Replace(global.UniqueID, globalMap));
                global.ProfileLikelihoodRun = RemapProfile(global.ProfileLikelihoodRun, experimentMap);
                RemapConvergence(global.Convergence, experimentMap);
            }
            foreach (var solution in graph.Solutions)
            {
                solution.SetID(Replace(solution.Guid, solutionMap));
                solution.ParentSolutionID = Replace(solution.ParentSolutionID, globalMap);
                solution.ProfileLikelihoodRun = RemapProfile(solution.ProfileLikelihoodRun, experimentMap);
                RemapConvergence(solution.Convergence, experimentMap);
                foreach (var attribute in solution.ModelOptions.Values)
                    RemapAttribute(attribute, experimentMap, resultMap, globalMap);
            }
            foreach (var comparison in graph.Comparisons)
                foreach (var member in comparison.Members)
                    member.ExperimentId = Replace(member.ExperimentId, experimentMap);
            foreach (var report in content.Reports)
            {
                report.SetID(Replace(report.UniqueID, reportMap));
                report.SetResultIds(report.ResultIds.Select(id => Replace(id, resultMap)));
                report.SetSupportingExperimentIds(report.SupportingExperimentIds.Select(id => Replace(id, experimentMap)));
                var context = report.StudyContext;
                foreach (var annotation in context.Experiments)
                    annotation.ExperimentId = Replace(annotation.ExperimentId, experimentMap);
                report.UpdateStudyContext(context);
            }
        }

        static Dictionary<string, string> Map(IEnumerable<string> incoming, ISet<string> existing, ISet<string> reserved)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in incoming.Distinct(StringComparer.Ordinal).Where(existing.Contains))
            {
                string replacement;
                do replacement = Guid.NewGuid().ToString(); while (!reserved.Add(replacement));
                map.Add(id, replacement);
            }
            return map;
        }

        static string Replace(string id, IReadOnlyDictionary<string, string> map) =>
            id != null && map.TryGetValue(id, out var replacement) ? replacement : id;

        static void RemapAttribute(ExperimentAttribute attribute, IReadOnlyDictionary<string, string> experiments,
            IReadOnlyDictionary<string, string> results, IReadOnlyDictionary<string, string> globals)
        {
            if (attribute.Key == AttributeKey.BufferSubtraction)
                attribute.StringValue = Replace(attribute.StringValue, experiments);
            if (attribute.Key == AttributeKey.CompetitorResult)
            {
                attribute.StringValue = Replace(attribute.StringValue, results);
                attribute.SourceSolutionId = Replace(attribute.SourceSolutionId, globals);
            }
        }

        static void RemapConvergence(SolverConvergence convergence, IReadOnlyDictionary<string, string> experiments)
        {
            if (convergence == null || !convergence.ParameterBoundaryContacts.Any(contact =>
                contact.ExperimentIdentity != null && experiments.ContainsKey(contact.ExperimentIdentity))) return;
            convergence.SetParameterBoundaryContacts(convergence.ParameterBoundaryContacts.Select(contact =>
                new ParameterBoundaryContact(contact.Parameter, contact.Scope,
                    Replace(contact.ExperimentIdentity, experiments), contact.ExperimentName,
                    contact.Side, contact.FinalValue, contact.BoundValue)));
        }

        static ProfileLikelihoodRunResult RemapProfile(ProfileLikelihoodRunResult run, IReadOnlyDictionary<string, string> experiments)
        {
            if (run == null || !run.Coordinates.Any(coordinate => coordinate.Id.ExperimentIdentity != null
                && experiments.ContainsKey(coordinate.Id.ExperimentIdentity))) return run;
            return new ProfileLikelihoodRunResult(run.ConfidenceLevel, run.Calibration, run.N, run.P, run.Q, run.Df,
                run.BaselineObjective, run.TargetIncrement, run.Algorithm, run.UseWeightedFitting, run.Tolerance,
                run.CandidateIterationLimit, run.ExpansionLimit, run.RefinementLimit, run.Elapsed, run.Outcome,
                run.Coordinates.Select(coordinate => new ProfileCoordinateResult(new ProfileCoordinateId(
                    coordinate.Id.Parameter, coordinate.Id.Scope, Replace(coordinate.Id.ExperimentIdentity, experiments),
                    coordinate.Id.PrimaryOptimizerIndex), coordinate.BestValue, coordinate.LowerBound, coordinate.UpperBound,
                    coordinate.Lower, coordinate.Upper, coordinate.ShapeWarnings)), run.AttemptedSolverCalls,
                run.OptimizerToleranceSetting);
        }

        sealed class Graph
        {
            internal HashSet<ExperimentData> Experiments { get; } = new HashSet<ExperimentData>();
            internal HashSet<GlobalSolution> Globals { get; } = new HashSet<GlobalSolution>();
            internal HashSet<SolutionInterface> Solutions { get; } = new HashSet<SolutionInterface>();
            internal HashSet<NullModelComparison> Comparisons { get; } = new HashSet<NullModelComparison>();
            internal HashSet<Model> PrimaryModels { get; } = new HashSet<Model>();

            internal Graph(IEnumerable<ITCDataContainer> roots)
            {
                foreach (var experiment in roots.OfType<ExperimentData>())
                {
                    Experiments.Add(experiment);
                    Add(experiment.Solution);
                }
                foreach (var result in roots.OfType<AnalysisResult>())
                {
                    Add(result.Solution);
                    Add(result.NullComparison);
                    foreach (var member in result.MemberAssessments) Add(member.Comparison);
                }
            }

            void Add(GlobalSolution global, bool isBootstrap = false)
            {
                if (global == null || !Globals.Add(global)) return;
                foreach (var solution in global.Solutions) Add(solution, isBootstrap);
                foreach (var bootstrap in global.BootstrapSolutions) Add(bootstrap, true);
                Add(global.NullComparison, isBootstrap);
            }

            void Add(SolutionInterface solution, bool isBootstrap = false)
            {
                if (solution == null || !Solutions.Add(solution)) return;
                Experiments.Add(solution.Data);
                if (!isBootstrap) PrimaryModels.Add(solution.Model);
                foreach (var bootstrap in solution.BootstrapSolutions) Add(bootstrap, true);
                Add(solution.NullComparison, isBootstrap);
            }

            void Add(NullModelComparison comparison, bool isBootstrap = false)
            {
                if (comparison == null || !Comparisons.Add(comparison)) return;
                foreach (var solution in comparison.NullSolutions) Add(solution, isBootstrap);
            }
        }
    }
}
