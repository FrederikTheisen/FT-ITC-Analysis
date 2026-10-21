using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AnalysisITC.Core.Application
{
    /// <summary>Describes the effective preference fields represented by a settings notification.</summary>
    public sealed class PreferencesChangeSet : EventArgs
    {
        static readonly string[] PreferencePropertyNames = typeof(PreferencesState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        static readonly HashSet<string> PreferencePropertyNameSet = new HashSet<string>(PreferencePropertyNames, StringComparer.Ordinal);

        readonly HashSet<string> changedProperties;

        PreferencesChangeSet(bool isFullApply, IEnumerable<string> changedProperties)
        {
            IsFullApply = isFullApply;
            this.changedProperties = new HashSet<string>(changedProperties ?? Array.Empty<string>(), StringComparer.Ordinal);
            ChangedProperties = Array.AsReadOnly(this.changedProperties.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        }

        public bool IsFullApply { get; }
        public IReadOnlyCollection<string> ChangedProperties { get; }

        public bool Contains(string propertyName) => propertyName != null && changedProperties.Contains(propertyName);

        public bool ContainsAny(params string[] propertyNames) => propertyNames != null && propertyNames.Any(Contains);

        public new static PreferencesChangeSet Empty { get; } = new PreferencesChangeSet(false, Array.Empty<string>());
        public static PreferencesChangeSet Full { get; } = new PreferencesChangeSet(true, PreferencePropertyNames);

        public static PreferencesChangeSet FromEventArgs(EventArgs args) => args as PreferencesChangeSet ?? Full;

        public static PreferencesChangeSet Between(PreferencesState before, PreferencesState after)
        {
            if (before == null) return Full;
            if (after == null) return Empty;

            var changed = new List<string>();
            foreach (var propertyName in PreferencePropertyNames)
            {
                var property = typeof(PreferencesState).GetProperty(propertyName);
                var previous = property.GetValue(before);
                var current = property.GetValue(after);
                if (!Equals(previous, current)) changed.Add(propertyName);
            }
            return changed.Count == 0 ? Empty : new PreferencesChangeSet(false, changed);
        }

        internal static bool IsPreferenceProperty(string propertyName) => PreferencePropertyNameSet.Contains(propertyName);
    }
}
