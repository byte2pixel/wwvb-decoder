import type {
  Line,
  Polyline,
  PulseMark,
  Text,
  Rect,
  SignalDrawing,
} from "./model";

export function paintSignal(
  ctx: CanvasRenderingContext2D,
  drawing: SignalDrawing,
  fontFamily: string,
): void {
  const strokeLine = (line: Line): void => {
    ctx.beginPath();
    ctx.moveTo(line.x1, line.y1);
    ctx.lineTo(line.x2, line.y2);
    ctx.strokeStyle = line.stroke;
    ctx.lineWidth = line.width;
    ctx.stroke();
  };

  const strokePolyline = (polyline: Polyline | null): void => {
    if (!polyline) return;
    ctx.beginPath();
    ctx.moveTo(polyline.points[0].x, polyline.points[0].y);
    for (let i = 1; i < polyline.points.length; i++) {
      ctx.lineTo(polyline.points[i].x, polyline.points[i].y);
    }
    ctx.strokeStyle = polyline.stroke;
    ctx.lineWidth = polyline.width;
    ctx.stroke();
  };

  const paintRect = (bar: Rect): void => {
    ctx.beginPath();
    ctx.roundRect(bar.x, bar.y, bar.w, bar.h, bar.radius);
    if (bar.fill) {
      ctx.fillStyle = bar.fill;
      ctx.fill();
    }
    if (bar.stroke) {
      ctx.lineWidth = 1;
      ctx.strokeStyle = bar.stroke;
      ctx.stroke();
    }
  };

  const writeText = (label: Text | null): void => {
    if (!label) return;
    const { text, size, x, y, fill, align } = label;
    ctx.font = `${size}px ${fontFamily}`;
    ctx.textAlign = align;
    ctx.fillStyle = fill;
    ctx.textBaseline = "middle";
    ctx.fillText(text, x, y);
  };

  const paintMark = ({ bar, label }: PulseMark): void => {
    paintRect(bar);
    writeText(label);
  };

  drawing.ticks.forEach(strokeLine);
  strokePolyline(drawing.carrier);
  drawing.pulses.forEach(paintMark);
  // Over the pulses, because a bridged gap is a notch in its pulse's bar.
  drawing.glitches.forEach(paintMark);
  drawing.symbols.forEach((chip) => {
    const { box, text } = chip;
    paintRect(box);
    writeText(text);
  });
  drawing.seconds.forEach(writeText);
  if (drawing.cursor) strokeLine(drawing.cursor);
}
