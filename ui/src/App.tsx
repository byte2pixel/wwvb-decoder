import { useEffect, useReducer, useState } from "react";
import "./App.css";
import { createBridge, findWebView } from "./protocol/bridge.ts";
import { appendMessage, emptyLog } from "./protocol/log.ts";
import type { SourceStatus } from "./protocol/messages.ts";
import { Button } from "./components/ui/button.tsx";

const bridge = createBridge(findWebView());

export default function App() {
  const [log, dispatch] = useReducer(appendMessage, emptyLog);
  const [noise, setNoise] = useState(0);
  const [status, setStatus] = useState<SourceStatus | null>(null);

  useEffect(() => {
    const off = bridge.onMessage((message) => {
      dispatch(message);
      if (message.type === "sourceStatus") setStatus(message.payload);
    });
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
