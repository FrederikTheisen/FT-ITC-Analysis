using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC
{
    public static class DocumentDirtyTracker
    {
        private static readonly HashSet<ITCDataContainer> ObservedContainers = new();

        private static bool initialized;
        private static bool isDirty;
        private static int suspendCount;

        public static event EventHandler DirtyStateChanged;

        public static bool IsDirty => isDirty;
        public static bool IsSuspended => suspendCount > 0;

        public static void Initialize()
        {
            if (initialized) return;

            initialized = true;

            DataManager.DataDidChange += OnSourceItemsChanged;
            DataManager.DataInclusionDidChange += OnDocumentContentChanged;
            DataManager.RemoveListIndices += OnSourceItemsRemoved;

            ResubscribeContainers();
        }

        public static IDisposable Suspend()
        {
            suspendCount++;
            return new Scope(() =>
            {
                suspendCount = Math.Max(0, suspendCount - 1);
                ResubscribeContainers();
            });
        }

        public static void MarkDirty()
        {
            if (IsSuspended) return;
            SetDirty(true);
        }

        public static void MarkClean()
        {
            foreach (var container in DataManager.SourceItems ?? Enumerable.Empty<ITCDataContainer>())
            {
                container.MarkClean();
            }

            SetDirty(false);
        }

        static void OnSourceItemsChanged(object sender, ExperimentData e)
        {
            ResubscribeContainers();
            MarkDirty();
        }

        static void OnSourceItemsRemoved(object sender, int[] e)
        {
            ResubscribeContainers();
            MarkDirty();
        }

        static void OnDocumentContentChanged(object sender, ExperimentData e)
        {
            MarkDirty();
        }

        static void OnContainerModifiedChanged(object sender, EventArgs e)
        {
            if (IsSuspended) return;

            if (sender is ITCDataContainer container && container.IsModified)
            {
                SetDirty(true);
            }
        }

        static void ResubscribeContainers()
        {
            foreach (var container in ObservedContainers)
            {
                container.ModifiedChanged -= OnContainerModifiedChanged;
            }

            ObservedContainers.Clear();

            foreach (var container in DataManager.SourceItems ?? Enumerable.Empty<ITCDataContainer>())
            {
                if (container == null) continue;

                ObservedContainers.Add(container);
                container.ModifiedChanged += OnContainerModifiedChanged;
            }
        }

        static void SetDirty(bool value)
        {
            if (isDirty == value) return;

            isDirty = value;
            DirtyStateChanged?.Invoke(null, EventArgs.Empty);
        }

        sealed class Scope : IDisposable
        {
            readonly Action onDispose;

            public Scope(Action onDispose)
            {
                this.onDispose = onDispose;
            }

            public void Dispose()
            {
                onDispose?.Invoke();
            }
        }
    }
}
