#if os(tvOS)
import StreamVueCore
import SwiftUI

private enum TVBrowseDestination: Hashable {
    case all
    case favorites
    case group(String)
}

private enum TVSheet: String, Identifiable {
    case source
    case settings
    var id: String { rawValue }
}

struct AppleTVRootView: View {
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme
    @State private var destination: TVBrowseDestination = .all
    @State private var activeSheet: TVSheet?
    @State private var fullscreenChannel: CatalogChannel?

    var body: some View {
        ZStack(alignment: .bottom) {
            theme.backgroundGradient.ignoresSafeArea()
            content
            if let notice = store.notice {
                NoticeBanner(message: notice, onDismiss: store.dismissNotice)
                    .frame(maxWidth: 760)
                    .padding(.bottom, 34)
            }
        }
        .overlay(alignment: .top) {
            if store.isLoading, store.catalog != nil {
                ProgressView(store.loadingLabel)
                    .padding(.horizontal, 22)
                    .padding(.vertical, 12)
                    .background(.ultraThinMaterial, in: Capsule())
                    .padding(.top, 14)
            }
        }
        .sheet(item: $activeSheet) { sheet in
            switch sheet {
            case .source: SourceImportView()
            case .settings: PlaybackSettingsView()
            }
        }
        .fullScreenCover(item: $fullscreenChannel) { channel in
            FullscreenPlayerView(channel: channel)
        }
        .alert(
            "OrbitalVue needs attention",
            isPresented: Binding(
                get: { store.errorMessage != nil },
                set: { if !$0 { store.dismissError() } }
            )
        ) {
            Button("Got it", role: .cancel) { store.dismissError() }
        } message: {
            Text(store.errorMessage ?? "OrbitalVue could not complete that request.")
        }
        .onChange(of: destination) { _, destination in
            switch destination {
            case .group(let group): store.selectGroup(group)
            case .all, .favorites: store.selectGroup(nil)
            }
        }
    }

    @ViewBuilder
    private var content: some View {
        if store.catalog == nil, store.isLoading {
            VStack(spacing: 22) {
                ProgressView().controlSize(.large).tint(theme.accent)
                Text(store.loadingLabel).foregroundStyle(theme.muted)
            }
        } else if store.catalog == nil {
            FirstRunOnboardingView { activeSheet = .source }
        } else {
            VStack(spacing: 0) {
                TVHeader(
                    onRefresh: { Task { await store.refresh() } },
                    onAddSource: { activeSheet = .source },
                    onSettings: { activeSheet = .settings }
                )
                TVWorkspace(
                    destination: $destination,
                    onFullscreen: { fullscreenChannel = $0 }
                )
            }
        }
    }
}

private struct TVHeader: View {
    let onRefresh: () -> Void
    let onAddSource: () -> Void
    let onSettings: () -> Void
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        HStack(spacing: 20) {
            BrandMark()
            if let catalog = store.catalog {
                Text(
                    store.isMediaCenterSource
                        ? "\(catalog.channels.count.formatted()) ITEMS  •  \(store.groups.count.formatted()) LIBRARIES"
                        : "\(catalog.channels.count.formatted()) CHANNELS  •  \(store.groups.count.formatted()) GROUPS"
                )
                    .font(.caption.weight(.bold))
                    .tracking(1)
                    .foregroundStyle(theme.muted)
            }
            Spacer()
            if let source = store.catalog?.sources.first {
                SourceStatusPill(source: source, usedCachedFallback: store.notice?.contains("last working") == true)
            }
            Button(action: onRefresh) { Label("Refresh", systemImage: "arrow.clockwise") }
                .disabled(store.isLoading)
            Button(action: onAddSource) { Label("Source", systemImage: "plus") }
            Button(action: onSettings) { Label("Settings", systemImage: "gearshape") }
        }
        .buttonStyle(.bordered)
        .padding(.horizontal, 38)
        .padding(.vertical, 20)
        .background(theme.background.opacity(0.93))
        .overlay(alignment: .bottom) { Rectangle().fill(theme.border).frame(height: 1) }
    }
}

private struct TVWorkspace: View {
    @Binding var destination: TVBrowseDestination
    let onFullscreen: (CatalogChannel) -> Void
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        GeometryReader { proxy in
            HStack(spacing: 18) {
                TVGroupRail(destination: $destination)
                    .frame(width: min(310, proxy.size.width * 0.18))
                TVChannelRail(
                    title: title,
                    sections: destination == .favorites ? store.favoriteSections : store.visibleSections,
                    favoritesOnly: destination == .favorites
                )
                .frame(width: min(500, proxy.size.width * 0.29))
                PlayerPanel(onFullscreen: onFullscreen)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            .padding(.horizontal, 34)
            .padding(.vertical, 26)
        }
    }

    private var title: String {
        switch destination {
        case .all: store.isMediaCenterSource ? "All media" : "All channels"
        case .favorites: "Favorites"
        case .group(let name): name
        }
    }
}

private struct TVGroupRail: View {
    @Binding var destination: TVBrowseDestination
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("LIBRARY")
                .font(.caption.weight(.black))
                .tracking(2)
                .foregroundStyle(theme.muted)
            ScrollView {
                LazyVStack(spacing: 10) {
                    groupButton(
                        store.isMediaCenterSource ? "All media" : "All channels",
                        count: store.catalog?.channels.count ?? 0,
                        icon: "rectangle.stack",
                        value: .all
                    )
                    groupButton("Favorites", count: store.favorites.count, icon: "star", value: .favorites)
                    ForEach(store.groups) { group in
                        groupButton(group.name, count: group.count, icon: "square.grid.2x2", value: .group(group.name))
                    }
                }
            }
        }
        .padding(18)
        .background(theme.backgroundRaised.opacity(0.85), in: RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay { RoundedRectangle(cornerRadius: 20, style: .continuous).stroke(theme.border) }
    }

    private func groupButton(
        _ name: String,
        count: Int,
        icon: String,
        value: TVBrowseDestination
    ) -> some View {
        Button {
            destination = value
        } label: {
            HStack(spacing: 12) {
                Image(systemName: icon).frame(width: 26)
                Text(name).lineLimit(1)
                Spacer()
                Text(count.formatted())
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(destination == value ? theme.background : theme.muted)
            }
            .font(.subheadline.weight(.semibold))
            .padding(.horizontal, 14)
            .padding(.vertical, 13)
            .foregroundStyle(destination == value ? theme.background : theme.text)
            .background(destination == value ? theme.accent : theme.surface, in: RoundedRectangle(cornerRadius: 13))
        }
        .buttonStyle(.plain)
    }
}

private struct TVChannelRail: View {
    let title: String
    let sections: [ChannelSection]
    let favoritesOnly: Bool
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme
    @FocusState private var focusedChannelID: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title)
                .font(.title2.bold())
                .lineLimit(1)
            TextField(
                store.isMediaCenterSource ? "Search media or libraries" : "Search channels or groups",
                text: Binding(
                    get: { store.query },
                    set: { value in store.updateQuery(value) }
                )
            )
            .textFieldStyle(.plain)
            .padding(.horizontal, 16)
            .padding(.vertical, 13)
            .background(theme.surface, in: RoundedRectangle(cornerRadius: 13))
            .overlay { RoundedRectangle(cornerRadius: 13).stroke(theme.border) }

            if sections.isEmpty {
                EmptyLibraryView(favoritesOnly: favoritesOnly, mediaCenter: store.isMediaCenterSource)
            } else {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 10, pinnedViews: [.sectionHeaders]) {
                            ForEach(sections) { section in
                                Section {
                                    ForEach(section.channels) { channel in
                                        TVChannelButton(
                                            channel: channel,
                                            isSelected: store.selectedChannel?.id == channel.id
                                        ) {
                                            store.selectChannel(channel)
                                        }
                                        .focused($focusedChannelID, equals: channel.id)
                                        .id(channel.id)
                                    }
                                } header: {
                                    HStack {
                                        Text(section.name)
                                        Spacer()
                                        Text(section.channels.count.formatted())
                                    }
                                    .font(.caption.weight(.bold))
                                    .foregroundStyle(theme.muted)
                                    .padding(.horizontal, 4)
                                    .padding(.vertical, 8)
                                    .background(theme.backgroundRaised)
                                }
                            }
                        }
                    }
                    .onChange(of: focusedChannelID) { _, value in
                        guard let value else { return }
                        withAnimation(.easeOut(duration: 0.18)) { proxy.scrollTo(value, anchor: .center) }
                    }
                }
            }
        }
        .padding(18)
        .background(theme.backgroundRaised.opacity(0.85), in: RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay { RoundedRectangle(cornerRadius: 20, style: .continuous).stroke(theme.border) }
        .onAppear {
            focusedChannelID = sections.first?.channels.first?.id
        }
        .onChange(of: sections.first?.channels.first?.id) { _, newValue in
            if focusedChannelID == nil { focusedChannelID = newValue }
        }
    }
}

private struct TVChannelButton: View {
    let channel: CatalogChannel
    let isSelected: Bool
    let action: () -> Void
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme
    @Environment(\.isFocused) private var isFocused
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        Button(action: action) {
            HStack(spacing: 13) {
                ChannelMonogram(channel: channel)
                VStack(alignment: .leading, spacing: 3) {
                    Text(channel.name).font(.headline).lineLimit(1)
                    HStack(spacing: 7) {
                        Text(channel.kind.label).foregroundStyle(theme.accent)
                        if store.favorites.contains(channel.id) {
                            Image(systemName: "star.fill").foregroundStyle(theme.warning)
                        }
                    }
                    .font(.caption2.weight(.bold))
                }
                Spacer()
                Image(systemName: "play.fill")
                    .foregroundStyle(isSelected ? theme.background : theme.accent)
            }
            .padding(.horizontal, 13)
            .padding(.vertical, 11)
            .foregroundStyle(theme.text)
            .background(
                isSelected ? theme.accent.opacity(0.22) : theme.surface,
                in: RoundedRectangle(cornerRadius: 15, style: .continuous)
            )
            .overlay {
                RoundedRectangle(cornerRadius: 15, style: .continuous)
                    .stroke(isFocused || isSelected ? theme.accent : theme.border, lineWidth: isFocused ? 4 : 1)
            }
        }
        .buttonStyle(.plain)
        .scaleEffect(isFocused ? 1.025 : 1)
        .animation(reduceMotion ? nil : .easeOut(duration: 0.14), value: isFocused)
        .accessibilityLabel(channel.name)
        .accessibilityValue("\(channel.kind.label)\(isSelected ? ", selected" : "")")
        .accessibilityHint("Starts playback")
    }
}
#endif
