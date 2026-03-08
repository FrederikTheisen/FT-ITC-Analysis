using Foundation;

public static class AppVersion
{
    static string? ShortVersion =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString();

    static string? BuildVersion =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString();

    /// <summary>
    /// Returns the full app version x.y.z...
    /// </summary>
    public static string FullVersionString
    {
        get
        {
            var v = ShortVersion ?? "?.?.?";
            return v;
        }
    }

    /// <summary>
    /// Return app version major.minor
    /// </summary>
    public static string ShortVersionString
    {
        get
        {
            var vs = FullVersionString.Split('.');

            return $"{vs[0]}.{vs[1]}";
        }
    }
}