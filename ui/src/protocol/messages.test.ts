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
  it("has 19 example files", () => {
    expect(files).toHaveLength(19);
  });

  it("has 8 command files and 11 host message files", () => {
    const commands = files.filter((f) => f.startsWith("command-"));
    expect(commands).toHaveLength(8);
    expect(files.length - commands.length).toBe(11);
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
});
