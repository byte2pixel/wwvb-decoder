import { readFileSync } from "node:fs";
import { afterEach, describe, expect, it } from "vitest";
import { readSignalStyle } from "./style";

/** Puts a stylesheet on the page and returns a canvas in the monospaced face. */
function page(styles: string): HTMLCanvasElement {
  const sheet = document.createElement("style");
  sheet.textContent = styles;
  document.head.append(sheet);
  const canvas = document.createElement("canvas");
  canvas.className = "font-mono";
  document.body.append(canvas);
  return canvas;
}

describe("readSignalStyle", () => {
  afterEach(() => {
    document.head.replaceChildren();
    document.body.replaceChildren();
    document.documentElement.className = "";
  });

  it("reads each token from its variable, the rem from the root, and the font from the canvas", () => {
    // Each variable holds its own name, so the result reads as the mapping.
    const canvas = page(`
      :root {
        font-size: 20px;
        --border: border;
        --input: input;
        --signal: signal;
        --on-signal: on-signal;
        --zero: zero;
        --zero-surface: zero-surface;
        --marker: marker;
        --marker-surface: marker-surface;
        --bad: bad;
        --foreground-soft: foreground-soft;
        --muted-foreground: muted-foreground;
      }
      .font-mono {
        font-family: TestMono, monospace;
      }
    `);

    expect(readSignalStyle(canvas)).toEqual({
      tokens: {
        line: "border",
        lineStrong: "input",
        signal: "signal",
        onSignal: "on-signal",
        zero: "zero",
        zeroSurface: "zero-surface",
        marker: "marker",
        markerSurface: "marker-surface",
        bad: "bad",
        textSoft: "foreground-soft",
        textMuted: "muted-foreground",
      },
      rem: 20,
      fontFamily: "TestMono, monospace",
    });
  });

  it("finds every token in the page's stylesheet, in both themes", () => {
    // Read from disk, relative to ui/: vitest hands a test an empty string for a stylesheet import.
    const canvas = page(readFileSync("src/index.css", "utf8"));

    const light = readSignalStyle(canvas).tokens;
    document.documentElement.classList.add("dark");
    const dark = readSignalStyle(canvas).tokens;

    expect(Object.values(light)).not.toContain("");
    expect(Object.values(dark)).not.toContain("");
    expect(dark.signal).not.toBe(light.signal);
  });
});
