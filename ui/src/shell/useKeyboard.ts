import { useEffect } from "react";
import type { Bridge } from "@/protocol/bridge";
import { useAppStore } from "@/state/store";
import { keyAction } from "./keyboard";
import { noiseDemo } from "./noiseDemo";
import { toggleTheme } from "./theme";

/** True when a key pressed here belongs to the control: a text field, a list, or a slider, which is an `input` too. */
const takesKeys = (target: EventTarget | null): boolean =>
  target instanceof Element &&
  target.closest(
    'input, textarea, select, [data-slot="select-content"], [data-slot="popover-content"], [aria-haspopup][aria-expanded="true"]',
  ) !== null;

/**
 * Runs the keyboard map for the whole page while the component is mounted. The bridge is for
 * the one key that changes what the host runs, `D`.
 */
export function useKeyboard(bridge: Bridge): void {
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
      event.stopPropagation();
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
        case "noiseDemo": {
          // The same two steps as the app bar's own controls: the pick, then the command.
          const { sourceStatus, picks, setPicks } = useAppStore.getState();
          const demo = noiseDemo(
            sourceStatus?.source === "synthetic" &&
              sourceStatus.state === "running",
            picks.noise,
          );
          setPicks(demo.picks);
          bridge.send(demo.command);
          break;
        }
      }
    };

    // In the capture phase, so the map runs before a drawer's own Esc closes it
    // and still finds the drawer open.
    window.addEventListener("keydown", onKeyDown, true);
    return () => window.removeEventListener("keydown", onKeyDown, true);
  }, [bridge]);
}
