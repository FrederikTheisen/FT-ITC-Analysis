using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Supporting-figure sources and settings retained for one document in this session.</summary>
    public sealed class SupportingFigureSelectionState
    {
        Guid? documentId;
        List<ITCDataContainer> selection = new List<ITCDataContainer>();
        PublicationFigureCanvasOptions canvasOptions;
        double? previewZoom = 1;

        public void Capture(Guid owningDocumentId, IEnumerable<ITCDataContainer> sources,
            PublicationFigureCanvasOptions options = null, double? zoom = 1)
        {
            if (documentId != owningDocumentId) canvasOptions = null;
            documentId = owningDocumentId;
            selection = sources.ToList();
            if (options != null && string.IsNullOrEmpty(options.Validate())) canvasOptions = options.Clone();
            previewZoom = zoom;
        }

        public PublicationFigureCanvasOptions RestoreOptions(Guid currentDocumentId)
            => documentId == currentDocumentId && canvasOptions != null
                ? canvasOptions.Clone() : new PublicationFigureCanvasOptions();

        /// <summary>Null represents the native preview's Fit mode.</summary>
        public double? RestorePreviewZoom(Guid currentDocumentId)
            => documentId == currentDocumentId ? previewZoom : 1;

        public IReadOnlyList<ITCDataContainer> Restore(Guid currentDocumentId,
            IEnumerable<ITCDataContainer> availableSources, ITCDataContainer initialSelection)
        {
            if (documentId != currentDocumentId)
            {
                documentId = null;
                selection.Clear();
                canvasOptions = null;
                previewZoom = 1;
                return initialSelection == null
                    ? Array.Empty<ITCDataContainer>()
                    : new[] { initialSelection };
            }

            var available = new HashSet<ITCDataContainer>(availableSources);
            selection = selection.Where(available.Contains).ToList();
            return selection.ToArray();
        }
    }
}
