using Foundation;

public static class AppVersion
{
    static string? ShortVersion =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString();

    static string? BuildVersion =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString();

    // Convenient combined string, e.g. "1.2.3 (456)"
    public static string FullVersionString
    {
        get
        {
            var v = ShortVersion ?? "?.?.?";
            var b = BuildVersion;
            return v;
        }
    }

    public static string ShortVersionString
    {
        get
        {
            var vs = FullVersionString.Split('.');

            return $"{vs[0]}.{vs[1]}";
        }
    }
}