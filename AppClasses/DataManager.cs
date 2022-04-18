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
        public static List<ITCDataViewContainer> DataSourceContent => DataSource.Content;
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
                else if (value > Count) selectedDataIndex = Count - 1;
                else selectedDataIndex = value;
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
            if (index == -1)
            {
                selectedDataIndex = -1;

                SelectionDidChange?.Invoke(null, Current);
            }
            else if (DataSourceContent[index] is ExperimentData)
            {
                SelectedDataIndex = Data.IndexOf(DataSourceContent[index] as ExperimentData);

                SelectionDidChange?.Invoke(null, Current);
            }
            else AnalysisResultSelected?.Invoke(null, DataSourceContent[index] as AnalysisResult);

            //SelectedIndex = index;

                //if Content[index] is ExperimentData then SelectedIndex = index; SelectionChanged.Invoke();
                //else dataviewcicked => open data view sheet

                //Selected index refers only to data rows
                //ExperimentData selection changed event and Analysis result was selected event
                //Selected ED index only changed if clicked index is ED

        }

        internal static void RemoveData(int index)
        {
            if (DataSourceContent[index] is ExperimentData)
            {
                int datindex = Data.IndexOf(DataSourceContent[index] as ExperimentData);

                if (datindex < SelectedDataIndex) SelectedDataIndex--;

                DataSourceContent.RemoveAt(index);

                DataDidChange.Invoke(null, Current);
            }
            else
            {
                DataSourceContent.RemoveAt(index);
            }

            //if Content[index] is ExperimentData then change SelectedIndex (data) to match expected, index should still be same data unless selected data was removed
            //If analysis result is removed, then do nothing else

            //DataSourceContent.RemoveAt(index);

            //DataDidChange.Invoke(null, Current);
        }

        public static void AddData(ExperimentData data)
        {
            DataSourceContent.Add(data);

            DataDidChange.Invoke(null, data);
        }

        public static void Clear() => Init();

        public static async void CopySelectedProcessToAll()
        {
            var curr = Current;

            if (curr == null) return;
            if (curr.Processor.Interpolator == null) return;

            var int_delay = curr.Injections.Select(inj => inj.IntegrationStartDelay).Average();
            var int_length = curr.Injections.Select(inj => inj.IntegrationLength).Average();

            if (curr.UseIntegrationFactorLength) int_length = curr.IntegrationLengthFactor;

            var count = Count - 1; //Do not count current data 

            StatusBarManager.SetStatus("Processing data...", 0);
            StatusBarManager.Progress = 0;

            foreach (var data in Data)
            {
                if (data == curr) continue;

                data.SetProcessor(new DataProcessor(data, curr.Processor));
            }

            float i = 0;

            foreach (var data in Data)
            {
                if (data == curr) continue;

                i++;

                data.Processor.WillProcessData();
                await data.Processor.InterpolateBaseline();
                data.UseIntegrationFactorLength = curr.UseIntegrationFactorLength;
                data.SetCustomIntegrationTimes(int_delay, int_length);
                data.Processor.IntegratePeaks();
                data.Processor.DidProcessData();

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
