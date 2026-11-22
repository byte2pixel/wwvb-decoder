import { flushSync } from "react-dom";
import { useAppStore } from "@/state/store";

/** True when the viewer has asked the system for less motion. */
export const prefersReducedMotion = (): boolean =>
  globalThis.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;

/**
 * Makes a change to the page inside a view transition: the browser takes a picture before and
 * after `update` and animates from one to the other. The change is made at once, with no
 * animation, where there is no View Transitions API and when the viewer asked for less motion.
 */
export function withViewTransition(update: () => void): void {
  if (!("startViewTransition" in document) || prefersReducedMotion()) {
    update();
    return;
  }

  // The browser needs the new picture when its callback returns, so React renders in it.
  const transition = document.startViewTransition(() => flushSync(update));
  // A second change inside the 200 ms skips this one, which is not a fault to report.
  transition.ready.catch(() => undefined);
}

/**
 * Opens a stage's focus view, or the overview for null, as a view transition. Asking for the
 * view that is on show does nothing, so no transition runs between two equal pictures.
 */
export function focusStage(stage: number | null): void {
  const { focus, setFocus } = useAppStore.getState();
  if (stage === focus) return;

  withViewTransition(() => setFocus(stage));
}
