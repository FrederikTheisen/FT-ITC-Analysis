
namespace AnalysisITC.Platform
{
    public interface IAppEnvironment
    {
        string LocaleIdentifier { get; }
        string ShortVersion { get; }
        string BuildVersion { get; }
        string ApplicationDataDirectory { get; }
        string AutoSaveDirectory { get; }

        string GetResourcePath(string name, string extension);
    }
}
