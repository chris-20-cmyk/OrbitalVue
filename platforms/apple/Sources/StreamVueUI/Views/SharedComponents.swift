#if os(iOS) || os(tvOS)
import StreamVueCore
import SwiftUI

struct BrandMark: View {
    @Environment(StreamVueTheme.self) private var theme
    var compact = false

    var body: some View {
        HStack(spacing: compact ? 10 : 14) {
            ZStack {
                RoundedRectangle(cornerRadius: compact ? 11 : 15, style: .continuous)
                    .fill(theme.accentDim.opacity(0.75))
                    .overlay {
                        RoundedRectangle(cornerRadius: compact ? 11 : 15, style: .continuous)
                            .stroke(theme.accent.opacity(0.32), lineWidth: 1)
                    }
                Image(systemName: "play.fill")
                    .font(.system(size: compact ? 18 : 24, weight: .black))
                    .foregroundStyle(theme.accent)
                    .offset(x: 1)
            }
            .frame(width: compact ? 40 : 52, height: compact ? 40 : 52)

            VStack(alignment: .leading, spacing: 2) {
                Text("ORBITALVUE")
                    .font(.system(size: compact ? 15 : 20, weight: .black, design: .rounded))
                    .tracking(compact ? 2 : 3)
                Text("YOUR SIGNAL. BEAUTIFULLY ORGANIZED.")
                    .font(.system(size: compact ? 7 : 9, weight: .semibold, design: .rounded))
                    .tracking(1.2)
                    .foregroundStyle(theme.muted)
            }
        }
        .foregroundStyle(theme.text)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("OrbitalVue")
    }
}

struct SourceStatusPill: View {
    let source: CatalogSource
    let usedCachedFallback: Bool
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        HStack(spacing: 8) {
            Circle()
                .fill(usedCachedFallback ? theme.warning : theme.accent)
                .frame(width: 7, height: 7)
            VStack(alignment: .leading, spacing: 1) {
                Text(usedCachedFallback ? "OFFLINE COPY" : "SOURCE READY")
                    .font(.caption2.weight(.bold))
                Text(source.displayLocation)
                    .font(.caption2)
                    .foregroundStyle(theme.muted)
                    .lineLimit(1)
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(theme.surface, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .stroke(theme.border, lineWidth: 1)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(usedCachedFallback ? "Offline source copy" : "Source ready")
        .accessibilityValue(source.name)
    }
}

struct ChannelRow: View {
    let channel: CatalogChannel
    let isSelected: Bool
    let isFavorite: Bool
    let onSelect: () -> Void
    let onFavorite: () -> Void
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        HStack(spacing: 8) {
            Button(action: onSelect) {
                HStack(spacing: 13) {
                    ChannelMonogram(channel: channel)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(channel.name)
                            .font(.headline)
                            .foregroundStyle(theme.text)
                            .lineLimit(1)
                        HStack(spacing: 8) {
                            Text(channel.group)
                                .lineLimit(1)
                            Text(channel.kind.label)
                                .font(.caption2.weight(.bold))
                                .foregroundStyle(theme.accent)
                        }
                        .font(.caption)
                        .foregroundStyle(theme.muted)
                    }
                    Spacer(minLength: 8)
                    Image(systemName: "play.fill")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(isSelected ? theme.background : theme.accent)
                        .frame(width: 32, height: 32)
                        .background(isSelected ? theme.accent : theme.accentDim, in: Circle())
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityLabel(channel.name)
            .accessibilityValue("\(channel.group), \(channel.kind.label)\(isSelected ? ", selected" : "")")
            .accessibilityHint("Starts playback")
            .accessibilityAddTraits(isSelected ? .isSelected : [])
            Button(action: onFavorite) {
                Image(systemName: isFavorite ? "star.fill" : "star")
                    .foregroundStyle(isFavorite ? theme.warning : theme.muted)
                    .frame(width: 38, height: 38)
            }
            .buttonStyle(.plain)
            .accessibilityLabel(isFavorite ? "Remove \(channel.name) from favorites" : "Add \(channel.name) to favorites")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 10)
        .background(
            isSelected ? theme.accent.opacity(0.13) : theme.surface.opacity(0.72),
            in: RoundedRectangle(cornerRadius: 15, style: .continuous)
        )
        .overlay {
            RoundedRectangle(cornerRadius: 15, style: .continuous)
                .stroke(isSelected ? theme.accent.opacity(0.8) : theme.border, lineWidth: isSelected ? 2 : 1)
        }
        .accessibilityIdentifier("channel-\(channel.id)")
    }
}

struct ChannelMonogram: View {
    let channel: CatalogChannel
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(theme.backgroundRaised)
            Text(channel.initials)
                .font(.subheadline.weight(.black))
                .foregroundStyle(theme.accent)
        }
        .frame(width: 48, height: 48)
        .overlay {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .stroke(theme.border, lineWidth: 1)
        }
        .accessibilityHidden(true)
    }
}

struct NoticeBanner: View {
    let message: String
    let onDismiss: () -> Void
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "checkmark.circle.fill")
                .foregroundStyle(theme.accent)
                .accessibilityHidden(true)
            Text(message)
                .font(.subheadline.weight(.medium))
                .lineLimit(2)
            Spacer(minLength: 6)
            Button(action: onDismiss) {
                Image(systemName: "xmark")
                    .font(.caption.weight(.bold))
            }
            .buttonStyle(.plain)
            .accessibilityLabel("Dismiss notification")
        }
        .foregroundStyle(theme.text)
        .padding(.horizontal, 16)
        .padding(.vertical, 13)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .stroke(theme.accent.opacity(0.35), lineWidth: 1)
        }
        .shadow(color: .black.opacity(0.35), radius: 22, y: 12)
    }
}

struct EmptyLibraryView: View {
    let favoritesOnly: Bool
    var mediaCenter = false
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        ContentUnavailableView(
            title,
            systemImage: favoritesOnly ? "star" : "magnifyingglass",
            description: Text(description)
        )
        .foregroundStyle(theme.text)
    }

    private var title: String {
        if favoritesOnly { return mediaCenter ? "No favorite media" : "No favorite channels" }
        return mediaCenter ? "No matching media" : "No matching channels"
    }

    private var description: String {
        if favoritesOnly {
            return mediaCenter
                ? "Mark movies, episodes, or recordings with the star to keep them here."
                : "Mark channels with the star to keep them here."
        }
        return mediaCenter ? "Try another search or library." : "Try another search or playlist group."
    }
}
#endif
