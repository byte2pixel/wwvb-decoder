import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import App from "./App";
import { createBridge, findWebView } from "./protocol/bridge";
import { createRecorder } from "./sim/recorder";

const webview = findWebView();
const bridge =
  !webview && import.meta.env.DEV
    ? (await import("./sim/simulatorBridge")).createSimulatorBridge(
        location.search,
      )
    : createBridge(webview);

if (import.meta.env.DEV) {
  const recorder = createRecorder(() => performance.now());
  bridge.onMessage(recorder.add);
  window.wwvbRecorder = recorder;
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App bridge={bridge} />
  </StrictMode>,
);
