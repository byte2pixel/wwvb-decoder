import type { Drawer, UiState } from "@/state/store";
import { stages } from "./stages";

/** What a key does to the page. */
export type KeyAction =
  | { kind: "focus"; stage: number | null }
  | { kind: "drawer"; drawer: Drawer | null }
  | { kind: "theme" };

/** The arrow keys walk a row of eight places: the overview, then the seven stages. */
const OVERVIEW = 0;

const focusAt = (place: number): KeyAction => ({
  kind: "focus",
  stage: place === OVERVIEW ? null : place,
});

const toggle = (drawer: Drawer, open: Drawer | null): KeyAction => ({
  kind: "drawer",
  drawer: open === drawer ? null : drawer,
});

/**
 * The keyboard map: what a key does, given what is on screen, or null when it does nothing.
 * `key` is a KeyboardEvent's `key`. See "Keyboard" in docs/design/ui-design.md.
 */
export function keyAction(
  key: string,
  { focus, drawer }: UiState,
): KeyAction | null {
  switch (key) {
    case "l":
    case "L":
      return toggle("log", drawer);
    case "?":
      return toggle("shortcuts", drawer);
  }

  // An open drawer has the keyboard. Its own Esc closes it, and the page behind keeps its view.
  if (drawer !== null) return null;

  const place = focus ?? OVERVIEW;
  switch (key) {
    case "Escape":
    case "0":
      return focusAt(OVERVIEW);
    case "ArrowRight":
    case "PageDown":
      return focusAt(Math.min(place + 1, stages.length));
    case "ArrowLeft":
    case "PageUp":
      return focusAt(Math.max(place - 1, OVERVIEW));
    case "t":
    case "T":
      return { kind: "theme" };
  }

  const stage = stages.find(({ number }) => String(number) === key);
  return stage ? focusAt(stage.number) : null;
}
