import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import App from "./App";
import { createBridge, findWebView } from "./protocol/bridge";
import { createRecorder } from "./sim/recorder";
import { createSourceClock } from "./time/sourceClock";
import { feedClock } from "./time/clockFeed";

const webview = findWebView();

const simulator =
  !webview && import.meta.env.DEV
    ? await import("./sim/simulatorBridge")
    : null;

const bridge = simulator
  ? simulator.createSimulatorBridge(location.search)
  : createBridge(webview);

const clock = createSourceClock(() => performance.now());
if (simulator) clock.setSpeed(simulator.speedFrom(location.search));
bridge.onMessage(feedClock(clock));

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
