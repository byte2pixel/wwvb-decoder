import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import {
  hostMessageSchema,
  type DstStatus,
  type Ut1Sign,
  type ValidationCheck,
  type ValidationResult,
} from "@/protocol/messages";
import { failureLine, frameRows } from "./model";

/** The host's validation example: the frame that reads 19:35 where 19:31 was due. */
const example = ((): ValidationResult => {
  const file = new URL(
    "../../../../docs/design/message-examples/validation.json",
    import.meta.url,
  );
  const message = hostMessageSchema.parse(
    JSON.parse(readFileSync(file, "utf8")),
  );
  if (message.type !== "validation") throw new Error("not a validation");
  return message.payload;
})();

/** The example's frame, accepted, with some of its decoded values replaced. */
const accepted = (
  values: Partial<ValidationResult["frame"]> = {},
): ValidationResult => ({
  frame: { ...example.frame, ...values },
  accepted: true,
  failures: [],
});

/** The example's frame, rejected for these checks. */
const rejected = (...checks: ValidationCheck[]): ValidationResult => ({
  ...example,
  failures: checks.map((check) => ({ check, detail: `${check} failed` })),
});

describe("frameRows", () => {
  it("has no rows before a frame is validated", () => {
    expect(frameRows([], 0)).toEqual([]);
  });

  it("reads the host's example as a rejected row", () => {
    expect(frameRows([example], 1)).toEqual([
      {
        number: 1,
        symbols: example.frame.frame.symbols,
        minute: 35,
        hour: 19,
        dayOfYear: 344,
        year: 26,
        ut1: "+0.0 s",
        dst: "standard",
        accepted: false,
        result: "Rejected: consecutive",
        failures: [
          {
            check: "consecutive",
            detail: "expected 2026-12-10T19:31Z, got 2026-12-10T19:35Z",
          },
        ],
      },
    ]);
  });

  it("reads an accepted frame as Accepted, with no failures", () => {
    const [row] = frameRows([accepted()], 1);

    expect(row).toMatchObject({
      accepted: true,
      result: "Accepted",
      failures: [],
    });
  });

  it("lists the newest frame first, numbered since the source reset", () => {
    // The store holds the newest three of 41 frames.
    const rows = frameRows(
      [
        accepted({ minute: 29 }),
        accepted({ minute: 30 }),
        accepted({ minute: 31 }),
      ],
      41,
    );

    expect(rows.map((row) => [row.number, row.minute])).toEqual([
      [41, 31],
      [40, 30],
      [39, 29],
    ]);
  });

  it.each<[Ut1Sign, number, string]>([
    ["plus", 3, "+0.3 s"],
    ["minus", 7, "-0.7 s"],
    ["plus", 0, "+0.0 s"],
    ["invalid", 2, "?0.2 s"],
  ])(
    "writes a UT1 correction of %s %d tenths as %s",
    (ut1Sign, ut1Tenths, text) => {
      const [row] = frameRows([accepted({ ut1Sign, ut1Tenths })], 1);

      expect(row.ut1).toBe(text);
    },
  );

  it.each<[DstStatus, string]>([
    ["standard", "standard"],
    ["dst", "dst"],
    ["beginsToday", "begins today"],
    ["endsToday", "ends today"],
  ])("writes the DST status %s as %s", (dst, text) => {
    const [row] = frameRows([accepted({ dst })], 1);

    expect(row.dst).toBe(text);
  });

  it.each<[ValidationCheck, string]>([
    ["markers", "Rejected: markers"],
    ["unusedBits", "Rejected: unused bits"],
    ["errorSymbols", "Rejected: error symbols"],
    ["bcdDigits", "Rejected: BCD digits"],
    ["dayOfYearRange", "Rejected: day of year range"],
    ["ut1Sign", "Rejected: UT1 sign"],
  ])("names the check %s in the result as %s", (check, text) => {
    const [row] = frameRows([rejected(check)], 1);

    expect(row.result).toBe(text);
  });

  it("names the first failure in the result and keeps every one", () => {
    const [row] = frameRows([rejected("errorSymbols", "consecutive")], 1);

    expect(row.result).toBe("Rejected: error symbols");
    expect(row.failures.map((failure) => failure.check)).toEqual([
      "errorSymbols",
      "consecutive",
    ]);
  });
});

describe("failureLine", () => {
  it("writes a failure as its check in words and the host's detail", () => {
    expect(
      failureLine({ check: "errorSymbols", detail: "seconds 12, 13" }),
    ).toBe("error symbols: seconds 12, 13");
  });
});
