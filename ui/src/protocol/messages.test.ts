import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { commandSchema, hostMessageSchema } from "./messages";
import { join } from "node:path";

const examplesDir = fileURLToPath(
  new URL("../../../docs/design/message-examples/", import.meta.url),
);
const files = readdirSync(examplesDir)
  .filter((f) => f.endsWith(".json"))
  .sort();

describe("message examples", () => {
  it("has 21 example files", () => {
    expect(files).toHaveLength(21);
  });

  it("has 8 command files and 13 host message files", () => {
    const commands = files.filter((f) => f.startsWith("command-"));
    expect(commands).toHaveLength(8);
    expect(files.length - commands.length).toBe(13);
  });
});

const load = (file: string): unknown =>
  JSON.parse(readFileSync(join(examplesDir, file), "utf8"));

const commandFiles = files.filter((f) => f.startsWith("command-"));
const hostFiles = files.filter((f) => !f.startsWith("command-"));

describe.each(hostFiles)("host example %s", (file) => {
  it("parses as the envelope type its file name says", () => {
    const message = hostMessageSchema.parse(load(file));
    expect(message.type).toBe(file.replace(".json", ""));
  });
});

describe.each(commandFiles)("command example %s", (file) => {
  it("parses as the command its file name says", () => {
    const command = commandSchema.parse(load(file));
    expect(command.command).toBe(file.replace(/^command-|\.json$/g, ""));
  });
});

describe("a source status", () => {
  it("keeps its speed", () => {
    const message = hostMessageSchema.parse(load("sourceStatus.json"));
    if (message.type !== "sourceStatus") throw new Error("wrong type");

    expect(message.payload.speed).toBe(1);
  });

  it("keeps its warning", () => {
    const example = hostMessageSchema.parse(load("sourceStatus.json"));
    if (example.type !== "sourceStatus") throw new Error("wrong type");
    expect(example.payload.warning).toBe("");

    const warned = hostMessageSchema.parse({
      ...example,
      payload: { ...example.payload, warning: "COM9 was not found." },
    });

    expect(warned).toMatchObject({
      payload: { warning: "COM9 was not found." },
    });
  });

  it("may come without a speed or a warning, from an older host", () => {
    const result = hostMessageSchema.safeParse({
      type: "sourceStatus",
      seq: 1,
      payload: { source: "none", name: "", state: "stopped", detail: "" },
    });

    expect(result.success).toBe(true);
  });
});

describe("a selection", () => {
  const nothingRemembered = {
    source: "synthetic",
    port: null,
    capture: null,
    speed: 1,
    noise: 0,
    reducedCarrierLevel: "high",
  };

  it("takes null for a port and a capture that were never picked", () => {
    const message = hostMessageSchema.parse({
      type: "selection",
      seq: 5,
      payload: nothingRemembered,
    });

    expect(message.payload).toEqual(nothingRemembered);
  });

  it("is rejected with none as its source", () => {
    const result = hostMessageSchema.safeParse({
      type: "selection",
      seq: 5,
      payload: { ...nothingRemembered, source: "none" },
    });

    expect(result.success).toBe(false);
  });
});

describe("schema rejects", () => {
  it("a frameSync with an unknown kind", () => {
    const result = hostMessageSchema.safeParse({
      type: "frameSync",
      seq: 1,
      payload: { kind: "bogus" },
    });
    expect(result.success).toBe(false);
  });

  it("a frame with 59 symbols", () => {
    const good = hostMessageSchema.parse(load("decodedFrame.json"));
    if (good.type !== "decodedFrame") throw new Error("wrong type");
    const short = {
      ...good,
      payload: {
        ...good.payload,
        frame: {
          ...good.payload.frame,
          symbols: good.payload.frame.symbols.slice(1),
        },
      },
    };
    expect(hostMessageSchema.safeParse(short).success).toBe(false);
  });

  it.each(["roles", "weights"] as const)("a frame layout with 59 %s", (key) => {
    const good = hostMessageSchema.parse(load("frameLayout.json"));
    if (good.type !== "frameLayout") throw new Error("wrong type");
    const short = {
      ...good,
      payload: { ...good.payload, [key]: good.payload[key].slice(1) },
    };
    expect(hostMessageSchema.safeParse(short).success).toBe(false);
  });
});
