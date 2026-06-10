using System;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses.Models;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
    public class ITCDataContainerDeletionLog
    {
        readonly List<ITCDataContainer> data = new List<ITCDataContainer>();
        readonly IReadOnlyList<ITCDataContainer> dataView;

        public IReadOnlyList<ITCDataContainer> Data => dataView;

        public ITCDataContainerDeletionLog(IEnumerable<ITCDataContainer> data)
        {
            dataView = this.data.AsReadOnly();
            if (data != null) this.data.AddRange(data.Where(item => item != null));
        }

        public ITCDataContainerDeletionLog(ITCDataContainer data)
        {
            dataView = this.data.AsReadOnly();
            if (data != null) this.data.Add(data);
        }
    }

    public enum DataClearMode
    {
        RecordUndo,
        ResetSession
    }

    public static class DataManager
    {
        public static EnergyUnit Unit { get; set; } = EnergyUnit.Joule;

        public static event EventHandler<ExperimentData> DataDidChange;
        public static event EventHandler<ExperimentData> DataInclusionDidChange;
        public static event EventHandler<ExperimentData> SelectionDidChange;
        public static event EventHandler<AnalysisResult> AnalysisResultSelected;
        public static event EventHandler<int> SourceItemRemoved;
        public static event EventHandler<SolutionInterface> ResultSolutionSelectionDidChange;
        public static event EventHandler<ExperimentData> ResultLinkedExperimentHighlightDidChange;
        public static event EventHandler UpdateTable;
        public static event EventHandler UpdateViewCells;

        public static SolutionInterface SelectedResultSolution { get; private set; }
        public static ExperimentData SelectedSolutionExperimentHighlight => SelectedResultSolution?.Data;
        public static List<ExperimentData> SelectedSolutionExperiments => SelectedResult?.Solution?.Solutions?.Select(sol => sol.Data).ToList() ?? new List<ExperimentData>();

        static readonly List<ITCDataContainer> sourceItems = new List<ITCDataContainer>();
        static readonly IReadOnlyList<ITCDataContainer> sourceItemsView = sourceItems.AsReadOnly();
        static readonly List<ITCDataContainerDeletionLog> deletedDataList = new List<ITCDataContainerDeletionLog>();
        static readonly IReadOnlyList<ITCDataContainerDeletionLog> deletedDataListView = deletedDataList.AsReadOnly();

        public static IReadOnlyList<ITCDataContainer> SourceItems => sourceItemsView;
        public static IReadOnlyList<ITCDataContainerDeletionLog> DeletedDataList => deletedDataListView;
        public static List<AnalysisResult> Results => SourceItems.Where(o => o is AnalysisResult).Select(o => o as AnalysisResult).ToList();
        public static List<ExperimentData> Data => SourceItems.Where(o => o is ExperimentData).Select(o => o as ExperimentData).ToList();
        public static IEnumerable<ExperimentData> IncludedData => Data.Where(d => d.Include).ToList();
        public static ExperimentData Current => SelectedIsData ? SourceItems[SelectedContentIndex] as ExperimentData : null;
        public static AnalysisResult SelectedResult => SelectedIsResult ? (SourceItems[selectedContentIndex] as AnalysisResult) : null;
        public static bool SelectedIsData
        {
            get
            {
                if (SelectedContentIndex == -1) return false;
                return SelectedContentIndex < SourceItems.Count && SourceItems[SelectedContentIndex] is ExperimentData;
            }
        }
        public static bool SelectedIsResult
        {
            get
            {
                if (SelectedContentIndex == -1) return false;
                return SelectedContentIndex < SourceItems.Count && SourceItems[SelectedContentIndex] is AnalysisResult;
            }
        }

        public static bool StopProcessCopying { get; set; } = false;

        static int selectedContentIndex = 0;
        public static int SelectedContentIndex
        {
            get => selectedContentIndex;
            private set
            {
                if (SourceItems.Count == 0) selectedContentIndex = -1;
                else if (value < 0) selectedContentIndex = -1;
                else if (value >= SourceItems.Count) selectedContentIndex = SourceItems.Count - 1;
                else selectedContentIndex = value;
            }
        }

        public static int Count => Data.Count();

        public static bool DataIsLoaded => SourceItems.Any(o => o is ExperimentData);
        public static bool AllDataIsBaselineProcessed => Data.All(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsBaselineProcessed => Data.Any(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsAnalyzed => Data.Any(d => d.Solution != null);

        public static void Init()
        {
            sourceItems.Clear();
            NotifySourceItemsChanged(-1);
        }

        static void ReplaceSourceItems(IEnumerable<ITCDataContainer> items)
        {
            sourceItems.Clear();
            if (items != null) sourceItems.AddRange(items);
        }

        public static int IndexOfSourceItem(ITCDataContainer item) => sourceItems.IndexOf(item);

        public static void SelectIndex(int index)
        {
            SelectedContentIndex = index;
            index = SelectedContentIndex;

            if (index == -1)
            {
                SelectionDidChange?.Invoke(null, null);
                ClearResultSolutionSelection();
                return;
            }

            if (SourceItems[index] is ExperimentData)
            {
                SelectionDidChange?.Invoke(null, Current);

                StateManager.ManagedReturnToAnalysisViewState();
            }
            else
            {
                AnalysisResultSelected?.Invoke(null, SourceItems[index] as AnalysisResult);

                StateManager.GoToResultView();
            }

            DataManager.ClearResultSolutionSelection();
        }

        public static void SelectResultSolution(SolutionInterface solution)
        {
            if (ReferenceEquals(SelectedResultSolution, solution)) return;

            SelectedResultSolution = solution;

            ResultSolutionSelectionDidChange?.Invoke(null, SelectedResultSolution);
            ResultLinkedExperimentHighlightDidChange?.Invoke(null, SelectedSolutionExperimentHighlight);
        }

        public static void ClearResultSolutionSelection()
        {
            SelectedResultSolution = null;

            ResultSolutionSelectionDidChange?.Invoke(null, null);
            ResultLinkedExperimentHighlightDidChange?.Invoke(null, null);
        }

        public static void LoadResultSolutionsToExperiments(AnalysisResult result)
        {
            if (result?.Solution?.Solutions == null) return;

            foreach (var solution in result.Solution.Solutions)
            {
                solution?.Data?.UpdateSolution(solution.Model);
            }
        }

        static string DescribeItem(ITCDataContainer item)
        {
            if (item == null) return "<null>";

            var type = item.GetType().Name;
            var name = string.IsNullOrWhiteSpace(item.Name) ? item.FileName : item.Name;
            return $"{type}[Name='{name}', ID='{item.UniqueID}']";
        }

        public static void RemoveSourceItemAt(int index)
        {
            AppEventHandler.PrintAndLog($"DataManager.RemoveSourceItemAt requested: index={index}, selectedContent={SelectedContentIndex}, totalContent={SourceItems.Count}, totalData={Count}");

            if (index < 0)
            {
                AppEventHandler.PrintAndLog($"DataManager.RemoveSourceItemAt ignored: index was {index}", 1);
                return;
            }
            if (index >= SourceItems.Count)
            {
                AppEventHandler.PrintAndLog($"DataManager.RemoveSourceItemAt ignored: index {index} is outside content range 0..{SourceItems.Count - 1}", 1);
                return;
            }

            var removedItem = SourceItems[index];
            AppEventHandler.PrintAndLog($"DataManager.RemoveSourceItemAt target: {DescribeItem(removedItem)}", 1);

            deletedDataList.Add(new ITCDataContainerDeletionLog(removedItem));

            var currentSelectedItem = SelectedContentIndex >= 0 && SelectedContentIndex < SourceItems.Count
                ? SourceItems[SelectedContentIndex]
                : null;

            AppEventHandler.PrintAndLog($"DataManager.RemoveSourceItemAt selection before remove: {DescribeItem(currentSelectedItem)}", 1);

            sourceItems.RemoveAt(index);

            var selectedIndexAfterRemove = -1;
            if (sourceItems.Count > 0)
            {
                selectedIndexAfterRemove = currentSelectedItem != null
                    ? sourceItems.IndexOf(currentSelectedItem)
                    : -1;

                if (selectedIndexAfterRemove < 0)
                {
                    selectedIndexAfterRemove = Math.Min(index, sourceItems.Count - 1);
                }
            }

            SourceItemRemoved?.Invoke(null, index);
            NotifySourceItemsChanged(selectedIndexAfterRemove);

            var selectedAfter = SelectedContentIndex >= 0 && SelectedContentIndex < SourceItems.Count
                ? SourceItems[SelectedContentIndex]
                : null;
            AppEventHandler.PrintAndLog($"DataManager.RemoveSourceItemAt completed: removed {DescribeItem(removedItem)}, selectedAfter={DescribeItem(selectedAfter)}, totalContent={SourceItems.Count}, totalData={Count}");
        }

        public static void UndoDeleteData()
        {
            AppEventHandler.PrintAndLog($"DataManager.UndoDeleteData requested: deletedBatches={deletedDataList.Count}, totalContent={SourceItems.Count}, totalData={Count}");

            if (deletedDataList.Count == 0)
            {
                AppEventHandler.PrintAndLog("DataManager.UndoDeleteData ignored: no deleted data batches", 1);
                return;
            }

            var deletionLog = deletedDataList.Last();
            var restoredData = deletionLog.Data.Where(data => data != null).ToList();
            AppEventHandler.PrintAndLog($"DataManager.UndoDeleteData restoring batch of {restoredData.Count}: {string.Join(", ", restoredData.Select(DescribeItem))}", 1);

            if (restoredData.Count == 0)
            {
                deletedDataList.Remove(deletionLog);
                AppEventHandler.PrintAndLog("DataManager.UndoDeleteData ignored empty deleted data batch", 1);
                return;
            }

            var selectedItem = SelectedContentIndex >= 0 && SelectedContentIndex < SourceItems.Count
                ? SourceItems[SelectedContentIndex]
                : null;

            foreach (var data in restoredData)
            {
                sourceItems.Add(data);
                if (ShouldSelectAddedItem(data))
                {
                    selectedItem = data;
                }
            }

            deletedDataList.Remove(deletionLog);

            NotifySourceItemsChanged(selectedItem, restoredData.Count == 1 ? restoredData[0] as ExperimentData : null);

            var selectedAfter = SelectedContentIndex >= 0 && SelectedContentIndex < SourceItems.Count
                ? SourceItems[SelectedContentIndex]
                : null;
            AppEventHandler.PrintAndLog($"DataManager.UndoDeleteData completed: totalContent={SourceItems.Count}, totalData={Count}, selectedAfter={DescribeItem(selectedAfter)}");
        }

        public static void AddData(ITCDataContainer data)
        {
            if (data == null) return;

            AppEventHandler.PrintAndLog($"DataManager.AddData requested: item={DescribeItem(data)}, totalContentBefore={SourceItems.Count}, totalDataBefore={Count}, selectedContentIndex={SelectedContentIndex}");

            sourceItems.Add(data);

            var selectedIndexAfterAdd = SourceItems.Count - 1;

            if (data is ExperimentData experimentData)
            {
                NotifySourceItemsChanged(selectedIndexAfterAdd, experimentData);
            }
            else
            {
                if (!ShouldSelectAddedItem(data))
                {
                    DataDidChange?.Invoke(null, null);
                }
                else if (data is AnalysisResult)
                {
                    DataDidChange?.Invoke(null, null);
                    SelectIndex(selectedIndexAfterAdd);
                }
                else
                {
                    NotifySourceItemsChanged(selectedIndexAfterAdd);
                }
            }

            var selectedAfter = SelectedContentIndex >= 0 && SelectedContentIndex < SourceItems.Count
                ? SourceItems[SelectedContentIndex]
                : null;
            AppEventHandler.PrintAndLog($"DataManager.AddData completed: item={DescribeItem(data)}, totalContentAfter={SourceItems.Count}, totalDataAfter={Count}, selectedAfter={DescribeItem(selectedAfter)}");
        }

        public static void AddData(IEnumerable<ITCDataContainer> data)
        {
            var items = data?.Where(item => item != null).ToList() ?? new List<ITCDataContainer>();
            if (items.Count == 0) return;

            AppEventHandler.PrintAndLog($"DataManager.AddData batch requested: items={items.Count}, totalContentBefore={SourceItems.Count}, totalDataBefore={Count}, selectedContentIndex={SelectedContentIndex}");

            sourceItems.AddRange(items);

            NotifySourceItemsChanged(SourceItems.Count - 1);

            var selectedAfter = SelectedContentIndex >= 0 && SelectedContentIndex < SourceItems.Count
                ? SourceItems[SelectedContentIndex]
                : null;
            AppEventHandler.PrintAndLog($"DataManager.AddData batch completed: items={items.Count}, totalContentAfter={SourceItems.Count}, totalDataAfter={Count}, selectedAfter={DescribeItem(selectedAfter)}");
        }

        static bool ShouldSelectAddedItem(ITCDataContainer data)
        {
            return data is not AnalysisResult || AppSettings.AutoOpenNewAnalysisResult;
        }

        public static void ApplyOptions()
        {
            foreach (ExperimentData exp in Data)
            {
                var atts = exp.Attributes;

                if (atts.Exists(a => a.Key == AttributeKey.BufferSubtraction))
                {
                    var att = atts.Find(a => a.Key == AttributeKey.BufferSubtraction);
                    var settings = BufferSubtractionSettings.FromAttribute(att);
                    var refexp = Data.Exists(d => d.UniqueID == settings?.ReferenceExperimentId) ? Data.Find(d => d.UniqueID == settings.ReferenceExperimentId) : null;

                    if (refexp != null)
                        exp.SetBufferSubtraction(refexp as ExperimentData, settings.Method);
                }
            }
        }

        public static void DuplicateSelectedData(ExperimentData data)
        {
            AppEventHandler.PrintAndLog("DataManager.Duplicating Data: " + data.FileName);
            StatusBarManager.SetStatus($"Duplicating Data: {data.Name}", 3000);

            var dps = new List<DataPoint>();
            foreach (var dp in data.DataPoints) dps.Add(dp.Copy());

            var newdata = new ExperimentData(data.FileName)
            {
                Name = data.Name,
                Instrument = data.Instrument,
                DataSourceFormat = data.DataSourceFormat,
                DataPoints = dps.ToList(),
                SyringeConcentration = data.SyringeConcentration,
                CellConcentration = data.CellConcentration,
                CellVolume = data.CellVolume,
                StirringSpeed = data.StirringSpeed,
                FeedBackMode = data.FeedBackMode,
                TargetTemperature = data.TargetTemperature,
                InitialDelay = data.InitialDelay,
                TargetPowerDiff = data.TargetPowerDiff,
                MeasuredTemperature = data.MeasuredTemperature,
                Date = data.Date,
            };
            newdata.IterateCopyName();

            var injs = new List<InjectionData>();
            foreach (var inj in data.Injections)
                injs.Add(inj.Copy(newdata));

            newdata.Injections = injs;

            foreach (var att in data.Attributes)
                newdata.Attributes.Add(att.Copy());

            if (data.Segments != null)
                foreach (var seg in data.Segments) newdata.AddSegment(seg);

            DataReaders.RawDataReader.ProcessInjections(newdata);

            if (data.BaseLineCorrectedDataPoints != null)
                newdata.BaseLineCorrectedDataPoints = data.BaseLineCorrectedDataPoints.Select(dp => dp.Copy()).ToList();

            newdata.SetProcessor(new DataProcessor(newdata, data.Processor));

            AddData(newdata);
        }

        public static void SortContent(SortMode mode)
        {
            AppEventHandler.PrintAndLog("Sorting content by " + mode.ToString() + "...");
            StatusBarManager.SetStatus($"Sorting Content by {mode}", 3333);

            var selectedItem = SelectedContentIndex >= 0 && SelectedContentIndex < SourceItems.Count
                ? SourceItems[SelectedContentIndex]
                : null;

            switch (mode)
            {
                case SortMode.Name:
                    ReplaceSourceItems(SourceItems.OrderBy(OrderOnType).ThenBy(o => o.Name).ToList());
                    break;
                case SortMode.Temperature:
                    ReplaceSourceItems(SourceItems.OrderBy(OrderOnTemperature).ToList());
                    break;
                case SortMode.Type:
                    ReplaceSourceItems(SourceItems.OrderBy(OrderOnType).ToList());
                    break;
                case SortMode.IonicStrength:
                    ReplaceSourceItems(SourceItems.OrderBy(OrderOnIonicStrength).ToList());
                    break;
                case SortMode.ProtonationEnthalpy:
                    ReplaceSourceItems(SourceItems.OrderBy(OrderOnIonicProtonationEnthalpy).ToList());
                    break;
                case SortMode.Date:
                    ReplaceSourceItems(SourceItems.OrderBy(OrderOnType).ThenBy(o => o.Date).ToList());
                    break;
            }

            NotifySourceItemsChanged(selectedItem);

            AppEventHandler.PrintAndLog("Sort completed");
        }

        public static void SetAllIncludeState(bool includeall)
        {
            AppEventHandler.PrintAndLog("Change IncludeState: " + includeall.ToString());

            foreach (var data in SourceItems.OfType<ExperimentData>())
            {
                data.Include = includeall;
            }

            InvokeDataInclusionDidChange();
        }

        static void NotifySourceItemsChanged(ITCDataContainer selectedItem)
        {
            NotifySourceItemsChanged(selectedItem, null);
        }

        static void NotifySourceItemsChanged(ITCDataContainer selectedItem, ExperimentData changedData)
        {
            RestoreSelectedItem(selectedItem);
            DataDidChange?.Invoke(null, changedData);
            SelectIndex(SelectedContentIndex);
        }

        static void NotifySourceItemsChanged(int selectedIndex, ExperimentData changedData = null)
        {
            SelectedContentIndex = selectedIndex;
            DataDidChange?.Invoke(null, changedData);
            SelectIndex(SelectedContentIndex);
        }

        static void RestoreSelectedItem(ITCDataContainer item)
        {
            if (item == null)
            {
                SelectedContentIndex = -1;
                return;
            }

            SelectedContentIndex = sourceItems.IndexOf(item);
        }

        static double OrderOnTemperature(ITCDataContainer item)
        {
            if (item is ExperimentData data) return data.MeasuredTemperature;
            return double.MaxValue;
        }

        static int OrderOnType(ITCDataContainer item)
        {
            if (item is ExperimentData) return 0;
            if (item is AnalysisResult) return 1;
            return 2;
        }

        static double OrderOnIonicStrength(ITCDataContainer item)
        {
            if (item is ExperimentData data) return BufferAttribute.GetIonicStrength(data);
            return double.MaxValue;
        }

        static double OrderOnIonicProtonationEnthalpy(ITCDataContainer item)
        {
            if (item is ExperimentData data) return BufferAttribute.GetProtonationEnthalpy(data);
            return double.MaxValue;
        }

        public static void InvokeDataDidChange()
        {
            DataDidChange?.Invoke(null, null);
        }

        public static void InvokeDataInclusionDidChange()
        {
            DataInclusionDidChange?.Invoke(null, null);
            InvokeUpdateDataViewCells();
        }

        public static void InvokeUpdateDataViewCells()
        {
            UpdateViewCells?.Invoke(null, null);
        }

        public static void InvokeUpdateTable()
        {
            UpdateTable?.Invoke(null, null);
        }

        public static void Clear(DataClearMode mode = DataClearMode.RecordUndo)
        {
            if (mode == DataClearMode.ResetSession)
            {
                deletedDataList.Clear();
            }
            else if (sourceItems.Count > 0)
            {
                deletedDataList.Add(new(sourceItems));
            }

            Init();

            FTITCFormat.CurrentAccessedAppDocumentPath = ""; //Remove save file path
        }

        public static void ClearProcessing()
        {
            var removedResults = sourceItems.Where(data => data is AnalysisResult).ToList();
            if (removedResults.Count == 0) return;

            var previousSelectedIndex = SelectedContentIndex;
            var previousSelectedItem = previousSelectedIndex >= 0 && previousSelectedIndex < SourceItems.Count
                ? SourceItems[previousSelectedIndex]
                : null;

            deletedDataList.Add(new(removedResults));
            sourceItems.RemoveAll(data => data is AnalysisResult);

            var selectedIndexAfterRemove = previousSelectedItem != null && sourceItems.Contains(previousSelectedItem)
                ? sourceItems.IndexOf(previousSelectedItem)
                : Math.Min(previousSelectedIndex, sourceItems.Count - 1);

            NotifySourceItemsChanged(selectedIndexAfterRemove);
        }

        public static void CopySelectedAttributesToActive(bool clear = false)
        {
            if (Current == null) return;

            var opt = Current.Attributes;
            var target = Data.Where(d => d != Current && d.Include).ToList();

            var copied = CopyAttributesTo(opt, target, clear);
            StatusBarManager.SetStatus(
                copied > 0
                    ? $"Copied attributes to {copied} active experiment{(copied == 1 ? "" : "s")}"
                    : "No active experiments available for attribute copy",
                4000);

            return;
        }

        public static void CopySelectedAttributesToAll(bool clear = false)
        {
            if (Current == null) return;

            var opt = Current.Attributes;
            var target = Data.Where(d => d != Current).ToList();

            var copied = CopyAttributesTo(opt, target, clear);
            StatusBarManager.SetStatus(
                copied > 0
                    ? $"Copied attributes to {copied} experiment{(copied == 1 ? "" : "s")}"
                    : "No other experiments available for attribute copy",
                4000);

            return;
        }

        public static void CopySelectedAttributeToAll(ExperimentAttribute attribute)
        {
            if (Current == null || attribute == null || !Current.Attributes.Contains(attribute)) return;

            var target = Data.Where(d => d != Current).ToList();
            var copied = CopyAttributesTo(new List<ExperimentAttribute> { attribute }, target);
            var attributeName = attribute.GetDisplayName();

            StatusBarManager.SetStatus(
                copied > 0
                    ? $"Copied {attributeName} to {copied} experiment{(copied == 1 ? "" : "s")}"
                    : $"No other experiments available for {attributeName} copy",
                4000);
        }

        public static void CopySelectedAttributeToActive(ExperimentAttribute attribute)
        {
            if (Current == null || attribute == null || !Current.Attributes.Contains(attribute)) return;

            var target = Data.Where(d => d != Current && d.Include).ToList();
            var copied = CopyAttributesTo(new List<ExperimentAttribute> { attribute }, target);
            var attributeName = attribute.GetDisplayName();

            StatusBarManager.SetStatus(
                copied > 0
                    ? $"Copied {attributeName} to {copied} active experiment{(copied == 1 ? "" : "s")}"
                    : $"No active experiments available for {attributeName} copy",
                4000);
        }

        public static void CopySelectedAttributesToExperiment(ExperimentData experiment, bool clear = false)
        {
            if (Current == null || experiment == null || experiment == Current) return;

            var opt = Current.Attributes;
            var copied = CopyAttributesTo(opt, new List<ExperimentData> { experiment }, clear);
            StatusBarManager.SetStatus(
                copied > 0
                    ? $"Copied attributes to {experiment.Name}"
                    : $"No attributes copied to {experiment.Name}",
                4000);
        }

        public static void CopySelectedAttributeToExperiment(ExperimentAttribute attribute, ExperimentData experiment)
        {
            if (Current == null || attribute == null || !Current.Attributes.Contains(attribute)) return;
            if (experiment == null || experiment == Current) return;

            var copied = CopyAttributesTo(new List<ExperimentAttribute> { attribute }, new List<ExperimentData> { experiment });
            var attributeName = attribute.GetDisplayName();

            StatusBarManager.SetStatus(
                copied > 0
                    ? $"Copied {attributeName} to {experiment.Name}"
                    : $"No {attributeName} copied to {experiment.Name}",
                4000);
        }

        public static void CopySelectedAttributesToNameToken(string token, bool clear = false)
        {
            if (Current == null) return;

            token = token?.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(token)) return;

            var opt = Current.Attributes;
            var target = Data.Where(d => d != Current && d.Name.ToLower().Contains(token)).ToList();

            var copied = CopyAttributesTo(opt, target, clear);
            StatusBarManager.SetStatus(
                copied > 0
                    ? $"Copied attributes to {copied} experiment{(copied == 1 ? "" : "s")} matching \"{token}\""
                    : $"No experiments matched \"{token}\"",
                4000);
        }

        static int CopyAttributesTo(List<ExperimentAttribute> attributes, List<ExperimentData> target, bool clear = false)
        {
            AppEventHandler.PrintAndLog($"Copying Attributes ({attributes.Count}) To Target ({target.Count})...");
            AppEventHandler.PrintAndLog($"Clear Existing: {clear}", 1);

            bool overwrite = false;
            int copiedExperiments = 0;

            if (target.Any(exp => exp.Attributes.Any(att => attributes.Exists(att2 => att2.Key == att.Key))))
            {
                overwrite = AppDelegate.PromptOverwrite("Overwrite existing attributes?");
            }

            AppEventHandler.PrintAndLog($"Overwrite Existing: {overwrite}", 1);

            foreach (var exp in target)
            {
                if (!exp.CopyAttributesFrom(attributes, clear, overwrite, notify: false)) continue;

                copiedExperiments++;

                foreach (var att in attributes)
                {
                    AppEventHandler.PrintAndLog($"Adding {att.GetDisplayName()}", 1);
                }
            }

            if (copiedExperiments > 0)
            {
                InvokeDataDidChange();
            }

            return copiedExperiments;
        }

        public static void CopySelectedProcessToAll()
        {
            var current = Current;
            var target = Data.ToList();

            if (current == null) return;
            if (current.Processor == null) return;
            if (current.Processor.Interpolator == null) return;

            StatusBarManager.SetStatus("Copying to all...", 0);

            CopySelectedProcessToSelection(current, target);
        }

        public static void CopySelectedProcessToActive()
        {
            var current = Current;
            var target = IncludedData.ToList();

            if (current == null) return;
            if (current.Processor == null) return;
            if (current.Processor.Interpolator == null) return;

            StatusBarManager.SetStatus("Copying to active...", 0);

            CopySelectedProcessToSelection(current, target);
        }

        public static void CopySelectedProcessToNonProcessed()
        {
            var current = Current;
            var target = Data.Where(d => d.Processor == null || d.Processor.Interpolator == null).ToList();

            if (current == null) return;
            if (current.Processor == null) return;
            if (current.Processor.Interpolator == null) return;

            StatusBarManager.SetStatus("Copying to non-processed...", 0);

            CopySelectedProcessToSelection(current, target);
        }

        public static async void CopySelectedProcessToSelection(ExperimentData current, List<ExperimentData> target)
        {
            StopProcessCopying = false;

            var int_delay = current.Injections.Select(inj => inj.IntegrationStartDelay).ToArray();
            var int_length = current.Injections.Select(inj => inj.IntegrationEndOffset).ToArray();
            var int_factor = current.Processor.IntegrationLengthFactor;
            var int_mode = current.Processor.IntegrationLengthMode;

            StatusBarManager.SetProgress(0);

            //Prepare the baseline interpolator
            foreach (var data in target)
            {
                if (data == current) continue;

                if (data.Processor.Interpolator == null || !data.Processor.IsLocked && !data.Processor.Interpolator.IsLocked)
                    data.SetProcessor(new DataProcessor(data, current.Processor));
            }

            float i = 0;

            foreach (var data in target)
            {
                if (data == current || StopProcessCopying) continue;

                i++;

                if (!data.Processor.IsLocked)
                {
                    // Make an initial baseline because we need something
                    data.Processor.WillProcessData();
                    if (!data.Processor.Interpolator.IsLocked) await data.Processor.InterpolateBaseline();

                    data.SetIntegrationStartTimes(int_delay);

                    data.Processor.IntegrationLengthMode = int_mode;
                    if (int_mode == InjectionData.IntegrationLengthMode.Factor) data.SetIntegrationLengthByFactor(int_factor);
                    else if (int_mode == InjectionData.IntegrationLengthMode.Fit) data.FitIntegrationPeaks();
                    else data.SetIntegrationLengthByTimes(int_length);

                    // Reprocesses baseline with new integration regions
                    data.Processor.WillProcessData();
                    if (!data.Processor.Interpolator.IsLocked) await data.Processor.InterpolateBaseline();
                    data.Processor.IntegratePeaks();
                    data.Processor.DidProcessData();
                }

                StatusBarManager.SetProgress(i / (Count - 1));
            }

            StatusBarManager.SetProgress(1);
            StatusBarManager.ClearAppStatus();
            StatusBarManager.SetStatus("Data processed", 3000);
        }

        public static void IntegrateAllValidData()
        {
            foreach (var data in Data)
            {
                if (data.Processor.BaselineCompleted) data.Processor.IntegratePeaks();
            }
        }

        public enum SortMode
        {
            Name,
            Temperature,
            Type,
            IonicStrength,
            ProtonationEnthalpy,
            Date
        }
    }
}
