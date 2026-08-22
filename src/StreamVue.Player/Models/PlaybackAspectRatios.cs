namespace StreamVue.Player.Models;

public static class PlaybackAspectRatios
{
    public static IReadOnlyList<string> SupportedLabels { get; } =
    [
        "Auto",
        "Fill",
        "4:3",
        "5:4",
        "3:2",
        "14:9",
        "16:10",
        "16:9",
        "18:9",
        "21:9",
        "2.35:1",
        "2.39:1",
        "32:9"
    ];

    public static string? ToLibVlcValue(string? label) => label switch
    {
        "4:3" => "4:3",
        "5:4" => "5:4",
        "3:2" => "3:2",
        "14:9" => "14:9",
        "16:10" => "16:10",
        "16:9" => "16:9",
        "18:9" => "2:1",
        "21:9" => "21:9",
        "2.35:1" => "47:20",
        "2.39:1" => "239:100",
        "32:9" => "32:9",
        _ => null
    };

    public static bool IsFill(string? label) => string.Equals(label, "Fill", StringComparison.OrdinalIgnoreCase);
}
