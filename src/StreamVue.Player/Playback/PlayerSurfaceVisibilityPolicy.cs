namespace StreamVue.Player.Playback;

public readonly record struct PlayerSurfaceVisibility(
    bool ShowVideoSurface,
    bool ShowEmptyState,
    bool ShowMultiview);

public static class PlayerSurfaceVisibilityPolicy
{
    public static PlayerSurfaceVisibility Evaluate(
        bool hasChannel,
        bool isWindowMinimized,
        bool isPlayerChromeSuppressed,
        bool isMultiviewMode,
        bool isModalVisible,
        bool isMultiviewWorkspaceVisible)
    {
        // Window activation is deliberately not part of this policy. Native video must
        // keep presenting when the viewer works in another app or on another monitor.
        var canPresentVideo = !isWindowMinimized;
        return new PlayerSurfaceVisibility(
            ShowVideoSurface: canPresentVideo && !isPlayerChromeSuppressed && !isMultiviewMode && hasChannel,
            ShowEmptyState: !isPlayerChromeSuppressed && !isMultiviewMode && !hasChannel,
            ShowMultiview: canPresentVideo && isMultiviewMode && !isModalVisible && isMultiviewWorkspaceVisible);
    }
}
