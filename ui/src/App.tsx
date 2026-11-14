import { useEffect, useState } from "react";
import "./App.css";
import { createBridge, findWebView } from "./protocol/bridge";
import { Button } from "./components/ui/button";
import { useAppStore } from "./state/store";

const bridge = createBridge(findWebView());

export default function App() {
  const log = useAppStore((state) => state.log);
  const status = useAppStore((state) => state.sourceStatus);
  const [noise, setNoise] = useState(0);

  useEffect(() => {
    const off = bridge.onMessage(useAppStore.getState().dispatch);
    bridge.send({ command: "hello", protocol: 1 });
    return off;
  }, []);

  return (
    <div className="app">
      <header className="header">
        <select value="synthetic" onChange={() => {}}>
          <option value="synthetic">synthetic</option>
          <option value="serial" disabled>
            serial
          </option>
          <option value="replay" disabled>
            replay
          </option>
        </select>
        <input
          type="range"
          min="0"
          max="1"
          step="0.05"
          value={noise}
          onChange={(e) => setNoise(Number(e.target.value))}
        />
        <Button
          onClick={() =>
            bridge.send({ command: "selectSource", source: "synthetic", noise })
          }
        >
          Start
        </Button>
        <button onClick={() => bridge.send({ command: "stop" })}>Stop</button>
        <div className="status">
          {status
            ? `${status.name} ${status.state} ${status.detail}`
            : "no host"}
        </div>
      </header>
      <main className="main">
        <section className="counts">
          {/* one div per type */}
          {Object.entries(log.counts).map(([type, count]) => (
            <div key={type}>
              <span>{type}</span>
              <span>{count}</span>
            </div>
          ))}
        </section>
        <section className="entries">
          {/* newest first */}
          {log.entries
            .slice()
            .reverse()
            .map((entry) => (
              <div className="entry" key={entry.seq}>
                <pre>{entry.type}</pre>
                <pre>{entry.seq}</pre>
                <pre>{JSON.stringify(entry.payload)}</pre>
              </div>
            ))}
        </section>
      </main>
    </div>
  );
}
