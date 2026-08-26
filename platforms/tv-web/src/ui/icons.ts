export type IconName = "grid" | "favorite" | "folder" | "sports" | "film" | "news" | "search" | "settings" | "play" | "back";

export function icon(name: IconName): string {
  const paths: Record<IconName, string> = {
    grid: '<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>',
    favorite: '<path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3l-5.6 2.9 1.1-6.2L3 9.6l6.2-.9L12 3Z"/>',
    folder: '<path d="M3 7.5h7l2-2h9v13H3z"/><path d="M3 9.5h18"/>',
    sports: '<circle cx="12" cy="12" r="9"/><path d="M8.2 4.1c2.1 2.4 3.3 5.1 3.6 7.9.3 2.9-.5 5.5-2.3 8M15.8 4.1c-2.1 2.4-3.3 5.1-3.6 7.9-.3 2.9.5 5.5 2.3 8M3.4 9.2c2.7.8 5.6 1 8.6.7 3-.3 5.9-1.1 8.6-2.4M3.5 15c2.7-.8 5.6-1 8.5-.7 3 .3 5.9 1.1 8.6 2.4"/>',
    film: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m9.5 9 6 3-6 3zM6 3v4M18 3v4"/>',
    news: '<rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 7h8M8 11h8M8 15h5"/>',
    search: '<circle cx="11" cy="11" r="7"/><path d="m16.2 16.2 4.3 4.3"/>',
    settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-1.6v-.2h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z"/>',
    play: '<path class="fill" d="m8 5 11 7-11 7z"/>',
    back: '<path d="m15 18-6-6 6-6"/>'
  };
  return `<svg class="icon icon-${name}" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">${paths[name]}</svg>`;
}
