using System.Collections.Generic;
using System.Linq;
using UniformTypeIdentifiers;

namespace AnalysisITC.UI.MacOS
{
    public static class MacITCFormatTypes
    {
        public static UTType[] DataFiles()
        {
            var types = new List<UTType>();

            types.AddRange(UTType.GetTypes("itc", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("ta", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("dat", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("aff", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("dh", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("apj", UTTagClass.FilenameExtension, UTTypes.Data).ToList());

            return types.ToArray();
        }

        public static UTType[] ProjectFile()
        {
            return UTType.GetTypes("ftitc", UTTagClass.FilenameExtension, UTTypes.Data);
        }

        public static UTType[] GetAllUTTypes()
        {
            var list = new List<UTType>();
            list.AddRange(DataFiles());
            list.AddRange(ProjectFile());

            return list.ToArray();
        }
    }
}
