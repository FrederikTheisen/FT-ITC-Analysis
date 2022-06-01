using System;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public static class DataManager
    {
        public static EnergyUnit Unit { get; set; } = EnergyUnit.Joule;

        public static AnalysisITCDataSource DataSource { get; private set; }
        public static List<ITCDataContainer> DataSourceContent => DataSource.Content;
        public static List<AnalysisResult> Results => DataSourceContent.Where(o => o is AnalysisResult).Select(o => o as AnalysisResult).ToList();
        public static List<ExperimentData> Data => DataSourceContent.Where(o => o is ExperimentData).Select(o => o as ExperimentData).ToList();
        public static IEnumerable<ExperimentData> IncludedData => Data.Where(d => d.Include);

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
                if (value >= DataSourceContent.Count) selectedContentIndex = DataSourceContent.Count - 1;
                else selectedContentIndex = value;
            }
        }

        public static event EventHandler<ExperimentData> DataDidChange;
        public static event EventHandler<ExperimentData> SelectionDidChange;
        public static event EventHandler<AnalysisResult> AnalysisResultSelected;

        public static int Count => Data.Count();

        public static bool DataIsLoaded => DataSource.Content.Exists(o => o is ExperimentData);
        public static bool AllDataIsBaselineProcessed => Data.All(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsBaselineProcessed => Data.Any(d => d.Processor.BaselineCompleted);
        public static bool AnyDataIsAnalyzed => Data.Any(d => d.Solution != null);

        public static ExperimentData Current => SelectedDataIndex == -1 || (SelectedDataIndex >= Count) ? null : Data[SelectedDataIndex];

        public static void Init()
        {
            DataSource = new AnalysisITCDataSource();

            DataDidChange.Invoke(null, null);
        }

        public static void SelectIndex(int index)
        {
            SelectedContentIndex = index;

            if (index == -1) return;
            if (DataSourceContent[index] is ExperimentData)
            {
                SelectedDataIndex = Data.IndexOf(DataSourceContent[index] as ExperimentData);

                SelectionDidChange?.Invoke(null, Current);

                StateManager.ManagedReturnToAnalysisViewState();
            }
            else
            {
                AnalysisResultSelected?.Invoke(null, DataSourceContent[index] as AnalysisResult);

                StateManager.GoToResultView();
            }
        }

        internal static void RemoveData(int index)
        {
            if (SelectedContentIndex >= index) SelectedContentIndex--;

            if (DataSourceContent[index] is ExperimentData)
            {
                int datindex = Data.IndexOf(DataSourceContent[index] as ExperimentData);

                if (datindex < SelectedDataIndex) SelectedDataIndex--;
                else if (datindex == SelectedDataIndex) { selectedDataIndex = -1; SelectionDidChange?.Invoke(null, Current); }

                DataSourceContent.RemoveAt(index);

                DataDidChange.Invoke(null, Current);
            }
            else
            {
                DataSourceContent.RemoveAt(index);
            }
        }

        public static void AddData(ITCDataContainer data)
        {
            DataSourceContent.Add(data);

            if (data is ExperimentData) { DataDidChange.Invoke(null, data as ExperimentData); SelectIndex(DataSourceContent.Count - 1); SelectionDidChange?.Invoke(null, Current); }
            else DataDidChange.Invoke(null, null);
        }

        public static void Clear() => Init();

        public static async void CopySelectedProcessToAll()
        {
            var curr = Current;

            if (curr == null) return;
            if (curr.Processor.Interpolator == null) return;

            var int_delay = curr.Injections.Select(inj => inj.IntegrationStartDelay).Average();
            var int_length = curr.Injections.Select(inj => inj.IntegrationLength).Average();

            if (curr.IntegrationLengthMode) int_length = curr.IntegrationLengthFactor;

            var count = Count - 1; //Do not count current data 

            StatusBarManager.SetStatus("Processing data...", 0);
            StatusBarManager.Progress = 0;

            foreach (var data in Data)
            {
                if (data == curr) continue;

                if (!data.Processor.IsLocked) data.SetProcessor(new DataProcessor(data, curr.Processor));
            }

            float i = 0;

            foreach (var data in Data)
            {
                if (data == curr) continue;

                i++;

                if (!data.Processor.IsLocked)
                {
                    data.Processor.WillProcessData();
                    if (!data.Processor.Interpolator.IsLocked) await data.Processor.InterpolateBaseline();
                    data.IntegrationLengthMode = curr.IntegrationLengthMode;
                    data.SetCustomIntegrationTimes(int_delay, int_length);
                    data.Processor.IntegratePeaks();
                    data.Processor.DidProcessData();
                }

                StatusBarManager.Progress = i / count;
            }

            StatusBarManager.Progress = 1;
            StatusBarManager.SetStatus("Data processed", 3000);

        }

        public static void IntegrateAllValidData()
        {
            foreach (var data in Data)
            {
                if (data.Processor.BaselineCompleted) data.Processor.IntegratePeaks();
            }
        }
    }


}
