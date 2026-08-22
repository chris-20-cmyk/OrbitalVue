using System.IO;

namespace StreamVue.Player.Services;

public static class StreamVueDataPaths
{
    public const string OverrideEnvironmentVariable = "STREAMVUE_DATA_ROOT";

    public static string Resolve(string fileName)
    {
        var overrideRoot = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamVue")
            : Path.GetFullPath(overrideRoot);
        return Path.Combine(root, fileName);
    }
}
