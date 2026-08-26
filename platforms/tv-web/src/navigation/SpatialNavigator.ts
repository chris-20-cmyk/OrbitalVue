export type Direction = "up" | "down" | "left" | "right";

export interface RectLike {
  left: number;
  top: number;
  right: number;
  bottom: number;
  width: number;
  height: number;
}

const BACK_KEY_CODES = new Set([10009, 461]);

function centerX(rect: RectLike): number {
  return rect.left + rect.width / 2;
}

function centerY(rect: RectLike): number {
  return rect.top + rect.height / 2;
}

export function chooseNextIndex(rectangles: RectLike[], currentIndex: number, direction: Direction): number {
  const current = rectangles[currentIndex];
  if (!current) return currentIndex;
  let bestIndex = currentIndex;
  let bestScore = Number.POSITIVE_INFINITY;

  rectangles.forEach((candidate, index) => {
    if (index === currentIndex) return;
    const horizontal = centerX(candidate) - centerX(current);
    const vertical = centerY(candidate) - centerY(current);
    const primary = direction === "left" ? -horizontal
      : direction === "right" ? horizontal
        : direction === "up" ? -vertical
          : vertical;
    if (primary <= 1) return;
    const secondary = direction === "left" || direction === "right" ? Math.abs(vertical) : Math.abs(horizontal);
    const alignmentPenalty = secondary > primary * 1.8 ? secondary * 4 : secondary;
    const score = primary * 1000 + alignmentPenalty * 8 + Math.hypot(horizontal, vertical);
    if (score < bestScore) {
      bestScore = score;
      bestIndex = index;
    }
  });
  return bestIndex;
}

export class SpatialNavigator {
  private scope: HTMLElement;

  constructor(
    scope: HTMLElement,
    private readonly onBack: () => boolean
  ) {
    this.scope = scope;
    window.addEventListener("keydown", this.onKeyDown, { capture: true });
    this.scope.addEventListener("pointerover", this.onPointerOver);
  }

  setScope(scope: HTMLElement): void {
    this.scope.removeEventListener("pointerover", this.onPointerOver);
    this.scope = scope;
    this.scope.addEventListener("pointerover", this.onPointerOver);
  }

  focus(selector = "[data-autofocus='true'], [data-focusable='true']"): void {
    const element = selector === "[data-autofocus='true'], [data-focusable='true']"
      ? this.scope.querySelector<HTMLElement>("[data-autofocus='true']")
        ?? this.scope.querySelector<HTMLElement>("[data-focusable='true']")
      : this.scope.querySelector<HTMLElement>(selector);
    element?.focus({ preventScroll: true });
  }

  focusElement(element: HTMLElement | null): void {
    element?.focus({ preventScroll: true });
    element?.scrollIntoView({ block: "nearest", inline: "nearest" });
  }

  destroy(): void {
    window.removeEventListener("keydown", this.onKeyDown, { capture: true });
    this.scope.removeEventListener("pointerover", this.onPointerOver);
  }

  private readonly onPointerOver = (event: PointerEvent): void => {
    const target = (event.target as Element | null)?.closest<HTMLElement>("[data-focusable='true']");
    if (target) target.focus({ preventScroll: true });
  };

  private readonly onKeyDown = (event: KeyboardEvent): void => {
    if (event.defaultPrevented) return;
    if (event.key === "Escape" || event.key === "BrowserBack" || BACK_KEY_CODES.has(event.keyCode)) {
      if (this.onBack()) event.preventDefault();
      return;
    }
    if (event.key === "Enter") {
      const active = document.activeElement as HTMLElement | null;
      if (active?.dataset.focusable === "true") {
        event.preventDefault();
        active.click();
      }
      return;
    }
    if (isTextEntry(document.activeElement)) return;

    const direction = keyDirection(event.key);
    if (!direction) return;
    const focusable = this.focusableElements();
    const current = document.activeElement as HTMLElement | null;
    const currentIndex = current ? focusable.indexOf(current) : -1;
    if (currentIndex < 0) {
      this.focusElement(focusable[0] ?? null);
      event.preventDefault();
      return;
    }
    const nextIndex = chooseNextIndex(focusable.map((element) => element.getBoundingClientRect()), currentIndex, direction);
    if (nextIndex !== currentIndex) {
      event.preventDefault();
      this.focusElement(focusable[nextIndex] ?? null);
    }
  };

  private focusableElements(): HTMLElement[] {
    return [...this.scope.querySelectorAll<HTMLElement>("[data-focusable='true']")].filter((element) => {
      const bounds = element.getBoundingClientRect();
      return !element.hidden && !element.hasAttribute("disabled") && bounds.width > 0 && bounds.height > 0;
    });
  }
}

function keyDirection(key: string): Direction | null {
  if (key === "ArrowUp") return "up";
  if (key === "ArrowDown") return "down";
  if (key === "ArrowLeft") return "left";
  if (key === "ArrowRight") return "right";
  return null;
}

function isTextEntry(element: Element | null): boolean {
  return element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement;
}
