/** One thing a canvas would show, with the state it was painted in. */
export type Painted =
  | { op: "stroke"; path: string[]; strokeStyle: string; lineWidth: number }
  | { op: "fill"; path: string[]; fillStyle: string }
  | {
      op: "text";
      text: string;
      x: number;
      y: number;
      font: string;
      fillStyle: string;
      textAlign: string;
      textBaseline: string;
    };

/**
 * A stand-in for a canvas context, for tests. It keeps the current path and the styles as a
 * canvas does, and records what each stroke, fill, and text call would paint. It has only the
 * calls the signal painter uses.
 */
export function recordingContext() {
  const painted: Painted[] = [];
  let path: string[] = [];
  const ctx = {
    strokeStyle: "",
    fillStyle: "",
    lineWidth: 1,
    font: "",
    textAlign: "start",
    textBaseline: "alphabetic",
    beginPath(): void {
      path = [];
    },
    moveTo(x: number, y: number): void {
      path.push(`M ${x} ${y}`);
    },
    lineTo(x: number, y: number): void {
      path.push(`L ${x} ${y}`);
    },
    roundRect(
      x: number,
      y: number,
      w: number,
      h: number,
      radius: number,
    ): void {
      path.push(`R ${x} ${y} ${w} ${h} ${radius}`);
    },
    stroke(): void {
      painted.push({
        op: "stroke",
        path: [...path],
        strokeStyle: ctx.strokeStyle,
        lineWidth: ctx.lineWidth,
      });
    },
    fill(): void {
      painted.push({ op: "fill", path: [...path], fillStyle: ctx.fillStyle });
    },
    fillText(text: string, x: number, y: number): void {
      painted.push({
        op: "text",
        text,
        x,
        y,
        font: ctx.font,
        fillStyle: ctx.fillStyle,
        textAlign: ctx.textAlign,
        textBaseline: ctx.textBaseline,
      });
    },
  };
  return { ctx: ctx as unknown as CanvasRenderingContext2D, painted };
}
