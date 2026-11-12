import { describe, expect, it, vi } from "vitest";
import { createBridge, type MessageEventLike, type WebView } from "./bridge.ts";

function fakeWebView() {
  const listeners = new Set<(event: MessageEventLike) => void>();
  const posted: unknown[] = [];
  const webview: WebView = {
    addEventListener: (_type, listener) => {
      listeners.add(listener);
    },
    removeEventListener: (_type, listener) => {
      listeners.delete(listener);
    },
    postMessage: (message) => {
      posted.push(message);
    },
  };
  const emit = (data: unknown) => {
    for (const l of listeners) l({ data });
  };
  return { webview, posted, emit, listeners };
}

describe("bridge send", () => {
  it("posts the command object to the webview as is", () => {
    const { webview, posted } = fakeWebView();
    const bridge = createBridge(webview);

    bridge.send({ command: "hello", protocol: 1 });

    expect(posted).toEqual([{ command: "hello", protocol: 1 }]);
  });
});

describe("bridge onMessage", () => {
  it("onMessage delivers a valid envelope", () => {
    // Arrange
    const message = {
      type: "info",
      seq: 49,
      payload: { line: "# wall 2026-11-02T03:11:00Z 14500398" },
    };
    const { webview, emit } = fakeWebView();
    const handler = vi.fn();
    const warn = vi.fn();
    createBridge(webview, warn);
    createBridge(webview, warn).onMessage(handler);

    // Act
    emit(message);

    // Assert
    expect(handler).toHaveBeenCalledWith(message);
  });

  it("onMessage drops an invalid envelope with a warning", () => {
    const { webview, emit } = fakeWebView();
    const warn = vi.fn();
    const handler = vi.fn();
    createBridge(webview, warn).onMessage(handler);

    // Act
    emit({ type: "info", seq: 1, payload: {} });

    // Assert
    expect(handler).not.toHaveBeenCalled();
    expect(warn).toHaveBeenCalledTimes(1);
  });

  it("Unsubscribe stops delivery", () => {
    const { webview, emit } = fakeWebView();
    const handler = vi.fn();
    const bridge = createBridge(webview);
    const unsubscribe = bridge.onMessage(handler);

    // Act
    emit({ type: "info", seq: 1, payload: { line: "# dropped 1" } });
    unsubscribe();
    emit({ type: "info", seq: 2, payload: { line: "# dropped 2" } });

    // Assert
    expect(handler).toHaveBeenCalledTimes(1);
  });
});

describe("bridge with no webview", () => {
  it("No webview is logged no-op", () => {
    const warn = vi.fn();
    const bridge = createBridge(undefined as unknown as WebView, warn);
    bridge.send({ command: "stop" });
    expect(warn).toHaveBeenCalled();
  });
});
