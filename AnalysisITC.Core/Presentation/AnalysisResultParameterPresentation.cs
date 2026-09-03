using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    internal static class AnalysisResultParameterPresentation
    {
        public static IReadOnlyList<Parameter> LockedParameters(AnalysisResult result)
        {
            var model = result?.Solution?.Model;
            if (model == null) return Array.Empty<Parameter>();

            var exposed = model.Models.Count > 1
                ? model.Parameters?.GlobalTable?.Values
                : model.Models.FirstOrDefault()?.Parameters?.Table?.Values;

            return exposed?
                .Where(parameter => parameter.IsLocked && !parameter.IsGloballyDetermined)
                .ToList()
                ?? (IReadOnlyList<Parameter>)Array.Empty<Parameter>();
        }
    }
}
