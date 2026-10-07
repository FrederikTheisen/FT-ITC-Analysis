using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Application
{
    internal readonly struct DocumentSaveStamp
    {
        public Guid DocumentId { get; }
        public long Revision { get; }

        public DocumentSaveStamp(Guid documentId, long revision)
        {
            DocumentId = documentId;
            Revision = revision;
        }
    }

    public static class DocumentDirtyTracker
    {
        private static readonly HashSet<ITCDataContainer> ObservedContainers = new();

        private static bool initialized;
        private static bool isDirty;
        private static int suspendCount;
        private static int restoreDocumentCount;
        private static readonly object documentStateLock = new();
        private static Guid documentId = Guid.NewGuid();
        private static long revision;
        private static Guid? pendingDirtyDocumentId;

        public static event EventHandler DirtyStateChanged;

        public static bool IsDirty => isDirty;
        public static bool IsSuspended => suspendCount > 0;
        public static bool IsRestoringDocument => restoreDocumentCount > 0;

        internal static DocumentSaveStamp CaptureSaveStamp()
        {
            lock (documentStateLock) return new(documentId, revision);
        }

        internal static bool IsCurrentDocument(DocumentSaveStamp stamp)
        {
            lock (documentStateLock) return stamp.DocumentId == documentId;
        }

        internal static bool MatchesSaveStamp(DocumentSaveStamp stamp)
        {
            lock (documentStateLock) return stamp.DocumentId == documentId && stamp.Revision == revision;
        }

        internal static bool TryMarkClean(DocumentSaveStamp stamp)
        {
            lock (documentStateLock)
            {
                if (stamp.DocumentId != documentId) return false;
                if (stamp.Revision != revision)
                {
                    if (IsSuspended)
                        pendingDirtyDocumentId = stamp.DocumentId;
                    else
                        SetDirty(true);
                    return false;
                }
                if (IsSuspended) return false;
                MarkClean();
                return true;
            }
        }

        internal static void BeginDocument()
        {
            lock (documentStateLock)
            {
                documentId = Guid.NewGuid();
                pendingDirtyDocumentId = null;
            }
        }

        public static void Initialize()
        {
            if (initialized) return;

            initialized = true;

            DataManager.DataDidChange += OnSourceItemsChanged;
            DataManager.DataInclusionDidChange += OnDocumentContentChanged;
            DataManager.ReportsDidChange += OnReportsChanged;

            ResubscribeContainers();
        }

        public static IDisposable Suspend()
        {
            suspendCount++;
            return new Scope(() =>
            {
                suspendCount = Math.Max(0, suspendCount - 1);
                ApplyPendingDirtyIfReady();
                ResubscribeContainers();
            });
        }

        public static IDisposable RestoreDocument()
        {
            suspendCount++;
            restoreDocumentCount++;

            return new Scope(() =>
            {
                restoreDocumentCount = Math.Max(0, restoreDocumentCount - 1);
                suspendCount = Math.Max(0, suspendCount - 1);
                ApplyPendingDirtyIfReady();
                ResubscribeContainers();
            });
        }

        public static void MarkDirty()
        {
            AdvanceRevision();
            if (IsSuspended) return;
            SetDirty(true);
        }

        public static void MarkClean()
        {
            foreach (var container in DataManager.SourceItems ?? Enumerable.Empty<ITCDataContainer>())
            {
                container.MarkClean();
            }
            foreach (var report in DataManager.Reports ?? Enumerable.Empty<AnalysisReport>()) report.MarkClean();

            SetDirty(false);
        }

        static void OnSourceItemsChanged(object sender, ExperimentData e)
        {
            ResubscribeContainers();
            MarkDirty();
        }

        static void OnDocumentContentChanged(object sender, ExperimentData e)
        {
            MarkDirty();
        }

        static void OnReportsChanged(object sender, EventArgs e)
        {
            ResubscribeContainers();
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

        static void OnContainerContentChanged(object sender, EventArgs e) => AdvanceRevision();

        static void AdvanceRevision()
        {
            if (IsRestoringDocument) return;
            lock (documentStateLock) revision++;
        }

        static void ApplyPendingDirtyIfReady()
        {
            lock (documentStateLock)
            {
                if (IsSuspended || !pendingDirtyDocumentId.HasValue) return;
                if (pendingDirtyDocumentId.Value == documentId) SetDirty(true);
                pendingDirtyDocumentId = null;
            }
        }

        static void ResubscribeContainers()
        {
            foreach (var container in ObservedContainers)
            {
                container.ModifiedChanged -= OnContainerModifiedChanged;
                container.ContentChanged -= OnContainerContentChanged;
            }

            ObservedContainers.Clear();

            foreach (var container in DataManager.SourceItems ?? Enumerable.Empty<ITCDataContainer>())
            {
                if (container == null) continue;

                ObservedContainers.Add(container);
                container.ModifiedChanged += OnContainerModifiedChanged;
                container.ContentChanged += OnContainerContentChanged;
            }
            foreach (var report in DataManager.Reports ?? Enumerable.Empty<AnalysisReport>())
            {
                if (report == null) continue;
                ObservedContainers.Add(report);
                report.ModifiedChanged += OnContainerModifiedChanged;
                report.ContentChanged += OnContainerContentChanged;
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
