import {
  hostMessageSchema,
  type Command,
  type HostMessage,
} from "./messages.ts";
import { z } from "zod";

declare global {
  interface Window {
    chrome?: { webview?: WebView };
  }
}

export interface MessageEventLike {
  data: unknown;
}

/** The subset of window.chrome.webview the page uses. */
export interface WebView {
  addEventListener(
    type: "message",
    listener: (event: MessageEventLike) => void,
  ): void;
  removeEventListener(
    type: "message",
    listener: (event: MessageEventLike) => void,
  ): void;
  postMessage(message: unknown): void;
}

export interface Bridge {
  /** Subscribes to validated host messages. Returns an unsubscribe function. */
  onMessage(handler: (message: HostMessage) => void): () => void;
  send(command: Command): void;
}

export function createBridge(
  webview: WebView | undefined,
  warn: (...args: unknown[]) => void = console.warn,
): Bridge {
  return {
    onMessage: (handler: (message: HostMessage) => void) => {
      if (!webview) {
        warn("WebView is not available, cannot subscribe to messages");
        return () => {};
      }
      const listener = (event: MessageEventLike) => {
        const result = hostMessageSchema.safeParse(event.data);
        if (result.success) {
          handler(result.data);
        } else {
          warn(
            "Invalid host message received",
            z.prettifyError(result.error),
            event.data,
          );
        }
      };
      webview.addEventListener("message", listener);
      return () => {
        webview.removeEventListener("message", listener);
      };
    },
    send: (command: Command) => {
      if (webview) {
        webview.postMessage(command);
      } else {
        warn("WebView is not available, cannot send command", command);
      }
    },
  };
}
export function findWebView(): WebView | undefined {
  return typeof window === "undefined" ? undefined : window.chrome?.webview;
}
