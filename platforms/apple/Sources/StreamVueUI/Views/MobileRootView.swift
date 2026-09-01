#if os(iOS)
import StreamVueCore
import SwiftUI

private enum BrowseDestination: Hashable {
    case all
    case favorites
    case group(String)
}

private enum MobileSheet: String, Identifiable {
    case source
    case settings
    var id: String { rawValue }
}

struct MobileRootView: View {
    @Environment(\.horizontalSizeClass) private var horizontalSizeClass
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme
    @State private var browseDestination: BrowseDestination? = .all
    @State private var activeSheet: MobileSheet?
    @State private var fullscreenChannel: CatalogChannel?

    var body: some View {
        ZStack(alignment: .bottom) {
            theme.backgroundGradient.ignoresSafeArea()
            content
            if let notice = store.notice {
                NoticeBanner(message: notice, onDismiss: store.dismissNotice)
                    .padding(18)
                    .frame(maxWidth: 620)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .overlay(alignment: .top) {
            if store.isLoading, store.catalog != nil {
                ProgressView(store.loadingLabel)
                    .padding(.horizontal, 16)
                    .padding(.vertical, 10)
                    .background(.ultraThinMaterial, in: Capsule())
                    .padding(.top, 10)
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
        .onChange(of: browseDestination) { _, destination in
            switch destination {
            case .group(let group): store.selectGroup(group)
            case .all, .favorites, .none: store.selectGroup(nil)
            }
        }
    }

    @ViewBuilder
    private var content: some View {
        if store.catalog == nil, store.isLoading {
            VStack(spacing: 18) {
                ProgressView().controlSize(.large).tint(theme.accent)
                Text(store.loadingLabel).foregroundStyle(theme.muted)
            }
        } else if store.catalog == nil {
            FirstRunOnboardingView { activeSheet = .source }
        } else {
            library
        }
    }

    private var library: some View {
        NavigationSplitView {
            List(selection: $browseDestination) {
                Section {
                    Label(store.isMediaCenterSource ? "All media" : "All channels", systemImage: "rectangle.stack")
                        .tag(BrowseDestination.all)
                    Label("Favorites", systemImage: "star")
                        .tag(BrowseDestination.favorites)
                }
                Section(store.isMediaCenterSource ? "Libraries" : "Playlist groups") {
                    ForEach(store.groups) { group in
                        HStack {
                            Text(group.name).lineLimit(1)
                            Spacer()
                            Text(group.count.formatted())
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(theme.muted)
                        }
                        .tag(BrowseDestination.group(group.name))
                    }
                }
            }
            .scrollContentBackground(.hidden)
            .background(theme.background)
            .navigationTitle(store.isMediaCenterSource ? "Media" : "Channels")
            .safeAreaInset(edge: .top) {
                BrandMark(compact: true)
                    .padding(.horizontal, 16)
                    .padding(.top, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(theme.background)
            }
        } content: {
            ChannelBrowserView(
                title: browseTitle,
                sections: browseDestination == .favorites ? store.favoriteSections : store.visibleSections,
                favoritesOnly: browseDestination == .favorites,
                onSelect: selectChannel
            )
            .toolbar {
                ToolbarItemGroup(placement: .topBarTrailing) {
                    Button { Task { await store.refresh() } } label: {
                        Image(systemName: "arrow.clockwise")
                    }
                    .disabled(store.isLoading)
                    Button { activeSheet = .source } label: {
                        Image(systemName: "plus")
                    }
                    Button { activeSheet = .settings } label: {
                        Image(systemName: "gearshape")
                    }
                }
            }
        } detail: {
            PlayerPanel { fullscreenChannel = $0 }
                .padding(18)
                .background(theme.backgroundGradient)
                .navigationTitle(store.selectedChannel?.name ?? "Now playing")
                .navigationBarTitleDisplayMode(.inline)
        }
        .navigationSplitViewStyle(.balanced)
    }

    private var browseTitle: String {
        switch browseDestination {
        case .favorites: "Favorites"
        case .group(let group): group
        case .all, .none: store.isMediaCenterSource ? "All media" : "All channels"
        }
    }

    private func selectChannel(_ channel: CatalogChannel) {
        store.selectChannel(channel)
        if horizontalSizeClass == .compact {
            fullscreenChannel = channel
        }
    }
}

private struct ChannelBrowserView: View {
    let title: String
    let sections: [ChannelSection]
    let favoritesOnly: Bool
    let onSelect: (CatalogChannel) -> Void
    @Environment(StreamVueStore.self) private var store
    @Environment(StreamVueTheme.self) private var theme

    var body: some View {
        Group {
            if sections.isEmpty {
                EmptyLibraryView(favoritesOnly: favoritesOnly, mediaCenter: store.isMediaCenterSource)
            } else {
                List {
                    ForEach(sections) { section in
                        Section {
                            ForEach(section.channels) { channel in
                                ChannelRow(
                                    channel: channel,
                                    isSelected: store.selectedChannel?.id == channel.id,
                                    isFavorite: store.favorites.contains(channel.id),
                                    onSelect: { onSelect(channel) },
                                    onFavorite: { store.toggleFavorite(channel) }
                                )
                                .listRowInsets(.init(top: 5, leading: 14, bottom: 5, trailing: 14))
                                .listRowBackground(Color.clear)
                                .listRowSeparator(.hidden)
                            }
                        } header: {
                            HStack {
                                Text(section.name)
                                Spacer()
                                Text(section.channels.count.formatted())
                            }
                            .font(.caption.weight(.bold))
                            .foregroundStyle(theme.muted)
                        }
                    }
                }
                .listStyle(.plain)
                .scrollContentBackground(.hidden)
                .background(theme.background)
            }
        }
        .navigationTitle(title)
        .searchable(
            text: Binding(
                get: { store.query },
                set: { value in store.updateQuery(value) }
            ),
            placement: .navigationBarDrawer(displayMode: .always),
            prompt: store.isMediaCenterSource ? "Search media or libraries" : "Search channels or groups"
        )
    }
}
#endif
