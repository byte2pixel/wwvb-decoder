import { describe, expect, it } from "vitest";
import type { UiState } from "@/state/store";
import { keyAction } from "./keyboard";

const overview: UiState = { focus: null, drawer: null };
const onStage = (focus: number): UiState => ({ focus, drawer: null });

describe("keyAction", () => {
  it.each(["1", "2", "3", "4", "5", "6", "7"])(
    "focuses stage %s on its number",
    (key) => {
      expect(keyAction(key, overview)).toEqual({
        kind: "focus",
        stage: Number(key),
      });
    },
  );

  it("stays on a stage when its number is pressed again", () => {
    expect(keyAction("3", onStage(3))).toEqual({ kind: "focus", stage: 3 });
  });

  it.each(["Escape", "0"])("goes back to the overview on %s", (key) => {
    expect(keyAction(key, onStage(4))).toEqual({ kind: "focus", stage: null });
  });

  it.each(["ArrowRight", "PageDown"])(
    "walks forward on %s: the overview, then stage 1 to stage 7, and no further",
    (key) => {
      expect(keyAction(key, overview)).toEqual({ kind: "focus", stage: 1 });
      expect(keyAction(key, onStage(3))).toEqual({ kind: "focus", stage: 4 });
      expect(keyAction(key, onStage(7))).toEqual({ kind: "focus", stage: 7 });
    },
  );

  it.each(["ArrowLeft", "PageUp"])(
    "walks back on %s: down to stage 1, then the overview, and no further",
    (key) => {
      expect(keyAction(key, onStage(3))).toEqual({ kind: "focus", stage: 2 });
      expect(keyAction(key, onStage(1))).toEqual({
        kind: "focus",
        stage: null,
      });
      expect(keyAction(key, overview)).toEqual({ kind: "focus", stage: null });
    },
  );

  it.each(["t", "T"])("switches the theme on %s", (key) => {
    expect(keyAction(key, onStage(2))).toEqual({ kind: "theme" });
  });

  it.each(["d", "D"])("asks for the noise demo on %s", (key) => {
    expect(keyAction(key, onStage(2))).toEqual({ kind: "noiseDemo" });
  });

  it.each(["l", "L"])("opens and closes the protocol log on %s", (key) => {
    expect(keyAction(key, overview)).toEqual({ kind: "drawer", drawer: "log" });
    expect(keyAction(key, { focus: null, drawer: "log" })).toEqual({
      kind: "drawer",
      drawer: null,
    });
  });

  it("opens and closes the shortcut list on ?", () => {
    expect(keyAction("?", overview)).toEqual({
      kind: "drawer",
      drawer: "shortcuts",
    });
    expect(keyAction("?", { focus: null, drawer: "shortcuts" })).toEqual({
      kind: "drawer",
      drawer: null,
    });
  });

  it("goes from one drawer to the other", () => {
    expect(keyAction("?", { focus: null, drawer: "log" })).toEqual({
      kind: "drawer",
      drawer: "shortcuts",
    });
  });

  it.each(["2", "0", "Escape", "ArrowRight", "PageUp", "t", "d"])(
    "leaves the page behind an open drawer alone on %s",
    (key) => {
      expect(keyAction(key, { focus: 5, drawer: "log" })).toBeNull();
    },
  );

  it.each(["8", "9", "a", " ", "Enter", "Tab", "ArrowDown", "F11", "!"])(
    "does nothing on %j",
    (key) => {
      expect(keyAction(key, overview)).toBeNull();
    },
  );
});
