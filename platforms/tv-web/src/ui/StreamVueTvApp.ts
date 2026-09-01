import {
  canResumeMedia,
  matchesMediaLibraryBrowseMode,
  orderMediaLibraryChannels,
  type CatalogChannel,
  type MediaLibraryBrowseMode,
  type StreamVueCatalog
} from "@streamvue/catalog";
import {
  CatalogRepository,
  isMediaCenterCatalog,
  type CatalogLoadResult
} from "../catalog/CatalogRepository.js";
import { SpatialNavigator } from "../navigation/SpatialNavigator.js";
import { exitTelevisionApp, registerPlatformRemoteKeys } from "../platform/platform.js";
import { createPlayerAdapter } from "../playback/createPlayer.js";
import {
  ASPECT_MODES,
  type PlaybackSignal,
  type PlayerAdapter
} from "../playback/PlayerAdapter.js";
import { icon, type IconName } from "./icons.js";
import {
  createTelevisionPremiumService,
  type TelevisionPremiumService,
  type TelevisionPremiumSnapshot
} from "../premium/TelevisionPremiumService.js";

type Screen = "loading" | "onboarding" | "browse" | "player";
type Modal = "source" | "search" | "confirm-clear" | null;
type SourceMode = "playlist" | "plex" | "emby";

const FAVORITES_KEY = "streamvue-tv-favorites-v1";
const ALL_GROUPS = "All Channels";
const FAVORITES_GROUP = "Favorites";
const CONTINUE_WATCHING_GROUP = "orbitalvue:continue-watching";
const RECENTLY_ADDED_GROUP = "orbitalvue:recently-added";
const LIVE_MEDIA_GROUP = "orbitalvue:live";
const MOVIES_GROUP = "orbitalvue:movies";
const SERIES_GROUP = "orbitalvue:series";
const MEDIA_BROWSE_GROUPS = [
  CONTINUE_WATCHING_GROUP,
  RECENTLY_ADDED_GROUP,
  LIVE_MEDIA_GROUP,
  MOVIES_GROUP,
  SERIES_GROUP
] as const;
const GROUP_WINDOW_SIZE = 8;

export class StreamVueTvApp {
  private readonly repository: CatalogRepository;
  private readonly premiumService: TelevisionPremiumService;
  private premium: TelevisionPremiumSnapshot;
  private readonly unsubscribePremium: () => void;
  private readonly navigator: SpatialNavigator;
  private catalog: StreamVueCatalog | null = null;
  private screen: Screen = "loading";
  private modal: Modal = null;
  private sourceMode: SourceMode = "playlist";
  private selectedGroup = ALL_GROUPS;
  private selectedChannelId: string | null = null;
  private favorites = loadFavorites();
  private notice: string | null = null;
  private error: string | null = null;
  private searchQuery = "";
  private player: PlayerAdapter | null = null;
  private playbackSignal: PlaybackSignal = { state: "idle", message: null, warning: null };
  private aspectIndex = 0;
  private hideChromeTimer: number | null = null;
  private noticeTimer: number | null = null;
  private modalReturnFocusSelector: string | null = null;
  private playbackRequestSerial = 0;
  private startupComplete = false;

  constructor(
    private readonly root: HTMLElement,
    premiumService: TelevisionPremiumService = createTelevisionPremiumService()
  ) {
    this.premiumService = premiumService;
    this.premium = premiumService.snapshot;
    this.repository = new CatalogRepository(
      undefined,
      undefined,
      undefined,
      () => this.premium.access
    );
    this.unsubscribePremium = premiumService.subscribe(this.onPremiumChanged);
    window.addEventListener("keydown", this.onAppKeyDown, { capture: true });
    window.addEventListener("focus", this.onWindowFocus);
    document.addEventListener("visibilitychange", this.onVisibilityChange);
    this.navigator = new SpatialNavigator(root, this.handleBack);
    this.root.addEventListener("click", this.onClick);
    this.root.addEventListener("focusin", this.onFocusIn);
    this.root.addEventListener("input", this.onInput);
    this.root.addEventListener("change", this.onChange);
  }

  async start(): Promise<void> {
    registerPlatformRemoteKeys();
    this.render();
    try {
      await this.premiumService.start();
      const useDemo = new URLSearchParams(window.location.search).get("demo") === "1";
      const loaded = useDemo ? await this.repository.useDemo() : await this.repository.loadSaved();
      if (loaded) this.applyCatalog(loaded);
      else this.screen = "onboarding";
    } catch (error) {
      this.screen = "onboarding";
      this.error = readableError(error, "The saved channel catalog could not be opened.");
    }
    this.startupComplete = true;
    this.render();
    this.focusAfterRender();
  }

  destroy(): void {
    this.player?.destroy();
    this.clearNoticeTimer();
    this.unsubscribePremium();
    this.premiumService.destroy();
    this.navigator.destroy();
    this.root.removeEventListener("click", this.onClick);
    this.root.removeEventListener("focusin", this.onFocusIn);
    this.root.removeEventListener("input", this.onInput);
    this.root.removeEventListener("change", this.onChange);
    window.removeEventListener("keydown", this.onAppKeyDown, { capture: true });
    window.removeEventListener("focus", this.onWindowFocus);
    document.removeEventListener("visibilitychange", this.onVisibilityChange);
  }

  private render(): void {
    this.root.innerHTML = this.screen === "loading"
      ? this.loadingTemplate()
      : this.screen === "onboarding"
        ? this.onboardingTemplate()
        : this.screen === "player"
          ? this.playerTemplate()
          : this.browseTemplate();
    this.navigator.setScope(this.modalElement() ?? this.root);
  }

  private loadingTemplate(): string {
    return `<main class="startup-screen" aria-busy="true">
      ${brandMark()}
      <div class="startup-loader" aria-hidden="true"></div>
      <p>Preparing your library</p>
    </main>`;
  }

  private onboardingTemplate(): string {
    return `<main class="onboarding-screen">
      <header class="onboarding-header">${brandMark()}<span>Television edition</span></header>
      <section class="onboarding-copy">
        <h1>Connect your content</h1>
        <p>Bring an M3U playlist or your personal Plex or Emby library to the television. OrbitalVue includes no content of its own.</p>
        ${this.error ? `<div class="message message-error" role="alert">${escapeHtml(this.error)}</div>` : ""}
        <label class="field-label" for="playlist-url">M3U playlist URL</label>
        <input id="playlist-url" class="tv-input" data-focusable="true" data-autofocus="true" inputmode="url" autocomplete="off" spellcheck="false" placeholder="https://provider.example/playlist.m3u" />
        <div class="onboarding-actions">
          <button class="button button-primary" data-action="connect-url" data-focusable="true">Connect playlist</button>
          <button class="button button-secondary" data-action="choose-file" data-focusable="true">Choose M3U file</button>
          <button class="button button-secondary" data-action="open-source-mode" data-source-mode="plex" data-focusable="true">Connect Plex</button>
          <button class="button button-secondary" data-action="open-source-mode" data-source-mode="emby" data-focusable="true">Connect Emby</button>
          <button class="button button-quiet" data-action="use-demo" data-focusable="true">Explore the interface</button>
        </div>
        <input id="playlist-file" class="visually-hidden" type="file" tabindex="-1" aria-hidden="true" accept=".m3u,.m3u8,text/plain,audio/x-mpegurl,application/vnd.apple.mpegurl" />
      </section>
      <footer class="privacy-note">Connect only sources and personal servers you are authorized to use.</footer>
      ${this.modalTemplate()}
    </main>`;
  }

  private browseTemplate(): string {
    const catalog = this.requireCatalog();
    const groups = this.groups();
    const channels = this.channelsForSelectedGroup();
    const selected = this.ensureSelectedChannel(channels);
    const groupWindow = windowAround(groups, groups.indexOf(this.selectedGroup), GROUP_WINDOW_SIZE);
    const channelIndex = selected ? channels.findIndex((channel) => channel.id === selected.id) : 0;
    const channelWindow = windowAround(channels, channelIndex, channelWindowSize());

    const mediaCenter = isMediaCenterCatalog(catalog);
    return `<main class="tv-shell">
      <header class="topbar">
        <div class="topbar-title">${brandMark()}<span class="topbar-divider"></span><h1>${mediaCenter ? "Media Library" : "Live TV"}</h1></div>
        <div class="topbar-actions">
          <span class="source-status">${escapeHtml(sourceStatus(catalog))}</span>
          <button class="icon-button" aria-label="Search ${mediaCenter ? "media" : "channels"}" data-action="open-search" data-focusable="true">${icon("search")}</button>
          <button class="icon-button" aria-label="Source settings" data-action="open-source" data-focusable="true">${icon("settings")}</button>
        </div>
      </header>
      <section class="browser-grid">
        <nav class="group-rail" aria-label="${mediaCenter ? "Media libraries" : "Channel groups"}">
          ${groupWindow.items.map((group) => groupButton(group, group === this.selectedGroup, this.groupCount(group), mediaCenter)).join("")}
        </nav>
        <section class="channel-pane" aria-label="${escapeAttribute(this.selectedGroup)}">
          <div class="pane-heading"><h2>${escapeHtml(groupLabel(this.selectedGroup, mediaCenter))}</h2><span>${channels.length.toLocaleString()}</span></div>
          <div class="channel-list">
            ${channelWindow.items.length > 0
              ? this.channelRows(channelWindow.items, channelWindow.start, selected?.id ?? null)
              : `<div class="empty-list"><strong>No ${mediaCenter ? "media" : "channels"} here yet</strong><span>Add a favorite or choose another group.</span></div>`}
          </div>
        </section>
        <section class="detail-pane" data-role="details" aria-live="polite">
          ${selected ? this.detailTemplate(selected) : this.emptyDetailTemplate()}
        </section>
      </section>
      ${this.notice ? `<div class="notice" role="status">${escapeHtml(this.notice)}</div>` : ""}
      <footer class="remote-hints">
        <span><kbd>OK</kbd> Play</span><span><kbd>↑ ↓</kbd> Browse</span><span><kbd>←</kbd> Groups</span><span><kbd>Back</kbd> Exit</span>
      </footer>
      ${this.modalTemplate()}
    </main>`;
  }

  private channelRows(channels: CatalogChannel[], startIndex: number, selectedId: string | null): string {
    let previousGroup: string | null = startIndex > 0 ? this.channelsForSelectedGroup()[startIndex - 1]?.group ?? null : null;
    return channels.map((channel) => {
      const showSection = this.selectedGroup === ALL_GROUPS && channel.group !== previousGroup;
      previousGroup = channel.group;
      return `${showSection ? `<div class="group-section-label">${escapeHtml(channel.group)}</div>` : ""}${channelButton(channel, channel.id === selectedId, this.favorites.has(channel.id))}`;
    }).join("");
  }

  private detailTemplate(channel: CatalogChannel): string {
    const isFavorite = this.favorites.has(channel.id);
    const mediaCenter = this.catalog ? isMediaCenterCatalog(this.catalog) : false;
    const demoProgram = channel.name === "Northstar News"
      ? "Evening Report"
      : mediaCenter
        ? mediaKindLabel(channel)
        : "Live channel";
    const preview = mediaCenter
      ? `<div class="media-preview-placeholder"><span>${escapeHtml(channelInitials(channel.name))}</span><small>${escapeHtml(mediaKindLabel(channel))}</small></div>`
      : `<img src="./assets/broadcast-preview.png" alt="" /><span class="preview-live"><i></i> LIVE</span>`;
    const metadata = mediaMetadataLine(channel);
    const progress = watchProgressPercent(channel);
    return `<div class="preview-frame">
        ${preview}
      </div>
      <div class="channel-detail-copy">
        <h2>${escapeHtml(channel.name.toUpperCase())}</h2>
        <p class="detail-group">${escapeHtml(channel.group)}</p>
        ${metadata ? `<p class="detail-metadata">${escapeHtml(metadata)}</p>` : ""}
        ${progress === null ? "" : `<div class="resume-progress" role="progressbar" aria-label="Watch progress" aria-valuenow="${progress}" aria-valuemin="0" aria-valuemax="100"><span style="width:${progress}%"></span></div><p class="resume-label">${progress}% watched</p>`}
        <p class="now-line"><i></i><span>${mediaCenter ? "Type:" : "Now:"}</span> ${escapeHtml(demoProgram)}</p>
      </div>
      <div class="detail-actions">
        <button class="button button-watch" data-action="watch" data-focusable="true">${icon("play")}<span>${mediaCenter ? canResumeMedia(channel) ? "Resume" : "Play" : "Watch now"}</span></button>
        <button class="button button-favorite${isFavorite ? " is-active" : ""}" data-action="favorite" data-focusable="true">${icon("favorite")}<span>${isFavorite ? "Favorited" : "Favorite"}</span></button>
      </div>`;
  }

  private emptyDetailTemplate(): string {
    return `<div class="preview-frame"><img src="./assets/broadcast-preview.png" alt="" /></div>
      <div class="channel-detail-copy"><h2>NO CHANNEL SELECTED</h2><p class="detail-group">Choose a group to continue.</p></div>`;
  }

  private modalTemplate(): string {
    if (this.modal === "source") return this.sourceModalTemplate();
    if (this.modal === "search") return this.searchModalTemplate();
    if (this.modal === "confirm-clear") return this.confirmClearTemplate();
    return "";
  }

  private sourceModalTemplate(): string {
    const source = this.catalog?.sources[0];
    return `<div class="modal-backdrop"><section class="modal modal-source" role="dialog" aria-modal="true" aria-labelledby="source-title">
      <div class="modal-header"><div><h2 id="source-title">Source manager</h2><p>${escapeHtml(source?.displayLocation ?? "Add a private source")}</p></div><button class="icon-button" aria-label="Close" data-action="close-modal" data-focusable="true">×</button></div>
      ${this.error ? `<div class="message message-error" role="alert">${escapeHtml(this.error)}</div>` : ""}
      <div class="source-tabs" role="tablist" aria-label="Source type">
        ${sourceTab("playlist", "Playlist", this.sourceMode)}
        ${sourceTab("plex", "Plex", this.sourceMode)}
        ${sourceTab("emby", "Emby", this.sourceMode)}
      </div>
      ${this.sourceFormTemplate()}
      <p class="vault-note">${escapeHtml(this.repository.credentialSecurityLabel)}</p>
      <div class="source-utility-actions">
        ${this.catalog ? `<button class="button button-secondary" data-action="refresh-source" data-focusable="true">Refresh active source</button><button class="button button-danger" data-action="request-clear" data-focusable="true">Remove saved source</button>` : ""}
        <button class="button button-quiet" data-action="close-modal" data-focusable="true">Close</button>
      </div>
      <input id="playlist-file" class="visually-hidden" type="file" tabindex="-1" aria-hidden="true" accept=".m3u,.m3u8,text/plain,audio/x-mpegurl,application/vnd.apple.mpegurl" />
    </section></div>`;
  }

  private sourceFormTemplate(): string {
    if (this.sourceMode === "playlist") {
      return `<div id="source-panel-playlist" class="source-form" role="tabpanel" aria-labelledby="source-tab-playlist">
        <label class="field-label" for="source-url">M3U playlist URL</label>
        <input id="source-url" class="tv-input" data-focusable="true" data-autofocus="true" inputmode="url" autocomplete="off" spellcheck="false" placeholder="https://provider.example/playlist.m3u" />
        <div class="source-form-actions">
          <button class="button button-primary" data-action="connect-source-url" data-focusable="true">Connect playlist</button>
          <button class="button button-secondary" data-action="choose-file" data-focusable="true">Choose M3U file</button>
        </div>
      </div>`;
    }
    const provider = this.sourceMode === "plex" ? "Plex" : "Emby";
    if (!this.premium.access.canUseMediaCenters) {
      return `<div id="source-panel-${this.sourceMode}" class="source-form" role="tabpanel" aria-labelledby="source-tab-${this.sourceMode}">
        <p class="premium-copy"><strong>${provider} • ${escapeHtml(this.premium.access.badgeText)}</strong><span>${escapeHtml(this.premium.message)}</span></p>
        ${this.premium.productTitle || this.premium.localizedPrice
          ? `<p class="premium-offer"><strong>${escapeHtml(this.premium.productTitle ?? "One-time premium unlock")}</strong>${this.premium.localizedPrice ? `<span>${escapeHtml(this.premium.localizedPrice)}</span>` : ""}</p>`
          : ""}
        ${this.premiumActionsTemplate()}
        <p class="vault-note">No media-server address or credential is collected while store verification is unavailable. Playlist sources remain available.</p>
      </div>`;
    }
    const providerFields = this.sourceMode === "plex"
      ? `<label class="field-label" for="plex-access">Plex server token</label><input id="plex-access" class="tv-input" type="password" data-focusable="true" autocomplete="off" spellcheck="false" placeholder="Paste the token for this server" />`
      : `<div class="source-field-grid"><div><label class="field-label" for="emby-user">Emby username</label><input id="emby-user" class="tv-input" data-focusable="true" autocomplete="off" spellcheck="false" /></div><div><label class="field-label" for="emby-password">Emby password</label><input id="emby-password" class="tv-input" type="password" data-focusable="true" autocomplete="off" /></div></div>`;
    return `<div id="source-panel-${this.sourceMode}" class="source-form" role="tabpanel" aria-labelledby="source-tab-${this.sourceMode}">
      <p class="premium-copy"><strong>${provider} • ${escapeHtml(this.premium.access.badgeText)}</strong><span>Credentials are verified against one server before protected requests begin. ${escapeHtml(this.premium.access.explanation)}</span></p>
      <label class="field-label" for="media-server">Server address</label>
      <input id="media-server" class="tv-input" data-focusable="true" data-autofocus="true" inputmode="url" autocomplete="off" spellcheck="false" placeholder="https://media-server.example:port" />
      ${providerFields}
      <label class="field-label" for="media-name">Server nickname <span>optional</span></label>
      <input id="media-name" class="tv-input" data-focusable="true" autocomplete="off" spellcheck="false" placeholder="Living room library" />
      <label class="consent-row">
        <input id="allow-media-http" type="checkbox" data-focusable="true" />
        <span><strong>Allow an unencrypted local HTTP server</strong><small>Only enable this for a server you trust on your home network.</small></span>
      </label>
      <div class="source-form-actions">
        <button class="button button-primary" data-action="connect-${this.sourceMode}" data-focusable="true">Connect ${provider}</button>
      </div>
    </div>`;
  }

  private premiumActionsTemplate(): string {
    const actions: string[] = [];
    if (this.premium.canBuy) {
      const price = this.premium.localizedPrice ? ` • ${escapeHtml(this.premium.localizedPrice)}` : "";
      actions.push(`<button class="button button-primary" data-action="buy-premium" data-focusable="true" data-autofocus="true">Buy once${price}</button>`);
    }
    if (this.premium.canRestore) {
      actions.push(`<button class="button button-secondary" data-action="restore-premium" data-focusable="true">Restore purchase</button>`);
    }
    return actions.length > 0 ? `<div class="source-form-actions premium-actions">${actions.join("")}</div>` : "";
  }

  private searchModalTemplate(): string {
    const mediaCenter = this.catalog ? isMediaCenterCatalog(this.catalog) : false;
    return `<div class="modal-backdrop"><section class="modal modal-search" role="dialog" aria-modal="true" aria-labelledby="search-title">
      <div class="modal-header"><div><h2 id="search-title">Find ${mediaCenter ? "media" : "a channel"}</h2><p>Search names and ${mediaCenter ? "media libraries" : "exact playlist groups"}.</p></div><button class="icon-button" aria-label="Close" data-action="close-modal" data-focusable="true">×</button></div>
      <input id="channel-search" class="tv-input" data-focusable="true" data-autofocus="true" autocomplete="off" value="${escapeAttribute(this.searchQuery)}" placeholder="Type ${mediaCenter ? "a title or library" : "a channel or group"}" />
      <div class="search-results" data-role="search-results">${this.searchResultsTemplate()}</div>
    </section></div>`;
  }

  private searchResultsTemplate(): string {
    const query = this.searchQuery.trim().toLowerCase();
    const noun = this.catalog && isMediaCenterCatalog(this.catalog) ? "media library" : "channel library";
    if (!query) return `<p class="search-empty">Start typing to search your ${noun}.</p>`;
    const results = this.requireCatalog().channels
      .filter((channel) => channel.name.toLowerCase().includes(query) || channel.group.toLowerCase().includes(query))
      .slice(0, 8);
    if (results.length === 0) return `<p class="search-empty">No results match “${escapeHtml(this.searchQuery)}”.</p>`;
    return results.map((channel) => `<button class="search-result" data-action="search-result" data-channel-id="${escapeAttribute(channel.id)}" data-focusable="true"><strong>${escapeHtml(channel.name)}</strong><span>${escapeHtml(channel.group)}</span></button>`).join("");
  }

  private confirmClearTemplate(): string {
    return `<div class="modal-backdrop"><section class="modal modal-confirm" role="alertdialog" aria-modal="true" aria-labelledby="clear-title">
      <h2 id="clear-title">Remove this content source?</h2>
      <p>The private cached catalog and its protected device credential will be removed from this television. Your provider account is not changed.</p>
      <div class="modal-actions horizontal">
        <button class="button button-secondary" data-action="cancel-clear" data-focusable="true" data-autofocus="true">Keep source</button>
        <button class="button button-danger" data-action="confirm-clear" data-focusable="true">Remove source</button>
      </div>
    </section></div>`;
  }

  private playerTemplate(): string {
    const channel = this.selectedChannel();
    if (!channel) return this.loadingTemplate();
    const aspect = ASPECT_MODES[this.aspectIndex] ?? "Auto";
    return `<main class="player-screen" data-player-state="${this.playbackSignal.state}">
      <section id="player-surface" class="player-surface" aria-label="Playing ${escapeAttribute(channel.name)}">
        <video id="html-player" playsinline aria-label="Video playback for ${escapeAttribute(channel.name)}"></video>
        <object id="samsung-player" type="application/avplayer" hidden></object>
      </section>
      <div class="player-shade"></div>
      <header class="player-header player-chrome">
        <div><h1>${escapeHtml(channel.name)}</h1><p><i></i>${escapeHtml(channel.group)}</p></div>
        <span class="engine-label" data-role="engine">Native television player</span>
      </header>
      <div class="buffering-indicator" data-role="buffering" role="status" aria-live="polite" aria-atomic="true" hidden><span></span><strong>Buffering</strong></div>
      <div class="player-message" data-role="player-message" role="status" aria-live="polite" aria-atomic="true" hidden></div>
      <footer class="player-controls player-chrome">
        <button class="button player-control" data-action="toggle-playback" data-focusable="true">${icon("play")}<span>Play / pause</span></button>
        <button class="button player-control" data-action="cycle-aspect" data-focusable="true" aria-label="Change aspect ratio. Current ratio ${aspect}"><span>Aspect: <b data-role="aspect">${aspect}</b></span></button>
        <button class="button player-control" data-action="close-player" data-focusable="true">${icon("back")}<span>Back to channels</span></button>
      </footer>
    </main>`;
  }

  private async openPlayer(): Promise<void> {
    const channel = this.selectedChannel();
    if (!channel) return;
    this.screen = "player";
    this.modal = null;
    this.playbackSignal = { state: "opening", message: null, warning: null };
    this.render();
    const video = this.root.querySelector<HTMLVideoElement>("#html-player");
    const objectElement = this.root.querySelector<HTMLObjectElement>("#samsung-player");
    const surface = this.root.querySelector<HTMLElement>("#player-surface");
    if (!video || !objectElement || !surface) throw new Error("The native player surface could not be created.");
    this.player = createPlayerAdapter(video, objectElement, surface, this.updatePlaybackSignal);
    this.player.setAspect(ASPECT_MODES[this.aspectIndex] ?? "Auto");
    this.updateEngineLabel();
    this.navigator.setScope(this.root);
    this.focusAfterRender("[data-action='close-player']");
    await this.playResolvedChannel(channel);
  }

  private closePlayer(): void {
    this.playbackRequestSerial += 1;
    this.clearChromeTimer();
    this.player?.destroy();
    this.player = null;
    this.screen = "browse";
    this.playbackSignal = { state: "idle", message: null, warning: null };
    this.render();
    this.focusAfterRender(`[data-channel-id='${cssEscape(this.selectedChannelId ?? "")}']`);
  }

  private readonly updatePlaybackSignal = (signal: PlaybackSignal): void => {
    this.playbackSignal = signal;
    const screen = this.root.querySelector<HTMLElement>(".player-screen");
    const buffering = this.root.querySelector<HTMLElement>("[data-role='buffering']");
    const message = this.root.querySelector<HTMLElement>("[data-role='player-message']");
    if (!screen || !buffering || !message) return;
    screen.dataset.playerState = signal.state;
    buffering.hidden = signal.state !== "buffering";
    const copy = signal.message ?? signal.warning;
    message.hidden = !copy;
    message.textContent = copy ?? "";
    message.classList.toggle("is-error", Boolean(signal.message));
    if (signal.state === "playing") this.scheduleChromeHide();
    else this.showPlayerChrome();
  };

  private applyCatalog(loaded: CatalogLoadResult): void {
    this.catalog = loaded.catalog;
    this.notice = loaded.notice;
    this.error = null;
    this.screen = "browse";
    this.modal = null;
    const first = loaded.catalog.channels[0];
    this.selectedChannelId = first?.id ?? null;
    this.selectedGroup = ALL_GROUPS;
    this.scheduleNoticeDismiss();
  }

  private async connectUrl(inputId: string): Promise<void> {
    const input = this.root.querySelector<HTMLInputElement>(`#${inputId}`);
    if (!input) return;
    this.setBusy(true);
    this.error = null;
    try {
      const loaded = await this.repository.connectUrl(input.value);
      this.applyCatalog(loaded);
      this.render();
      this.focusAfterRender();
    } catch (error) {
      this.error = readableError(error, "The playlist could not be connected.");
      this.setBusy(false);
      this.render();
      this.focusAfterRender(`#${inputId}`);
    }
  }

  private async connectPlex(): Promise<void> {
    const server = this.inputValue("media-server");
    const accessToken = this.inputValue("plex-access", false);
    const displayName = this.inputValue("media-name");
    const allowInsecureHttp = this.checkboxValue("allow-media-http");
    this.setBusy(true);
    this.error = null;
    try {
      const loaded = await this.repository.connectPlex({
        serverAddress: server,
        accessToken,
        ...(displayName ? { displayName } : {}),
        allowInsecureHttp
      });
      this.applyCatalog(loaded);
      this.render();
      this.focusAfterRender();
    } catch (error) {
      this.error = readableError(error, "Plex could not be connected.");
      this.setBusy(false);
      this.render();
      this.focusAfterRender("#media-server");
    }
  }

  private async connectEmby(): Promise<void> {
    const server = this.inputValue("media-server");
    const username = this.inputValue("emby-user");
    const password = this.inputValue("emby-password", false);
    const displayName = this.inputValue("media-name");
    const allowInsecureHttp = this.checkboxValue("allow-media-http");
    this.setBusy(true);
    this.error = null;
    try {
      const loaded = await this.repository.connectEmby({
        serverAddress: server,
        username,
        password,
        ...(displayName ? { displayName } : {}),
        allowInsecureHttp
      });
      this.applyCatalog(loaded);
      this.render();
      this.focusAfterRender();
    } catch (error) {
      this.error = readableError(error, "Emby could not be connected.");
      this.setBusy(false);
      this.render();
      this.focusAfterRender("#media-server");
    }
  }

  private async refreshSource(): Promise<void> {
    this.setBusy(true);
    this.error = null;
    try {
      const loaded = await this.repository.refreshCurrent();
      this.applyCatalog(loaded);
      this.render();
      this.focusAfterRender();
    } catch (error) {
      this.error = readableError(error, "The active source could not be refreshed.");
      this.setBusy(false);
      this.render();
      this.focusAfterRender("[data-action='refresh-source']");
    }
  }

  private async buyPremium(): Promise<void> {
    this.error = null;
    try {
      await this.premiumService.purchase();
    } catch (error) {
      this.error = readableError(error, "Samsung Checkout could not finish the purchase.");
      this.render();
      this.focusAfterRender("[data-action='buy-premium']");
    }
  }

  private async restorePremium(): Promise<void> {
    this.error = null;
    try {
      await this.premiumService.refresh();
      this.render();
      this.focusAfterRender("[data-action='restore-premium']");
    } catch (error) {
      this.error = readableError(error, "The television store purchase could not be restored.");
      this.render();
      this.focusAfterRender("[data-action='restore-premium']");
    }
  }

  private async importFile(file: File): Promise<void> {
    this.setBusy(true);
    this.error = null;
    try {
      const loaded = await this.repository.importFile(file);
      this.applyCatalog(loaded);
      this.render();
      this.focusAfterRender();
    } catch (error) {
      this.error = readableError(error, "The playlist file could not be imported.");
      this.setBusy(false);
      this.render();
      this.focusAfterRender();
    }
  }

  private setBusy(busy: boolean): void {
    this.root.toggleAttribute("aria-busy", busy);
    this.root.querySelectorAll<HTMLButtonElement>("button").forEach((button) => { button.disabled = busy; });
  }

  private inputValue(id: string, trim = true): string {
    const value = this.root.querySelector<HTMLInputElement>(`#${id}`)?.value ?? "";
    return trim ? value.trim() : value;
  }

  private checkboxValue(id: string): boolean {
    return this.root.querySelector<HTMLInputElement>(`#${id}`)?.checked ?? false;
  }

  private async clearSource(): Promise<void> {
    await this.repository.clear();
    this.catalog = null;
    this.notice = null;
    this.modal = null;
    this.screen = "onboarding";
    this.render();
    this.focusAfterRender();
  }

  private selectGroup(group: string): void {
    this.selectedGroup = group;
    const channels = this.channelsForSelectedGroup();
    this.selectedChannelId = channels[0]?.id ?? null;
    this.render();
    this.focusAfterRender(`[data-group='${cssEscape(group)}']`);
  }

  private moveGroup(offset: number): void {
    const groups = this.groups();
    const current = Math.max(0, groups.indexOf(this.selectedGroup));
    const next = clamp(current + offset, 0, groups.length - 1);
    this.selectGroup(groups[next] ?? this.selectedGroup);
  }

  private moveChannel(offset: number, playImmediately = false): void {
    const channels = this.channelsForSelectedGroup();
    if (channels.length === 0) return;
    const current = Math.max(0, channels.findIndex((channel) => channel.id === this.selectedChannelId));
    const next = clamp(current + offset, 0, channels.length - 1);
    const channel = channels[next];
    if (!channel || channel.id === this.selectedChannelId) return;
    this.selectedChannelId = channel.id;
    if (playImmediately && this.screen === "player" && this.player) {
      this.playbackSignal = { state: "opening", message: null, warning: null };
      this.updatePlayerIdentity(channel);
      void this.playResolvedChannel(channel);
      return;
    }
    this.render();
    this.focusAfterRender(`[data-channel-id='${cssEscape(channel.id)}']`);
  }

  private toggleFavorite(): void {
    const channel = this.selectedChannel();
    if (!channel) return;
    if (this.favorites.has(channel.id)) this.favorites.delete(channel.id);
    else this.favorites.add(channel.id);
    saveFavorites(this.favorites);
    if (this.selectedGroup === FAVORITES_GROUP && !this.favorites.has(channel.id)) {
      this.selectedChannelId = this.channelsForSelectedGroup()[0]?.id ?? null;
    }
    this.render();
    this.focusAfterRender("[data-action='favorite']");
  }

  private openModal(modal: Exclude<Modal, null>): void {
    if (!this.modal) {
      const action = (document.activeElement as Element | null)?.closest<HTMLElement>("[data-action]")?.dataset.action;
      this.modalReturnFocusSelector = action ? `[data-action='${cssEscape(action)}']` : null;
    }
    this.modal = modal;
    this.error = null;
    this.render();
    const scope = this.modalElement();
    if (scope) this.navigator.setScope(scope);
    this.focusAfterRender();
  }

  private selectSourceMode(mode: SourceMode): void {
    this.sourceMode = mode;
    this.error = null;
    this.render();
    const scope = this.modalElement();
    if (scope) this.navigator.setScope(scope);
    this.focusAfterRender(`[data-source-mode='${mode}']`);
    if (mode !== "playlist") void this.premiumService.refresh();
  }

  private openSourceMode(mode: SourceMode): void {
    this.sourceMode = mode;
    this.openModal("source");
    if (mode !== "playlist") void this.premiumService.refresh();
  }

  private async playResolvedChannel(channel: CatalogChannel): Promise<void> {
    const serial = ++this.playbackRequestSerial;
    this.updatePlaybackSignal({ state: "opening", message: null, warning: null });
    try {
      const resolved = await this.repository.resolvePlayback(channel);
      if (serial !== this.playbackRequestSerial || !this.player || this.screen !== "player") return;
      await this.player.play(resolved.channel, resolved.startPositionMs);
    } catch (error) {
      if (serial !== this.playbackRequestSerial) return;
      this.updatePlaybackSignal({
        state: "error",
        message: readableError(error, "Playback could not be unlocked."),
        warning: null
      });
    }
  }

  private closeModal(): void {
    const returnFocusSelector = this.modalReturnFocusSelector;
    this.modal = null;
    this.modalReturnFocusSelector = null;
    this.error = null;
    this.render();
    this.navigator.setScope(this.root);
    this.focusAfterRender(returnFocusSelector ?? undefined);
  }

  private cycleAspect(): void {
    this.aspectIndex = (this.aspectIndex + 1) % ASPECT_MODES.length;
    const aspect = ASPECT_MODES[this.aspectIndex] ?? "Auto";
    this.player?.setAspect(aspect);
    const label = this.root.querySelector<HTMLElement>("[data-role='aspect']");
    if (label) label.textContent = aspect;
    const button = this.root.querySelector<HTMLElement>("[data-action='cycle-aspect']");
    button?.setAttribute("aria-label", `Change aspect ratio. Current ratio ${aspect}`);
    this.showPlayerChrome();
  }

  private readonly onClick = (event: MouseEvent): void => {
    const action = (event.target as Element | null)?.closest<HTMLElement>("[data-action]")?.dataset.action;
    const target = (event.target as Element | null)?.closest<HTMLElement>("[data-action]");
    if (!action || !target) return;
    if (action === "connect-url") void this.connectUrl("playlist-url");
    else if (action === "connect-source-url") void this.connectUrl("source-url");
    else if (action === "connect-plex") void this.connectPlex();
    else if (action === "connect-emby") void this.connectEmby();
    else if (action === "refresh-source") void this.refreshSource();
    else if (action === "buy-premium") void this.buyPremium();
    else if (action === "restore-premium") void this.restorePremium();
    else if (action === "open-source-mode") this.openSourceMode(sourceMode(target.dataset.sourceMode));
    else if (action === "select-source-mode") this.selectSourceMode(sourceMode(target.dataset.sourceMode));
    else if (action === "choose-file") this.root.querySelector<HTMLInputElement>("#playlist-file")?.click();
    else if (action === "use-demo") void this.repository.useDemo().then((loaded) => { this.applyCatalog(loaded); this.render(); this.focusAfterRender(); });
    else if (action === "open-search") this.openModal("search");
    else if (action === "open-source") this.openModal("source");
    else if (action === "close-modal") this.closeModal();
    else if (action === "request-clear") this.openModal("confirm-clear");
    else if (action === "cancel-clear") this.openModal("source");
    else if (action === "confirm-clear") void this.clearSource();
    else if (action === "select-group") this.selectGroup(target.dataset.group ?? ALL_GROUPS);
    else if (action === "select-channel") {
      this.selectedChannelId = target.dataset.channelId ?? this.selectedChannelId;
      void this.openPlayer();
    } else if (action === "watch") void this.openPlayer();
    else if (action === "favorite") this.toggleFavorite();
    else if (action === "search-result") {
      const channel = this.requireCatalog().channels.find((item) => item.id === target.dataset.channelId);
      if (channel) {
        this.selectedGroup = ALL_GROUPS;
        this.selectedChannelId = channel.id;
        this.closeModal();
      }
    } else if (action === "toggle-playback") this.player?.toggle();
    else if (action === "cycle-aspect") this.cycleAspect();
    else if (action === "close-player") this.closePlayer();
  };

  private readonly onPremiumChanged = (snapshot: TelevisionPremiumSnapshot): void => {
    const hadAccess = this.premium.access.canUseMediaCenters;
    this.premium = snapshot;
    if (!this.startupComplete) {
      this.render();
      return;
    }
    void this.reconcilePremiumAccess(hadAccess, snapshot.access.canUseMediaCenters);
  };

  private async reconcilePremiumAccess(hadAccess: boolean, hasAccess: boolean): Promise<void> {
    if (hadAccess && !hasAccess && this.catalog && isMediaCenterCatalog(this.catalog)) {
      this.playbackRequestSerial += 1;
      this.player?.destroy();
      this.player = null;
      this.catalog = null;
      this.screen = "onboarding";
      this.modal = null;
      this.error = "Premium ownership is no longer verified. Protected media playback has stopped.";
      this.render();
      this.focusAfterRender();
      return;
    }

    if (!hadAccess && hasAccess && !this.catalog) {
      try {
        const loaded = await this.repository.loadSaved();
        if (loaded) this.applyCatalog(loaded);
      } catch (error) {
        this.error = readableError(error, "The saved media library could not be restored.");
      }
    }
    this.render();
    this.focusAfterRender();
  }

  private readonly onWindowFocus = (): void => {
    if (this.startupComplete) void this.premiumService.refresh();
  };

  private readonly onVisibilityChange = (): void => {
    if (this.startupComplete && document.visibilityState === "visible") {
      void this.premiumService.refresh();
    }
  };

  private readonly onFocusIn = (event: FocusEvent): void => {
    const target = (event.target as Element | null)?.closest<HTMLElement>("[data-channel-id]");
    const channelId = target?.dataset.channelId;
    if (!channelId || this.screen !== "browse" || this.modal) return;
    this.selectedChannelId = channelId;
    this.updateBrowseSelection();
  };

  private readonly onInput = (event: Event): void => {
    const input = event.target as HTMLInputElement | null;
    if (input?.id !== "channel-search") return;
    this.searchQuery = input.value;
    const results = this.root.querySelector<HTMLElement>("[data-role='search-results']");
    if (results) results.innerHTML = this.searchResultsTemplate();
  };

  private readonly onChange = (event: Event): void => {
    const input = event.target as HTMLInputElement | null;
    if (input?.id !== "playlist-file") return;
    const file = input.files?.[0];
    if (file) void this.importFile(file);
  };

  private readonly onAppKeyDown = (event: KeyboardEvent): void => {
    if (this.screen === "player") {
      this.showPlayerChrome();
      if (event.key === "ArrowUp" || event.keyCode === 427) {
        event.preventDefault();
        event.stopImmediatePropagation();
        this.moveChannel(-1, true);
      } else if (event.key === "ArrowDown" || event.keyCode === 428) {
        event.preventDefault();
        event.stopImmediatePropagation();
        this.moveChannel(1, true);
      } else if ([19, 415, 10252].includes(event.keyCode) || event.key === "MediaPlayPause") {
        event.preventDefault();
        event.stopImmediatePropagation();
        this.player?.toggle();
      } else if (event.keyCode === 413 || event.key === "MediaStop") {
        event.preventDefault();
        event.stopImmediatePropagation();
        this.closePlayer();
      }
      return;
    }
    if (this.screen !== "browse" || this.modal || isTextEntry(document.activeElement)) return;
    const active = document.activeElement as HTMLElement | null;
    if (active?.dataset.channelId && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
      event.preventDefault();
      event.stopImmediatePropagation();
      this.moveChannel(event.key === "ArrowUp" ? -1 : 1);
    } else if (active?.dataset.channelId && event.key === "ArrowLeft") {
      event.preventDefault();
      event.stopImmediatePropagation();
      this.navigator.focusElement(this.root.querySelector(`[data-group='${cssEscape(this.selectedGroup)}']`));
    } else if (active?.dataset.group && (event.key === "ArrowUp" || event.key === "ArrowDown")) {
      event.preventDefault();
      event.stopImmediatePropagation();
      this.moveGroup(event.key === "ArrowUp" ? -1 : 1);
    } else if (active?.dataset.group && event.key === "ArrowRight") {
      event.preventDefault();
      event.stopImmediatePropagation();
      this.navigator.focusElement(this.root.querySelector(`[data-channel-id='${cssEscape(this.selectedChannelId ?? "")}']`));
    }
  };

  private readonly handleBack = (): boolean => {
    if (this.screen === "player") {
      this.closePlayer();
      return true;
    }
    if (this.modal) {
      this.closeModal();
      return true;
    }
    if (this.screen === "browse") return exitTelevisionApp();
    return false;
  };

  private updateBrowseSelection(): void {
    const channel = this.selectedChannel();
    if (!channel) return;
    this.root.querySelectorAll<HTMLElement>(".channel-row").forEach((row) => {
      const selected = row.dataset.channelId === channel.id;
      row.classList.toggle("is-selected", selected);
      row.setAttribute("aria-current", String(selected));
    });
    const detail = this.root.querySelector<HTMLElement>("[data-role='details']");
    if (detail) detail.innerHTML = this.detailTemplate(channel);
  }

  private updatePlayerIdentity(channel: CatalogChannel): void {
    const header = this.root.querySelector<HTMLElement>(".player-header > div");
    if (header) header.innerHTML = `<h1>${escapeHtml(channel.name)}</h1><p><i></i>${escapeHtml(channel.group)}</p>`;
  }

  private updateEngineLabel(): void {
    const label = this.root.querySelector<HTMLElement>("[data-role='engine']");
    if (label && this.player) label.textContent = this.player.kind === "samsung-avplay" ? "Samsung AVPlay" : "Native television video";
  }

  private showPlayerChrome(): void {
    const screen = this.root.querySelector<HTMLElement>(".player-screen");
    screen?.classList.remove("controls-hidden");
    this.scheduleChromeHide();
  }

  private scheduleChromeHide(): void {
    this.clearChromeTimer();
    if (this.playbackSignal.state !== "playing") return;
    this.hideChromeTimer = window.setTimeout(() => {
      this.root.querySelector<HTMLElement>(".player-screen")?.classList.add("controls-hidden");
    }, 4_000);
  }

  private clearChromeTimer(): void {
    if (this.hideChromeTimer !== null) window.clearTimeout(this.hideChromeTimer);
    this.hideChromeTimer = null;
  }

  private scheduleNoticeDismiss(): void {
    this.clearNoticeTimer();
    if (!this.notice) return;
    this.noticeTimer = window.setTimeout(() => {
      this.notice = null;
      this.root.querySelector<HTMLElement>(".notice")?.remove();
      this.noticeTimer = null;
    }, 4_500);
  }

  private clearNoticeTimer(): void {
    if (this.noticeTimer !== null) window.clearTimeout(this.noticeTimer);
    this.noticeTimer = null;
  }

  private groups(): string[] {
    const groups = [...new Set(this.requireCatalog().channels.map((channel) => channel.group))];
    return isMediaCenterCatalog(this.requireCatalog())
      ? [ALL_GROUPS, FAVORITES_GROUP, ...MEDIA_BROWSE_GROUPS, ...groups]
      : [ALL_GROUPS, FAVORITES_GROUP, ...groups];
  }

  private groupCount(group: string): number {
    if (group === ALL_GROUPS) return this.requireCatalog().channels.length;
    if (group === FAVORITES_GROUP) return this.requireCatalog().channels.filter((channel) => this.favorites.has(channel.id)).length;
    const mode = mediaBrowseModeForGroup(group);
    if (mode) return this.requireCatalog().channels.filter((channel) => matchesMediaLibraryBrowseMode(channel, mode)).length;
    return this.requireCatalog().channels.filter((channel) => channel.group === group).length;
  }

  private channelsForSelectedGroup(): CatalogChannel[] {
    const channels = this.requireCatalog().channels;
    if (this.selectedGroup === ALL_GROUPS) return channels;
    if (this.selectedGroup === FAVORITES_GROUP) return channels.filter((channel) => this.favorites.has(channel.id));
    const mode = mediaBrowseModeForGroup(this.selectedGroup);
    if (mode) {
      return orderMediaLibraryChannels(
        channels.filter((channel) => matchesMediaLibraryBrowseMode(channel, mode)),
        mode
      );
    }
    return channels.filter((channel) => channel.group === this.selectedGroup);
  }

  private selectedChannel(): CatalogChannel | null {
    if (!this.catalog || !this.selectedChannelId) return null;
    return this.catalog.channels.find((channel) => channel.id === this.selectedChannelId) ?? null;
  }

  private ensureSelectedChannel(channels: CatalogChannel[]): CatalogChannel | null {
    const selected = channels.find((channel) => channel.id === this.selectedChannelId) ?? channels[0] ?? null;
    this.selectedChannelId = selected?.id ?? null;
    return selected;
  }

  private requireCatalog(): StreamVueCatalog {
    if (!this.catalog) throw new Error("No channel catalog is connected.");
    return this.catalog;
  }

  private modalElement(): HTMLElement | null {
    return this.root.querySelector<HTMLElement>(".modal");
  }

  private focusAfterRender(selector?: string): void {
    window.setTimeout(() => {
      if (selector) this.navigator.focusElement(this.root.querySelector<HTMLElement>(selector));
      else this.navigator.focus();
    }, 0);
  }
}

function groupButton(group: string, active: boolean, count: number, mediaCenter: boolean): string {
  const iconName: IconName = group === ALL_GROUPS ? "grid"
    : group === FAVORITES_GROUP ? "favorite"
      : group === CONTINUE_WATCHING_GROUP ? "play"
        : group === RECENTLY_ADDED_GROUP ? "news"
          : group === LIVE_MEDIA_GROUP ? "sports"
            : group === MOVIES_GROUP || group === SERIES_GROUP ? "film"
      : /movie|cinema/i.test(group) ? "film"
        : /news/i.test(group) ? "news"
          : /sport/i.test(group) ? "sports"
            : "folder";
  const label = groupLabel(group, mediaCenter);
  return `<button class="group-button${active ? " is-active" : ""}" data-action="select-group" data-group="${escapeAttribute(group)}" data-focusable="true" aria-pressed="${active}">${icon(iconName)}<span>${escapeHtml(label)}</span><small>${count.toLocaleString()}</small></button>`;
}

function sourceTab(mode: SourceMode, label: string, active: SourceMode): string {
  return `<button id="source-tab-${mode}" class="source-tab${mode === active ? " is-active" : ""}" role="tab" aria-selected="${mode === active}" aria-controls="source-panel-${mode}" data-action="select-source-mode" data-source-mode="${mode}" data-focusable="true">${escapeHtml(label)}</button>`;
}

function sourceMode(value: string | undefined): SourceMode {
  return value === "plex" || value === "emby" ? value : "playlist";
}

function mediaKindLabel(channel: CatalogChannel): string {
  switch (channel.kind) {
  case "movie": return "Movie";
  case "series": return "Series episode";
  case "recording": return "Recording";
  case "replay": return "Replay";
  case "live": return "Live television";
  }
}

function channelButton(channel: CatalogChannel, selected: boolean, favorite: boolean): string {
  const initials = channelInitials(channel.name);
  const logo = safeImageUrl(channel.guide?.logoUri);
  const metadata = mediaMetadataLine(channel);
  const progress = watchProgressPercent(channel);
  return `<button class="channel-row${selected ? " is-selected" : ""}" data-action="select-channel" data-channel-id="${escapeAttribute(channel.id)}" data-focusable="true"${selected ? " data-autofocus='true'" : ""} aria-current="${selected}">
    <span class="channel-mark" data-tone="${channel.number % 5}">${logo ? `<img src="${escapeAttribute(logo)}" alt="" onerror="this.hidden=true" />` : ""}<b>${escapeHtml(initials)}</b></span>
    <span class="channel-copy"><span class="channel-name">${escapeHtml(channel.name)}</span>${metadata ? `<small>${escapeHtml(metadata)}</small>` : ""}${progress === null ? "" : `<span class="channel-progress" aria-hidden="true"><i style="width:${progress}%"></i></span>`}</span>
    ${favorite ? `<span class="favorite-mark">${icon("favorite")}</span>` : ""}
    <span class="live-mark"><i></i>${channel.kind === "live" ? "LIVE" : channel.kind.toUpperCase()}</span>
  </button>`;
}

function mediaBrowseModeForGroup(group: string): MediaLibraryBrowseMode | null {
  switch (group) {
  case CONTINUE_WATCHING_GROUP: return "continue-watching";
  case RECENTLY_ADDED_GROUP: return "recently-added";
  case LIVE_MEDIA_GROUP: return "live";
  case MOVIES_GROUP: return "movies";
  case SERIES_GROUP: return "series";
  default: return null;
  }
}

function groupLabel(group: string, mediaCenter: boolean): string {
  switch (group) {
  case ALL_GROUPS: return mediaCenter ? "All Media" : ALL_GROUPS;
  case CONTINUE_WATCHING_GROUP: return "Continue Watching";
  case RECENTLY_ADDED_GROUP: return "Recently Added";
  case LIVE_MEDIA_GROUP: return "Live TV";
  case MOVIES_GROUP: return "Movies";
  case SERIES_GROUP: return "Series";
  default: return group;
  }
}

function mediaMetadataLine(channel: CatalogChannel): string | null {
  const metadata = channel.media;
  if (!metadata) return null;
  const values: string[] = [];
  if (metadata.seriesTitle) values.push(metadata.seriesTitle);
  if (metadata.seasonNumber !== undefined && metadata.episodeNumber !== undefined) {
    values.push(`S${String(metadata.seasonNumber).padStart(2, "0")}E${String(metadata.episodeNumber).padStart(2, "0")}`);
  }
  if (metadata.year !== undefined) values.push(String(metadata.year));
  if (values.length === 0 && metadata.libraryTitle) values.push(metadata.libraryTitle);
  return values.length === 0 ? null : values.join(" • ");
}

function watchProgressPercent(channel: CatalogChannel): number | null {
  if (!canResumeMedia(channel)) return null;
  const duration = channel.media?.durationMs ?? 0;
  const position = channel.media?.resumePositionMs ?? 0;
  if (duration <= 0) return null;
  return clamp(Math.round((position / duration) * 100), 0, 100);
}

function brandMark(): string {
  return `<div class="brand" aria-label="OrbitalVue"><span>ORBITAL</span><b>VUE</b></div>`;
}

function sourceStatus(catalog: StreamVueCatalog): string {
  if (catalog.catalogId === "streamvue-demo") return "Source refreshed 2m ago";
  const loaded = Date.parse(catalog.loadedAt);
  const ageMinutes = Math.max(0, Math.round((Date.now() - loaded) / 60_000));
  if (ageMinutes < 1) return "Source refreshed just now";
  if (ageMinutes < 60) return `Source refreshed ${ageMinutes}m ago`;
  const hours = Math.round(ageMinutes / 60);
  return `Source refreshed ${hours}h ago`;
}

function channelInitials(name: string): string {
  const words = name.replace(/[^A-Za-z0-9 ]/g, " ").split(/\s+/).filter(Boolean);
  return (words.length > 1 ? `${words[0]?.[0] ?? ""}${words[1]?.[0] ?? ""}` : words[0]?.slice(0, 2) ?? "TV").toUpperCase();
}

function safeImageUrl(value: string | undefined): string | null {
  if (!value) return null;
  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.toString() : null;
  } catch {
    return null;
  }
}

function windowAround<T>(items: T[], selectedIndex: number, size: number): { items: T[]; start: number } {
  if (items.length <= size) return { items, start: 0 };
  const safeIndex = clamp(selectedIndex, 0, items.length - 1);
  const start = clamp(safeIndex - Math.floor(size / 2), 0, items.length - size);
  return { items: items.slice(start, start + size), start };
}

function channelWindowSize(): number {
  return window.innerHeight < 900 ? 4 : 5;
}

function loadFavorites(): Set<string> {
  try {
    const parsed = JSON.parse(localStorage.getItem(FAVORITES_KEY) ?? "[]") as unknown;
    return new Set(Array.isArray(parsed) ? parsed.filter((value): value is string => typeof value === "string") : []);
  } catch {
    return new Set();
  }
}

function saveFavorites(favorites: Set<string>): void {
  try {
    localStorage.setItem(FAVORITES_KEY, JSON.stringify([...favorites]));
  } catch {
    // Favorites remain active for the session if television storage is unavailable.
  }
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;"
  })[character] ?? character);
}

function escapeAttribute(value: string): string {
  return escapeHtml(value).replace(/`/g, "&#96;");
}

function cssEscape(value: string): string {
  return typeof CSS !== "undefined" && CSS.escape ? CSS.escape(value) : value.replace(/['"\\]/g, "\\$&");
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

function readableError(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}

function isTextEntry(element: Element | null): boolean {
  return element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement;
}
