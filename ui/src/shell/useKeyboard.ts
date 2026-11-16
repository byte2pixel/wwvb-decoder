import { useEffect } from "react";
import { useAppStore } from "@/state/store";
import { keyAction } from "./keyboard";
import { toggleTheme } from "./theme";

/** True when a key pressed here belongs to the control: a text field, a list, or a slider, which is an `input` too. */
const takesKeys = (target: EventTarget | null): boolean =>
  target instanceof Element &&
  target.closest("input, textarea, select") !== null;

/** Runs the keyboard map for the whole page while the component is mounted. */
export function useKeyboard(): void {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      // Ctrl+L and the like are the browser's, and a held key acts once.
      if (event.ctrlKey || event.altKey || event.metaKey || event.repeat) {
        return;
      }
      if (takesKeys(event.target)) return;

      const { focus, drawer, setFocus, setDrawer } = useAppStore.getState();
      const action = keyAction(event.key, { focus, drawer });
      if (!action) return;

      event.preventDefault();
      // The key was for the page, so the control with focus gives it up. A clicked
      // button keeps focus, and the browser would draw its focus ring on this key press.
      document.querySelector<HTMLElement>(":focus")?.blur();
      switch (action.kind) {
        case "focus":
          setFocus(action.stage);
          break;
        case "drawer":
          setDrawer(action.drawer);
          break;
        case "theme":
          toggleTheme();
          break;
      }
    };

    // In the capture phase, so the map runs before a drawer's own Esc closes it
    // and still finds the drawer open.
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, []);
}
