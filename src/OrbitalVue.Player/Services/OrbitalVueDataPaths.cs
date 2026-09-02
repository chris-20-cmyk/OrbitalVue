using System.IO;

namespace OrbitalVue.Player.Services;

public static class OrbitalVueDataPaths
{
    public const string OverrideEnvironmentVariable = "ORBITALVUE_DATA_ROOT";

    public static string Resolve(string fileName)
    {
        var overrideRoot = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        var root = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitalVue")
            : Path.GetFullPath(overrideRoot);
        return Path.Combine(root, fileName);
    }
}
