using System;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public static class DataManager
    {
        public static EnergyUnit Unit { get; set; } = EnergyUnit.Joule;

        public static event EventHandler<ExperimentData> DataDidChange;
        public static event EventHandler<ExperimentData> SelectionDidChange;
        public static event EventHandler<AnalysisResult> AnalysisResultSelected;
        public static event EventHandler<int[]> RemoveListIndices;

        public static AnalysisITCDataSource Source { get; private set; }
        public static List<ITCDataContainer> SourceItems => Source.Content;
        public static List<AnalysisResult> Results => SourceItems.Where(o => o is AnalysisResult).Select(o => o as AnalysisResult).ToList();
        public static List<ExperimentData> Data => SourceItems.Where(o => o is ExperimentData).Select(o => o as ExperimentData).ToList();
        public static IEnumerable<ExperimentData> IncludedData => Data.Where(d => d.Include);
        public static ExperimentData Current => SelectedDataIndex == -1 || (SelectedDataIndex >= Count) ? null : Data[SelectedDataIndex];
        public static bool SelectedIsData
        {
            get
            {
                if (SelectedContentIndex == -1) return false;
                return SelectedContentIndex < SourceItems.Count && SourceItems[SelectedContentIndex] is ExperimentData;
            }
        }

        public static List<ITCDataContainerDeletionLog> DeletedDataList { get; } = new List<ITCDataContainerDeletionLog>();

        public static bool StopProcessCopying { get; set; } = false;

        static int selectedDataIndex = 0;
        public static int SelectedDataIndex
        {
            get => selectedDataIndex;
            set
            {
                if (value < 0) selectedDataIndex = 0;
                else if (value >= Count) selectedDataIndex = Count - 1;
                else selectedDataIndex = value;
            }
        }
        static int selectedContentIndex = 0;
        public static int SelectedContentIndex
        {
            get => selectedContentIndex;
            set
            {
                if (value >= SourceItems.Count) selectedContentIndex = SourceItems.Count - 1;
                else selectedContentIndex = value;
            }
        }

        public static int Count => Data.Count();

        public static bool DataIsLoaded => Source.Content.Exists(o => o is ExperimentData);
        public static bool AllDataIsBaselineProcessed => Data.All(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsBaselineProcessed => Data.Any(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsAnalyzed => Data.Any(d => d.Solution != null);

        public static void Init()
        {
            Source = new AnalysisITCDataSource();

            DataDidChange.Invoke(null, null);
        }

        public static void SelectIndex(int index)
        {
            SelectedContentIndex = index;

            if (index == -1) return;
            if (SourceItems[index] is ExperimentData)
            {
                SelectedDataIndex = Data.IndexOf(SourceItems[index] as ExperimentData);

                SelectionDidChange?.Invoke(null, Current);

                StateManager.ManagedReturnToAnalysisViewState();
            }
            else
            {
                AnalysisResultSelected?.Invoke(null, SourceItems[index] as AnalysisResult);

                StateManager.GoToResultView();
            }
        }

        public static void RemoveData2(int index)
        {
            if (index == -1) return;
            if (index >= Source.Content.Count) return;

            DeletedDataList.Add(new ITCDataContainerDeletionLog(Source.Content[index]));

            if (SelectedContentIndex == -1) { Source.Content.RemoveAt(index); return; }
            else
            {
                var current_selected_item = Source.Content[SelectedContentIndex];
                var will_delete_selected = index == SelectedContentIndex;

                Source.Content.RemoveAt(index);

                if (will_delete_selected) DataDidChange.Invoke(null, null);
                SelectIndex(Source.Content.IndexOf(current_selected_item));
            }
        }

        public static void UndoDeleteData()
        {
            var restoreddata = DeletedDataList.Last().Data;

            foreach (var data in restoreddata) AddData(data);
            DeletedDataList.Remove(DeletedDataList.Last());
        }

        public static void AddData(ITCDataContainer data)
        {
            AppEventHandler.PrintAndLog("Adding Data: " + data.FileName);

            SourceItems.Add(data);

            if (data is ExperimentData) { DataDidChange.Invoke(null, data as ExperimentData); SelectIndex(SourceItems.Count - 1); SelectionDidChange?.Invoke(null, Current); }
            else DataDidChange.Invoke(null, null);
        }

        public static void DuplicateSelectedData(ExperimentData data)
        {
            AppEventHandler.PrintAndLog("Duplicating Data: " + data.FileName);

            var dps = new List<DataPoint>();
            foreach (var dp in data.DataPoints) dps.Add(dp.Copy());

            var newdata = new ExperimentData(data.FileName + " [COPY]")
            {
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
                Date = DateTime.Now,
            };

            var injs = new List<InjectionData>();
            foreach (var inj in data.Injections) injs.Add(inj.Copy(newdata));
            newdata.Injections = injs;

            foreach (var att in data.Attributes) newdata.Attributes.Add(att.Copy());

            if (data.Segments != null)
                foreach (var seg in data.Segments) newdata.AddSegment(seg);

            DataReaders.RawDataReader.ProcessInjections(newdata);

            AddData(newdata);
        }

        public static void SortContent(SortMode mode)
        {
            AppEventHandler.PrintAndLog("Sorting content by " + mode.ToString() + "...");

            switch (mode)
            {
                case SortMode.Name: Source.SortByName(); break;
                case SortMode.Temperature: Source.SortByTemperature(); break;
                case SortMode.Type: Source.SortByType(); break;
                case SortMode.IonicStrength: Source.SortByIonicStrength(); break;
                case SortMode.ProtonationEnthalpy: Source.SortByProtonationEnthalpy(); break;
                case SortMode.Date: Source.SortByDate(); break;
            }

            AppEventHandler.PrintAndLog("Sort completed");
        }

        public static void SetAllIncludeState(bool includeall)
        {
            AppEventHandler.PrintAndLog("Change IncludeState: " + includeall.ToString());

            Source.SetAllIncludeState(includeall);
        }

        public static void InvokeDataDidChange()
        {
            DataDidChange?.Invoke(null, null);
        }

        public static void Clear()
        {
            DeletedDataList.Add(new(SourceItems));

            Init();

            DataDidChange?.Invoke(null, null);

            FTITCFormat.CurrentAccessedAppDocumentPath = ""; //Remove save file path
        }

        public static void ClearProcessing()
        {
            DeletedDataList.Add(new(SourceItems.Where(data => data is AnalysisResult).ToList()));
            var idxs = Source.Content
                .Select((item, index) => new { Item = item, Index = index })
                .Where(x => x.Item is AnalysisResult)
                .Select(x => x.Index)
                .ToArray();
            Source.Content.RemoveAll(data => data is AnalysisResult);

            RemoveListIndices?.Invoke(null, idxs);
        }

        public static void CopySelectedAttributesToAll()
        {
            if (Current == null) return;

            var opt = Current.Attributes;
            bool clear = false;

            if (Data.Where(d => d != Current).Any(exp => exp.Attributes.Count > 0))
            {
                clear = AppDelegate.PromptOverwrite("Remove and overwrite existing attributes?");
            }

            foreach (var exp in Data.Where(d => d != Current))
            {
                if (clear) exp.Attributes.Clear();

                foreach (var att in Current.Attributes)
                {
                    if (!clear && exp.Attributes.Exists(mo => mo.Key == att.Key)) continue;

                    exp.Attributes.Add(att.Copy());
                }
            }
        }

        public static async void CopySelectedProcessToAll()
        {
            StopProcessCopying = false;

            if (Current == null) return;
            if (Current.Processor == null) return;
            if (Current.Processor.Interpolator == null) return;

            var int_delay = Current.Injections.Select(inj => inj.IntegrationStartDelay).ToArray();
            var int_length = Current.Injections.Select(inj => inj.IntegrationLength).ToArray();
            var int_factor = Current.Processor.IntegrationLengthFactor;
            var int_mode = Current.Processor.IntegrationLengthMode;

            StatusBarManager.SetStatus("Processing data...", 0);
            StatusBarManager.SetProgress(0);

            //Prepare the baseline interpolator
            foreach (var data in Data)
            {
                if (data == Current) continue;

                if (data.Processor.Interpolator == null || !data.Processor.IsLocked && !data.Processor.Interpolator.IsLocked)
                    data.SetProcessor(new DataProcessor(data, Current.Processor));
            }

            float i = 0;

            foreach (var data in Data)
            {
                if (data == Current || StopProcessCopying) continue;

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
