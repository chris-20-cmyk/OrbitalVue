namespace StreamVue.Player.Services;

public enum StreamVueDistributionMode
{
    Personal,
    Store,
    Unknown
}

public enum PremiumAccessState
{
    Included,
    Verified,
    Unavailable
}

public sealed record PremiumAccessSnapshot(
    string ContractVersion,
    string FeatureId,
    StreamVueDistributionMode DistributionMode,
    PremiumAccessState AccessState,
    string Acquisition,
    string ReceiptVerification,
    string? ProductId = null)
{
    public bool CanUseMediaCenters => AccessState is PremiumAccessState.Included or PremiumAccessState.Verified;

    public string BadgeText => AccessState switch
    {
        PremiumAccessState.Included => "PERSONAL BUILD • INCLUDED",
        PremiumAccessState.Verified => "PREMIUM • VERIFIED",
        _ => "PREMIUM • STORE LOCKED"
    };

    public string Explanation => AccessState switch
    {
        PremiumAccessState.Included => "Plex and Emby are included in this personal build.",
        PremiumAccessState.Verified => "A one-time store purchase was verified for this device account.",
        _ => "A verified one-time store purchase is required. Store purchase verification is not connected in this build."
    };
}

public static class PremiumAccessPolicy
{
    public const string ContractVersion = "1.0";
    public const string MediaCentersFeatureId = "personal-media-centers";

    public static PremiumAccessSnapshot Current
    {
        get
        {
#if STREAMVUE_STORE_BUILD
            return Evaluate("store", hasVerifiedStorePurchase: false);
#else
            return Evaluate("personal", hasVerifiedStorePurchase: false);
#endif
        }
    }

    public static PremiumAccessSnapshot Evaluate(
        string? distributionMode,
        bool hasVerifiedStorePurchase,
        string? productId = null)
    {
        var mode = ParseDistributionMode(distributionMode);
        if (mode == StreamVueDistributionMode.Personal)
        {
            return new PremiumAccessSnapshot(
                ContractVersion,
                MediaCentersFeatureId,
                mode,
                PremiumAccessState.Included,
                "included",
                "not-required");
        }

        var normalizedProductId = NormalizeProductId(productId);
        if (mode == StreamVueDistributionMode.Store &&
            hasVerifiedStorePurchase &&
            normalizedProductId is not null)
        {
            return new PremiumAccessSnapshot(
                ContractVersion,
                MediaCentersFeatureId,
                mode,
                PremiumAccessState.Verified,
                "one-time",
                "verified",
                normalizedProductId);
        }

        return new PremiumAccessSnapshot(
            ContractVersion,
            MediaCentersFeatureId,
            mode,
            PremiumAccessState.Unavailable,
            "one-time",
            "unavailable");
    }

    public static void RequireMediaCenters(PremiumAccessSnapshot access)
    {
        if (!access.CanUseMediaCenters) throw new InvalidOperationException(access.Explanation);
    }

    private static StreamVueDistributionMode ParseDistributionMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "personal" => StreamVueDistributionMode.Personal,
            "store" => StreamVueDistributionMode.Store,
            _ => StreamVueDistributionMode.Unknown
        };

    private static string? NormalizeProductId(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length is < 3 or > 256) return null;
        return candidate.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            ? candidate
            : null;
    }
}
