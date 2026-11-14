import { describe, expect, it } from "vitest";
import { createSourceClock } from "./sourceClock.ts";

const fakeWall = (startMs = 1_000) => {
  let ms = startMs;
  return {
    now: () => ms,
    advance: (byMs: number) => {
      ms += byMs;
    },
  };
};

describe("createSourceClock", () => {
  it("has no present before the first edge", () => {
    // Arrange & Act
    const clock = createSourceClock(() => 0);

    // Assert
    expect(clock.now()).toBeNull();
  });

  it("advances with wall time at 1x after the first edge", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.sync(12_000_000);
    expect(clock.now()).toBe(12_000_000);

    wall.advance(500);
    expect(clock.now()).toBe(12_500_000);
  });

  it("runs at the new speed from the moment it is set, without a jump", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.sync(12_000_000);
    wall.advance(500);

    clock.setSpeed(10);
    expect(clock.now()).toBe(12_500_000);

    wall.advance(100);
    expect(clock.now()).toBe(13_500_000);
  });

  it("keeps a speed set before the first edge", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act
    clock.setSpeed(10);
    clock.sync(12_000_000);
    wall.advance(100);

    // Assert
    expect(clock.now()).toBe(13_000_000);
  });

  it("has no present at speed 0, where the source is unpaced", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.sync(12_000_000);
    clock.setSpeed(0);
    expect(clock.now()).toBeNull();

    clock.sync(500_000_000);
    expect(clock.now()).toBeNull();
  });

  it("anchors on the next edge when the speed returns from 0", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);
    clock.sync(12_000_000);
    clock.setSpeed(0);
    clock.sync(500_000_000);
    wall.advance(100);

    // Act & Assert
    clock.setSpeed(10);
    expect(clock.now()).toBeNull();

    clock.sync(600_000_000);
    wall.advance(100);
    expect(clock.now()).toBe(601_000_000);
  });

  it("makes up the difference over 250 ms when an edge is ahead of the estimate", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);
    clock.sync(12_000_000);
    wall.advance(100);

    // Act & Assert
    clock.sync(12_400_000); // 300 ms ahead of the estimate
    expect(clock.now()).toBe(12_100_000); // no jump

    wall.advance(125); // halfway: 125 ms of time plus half of the 300 ms
    expect(clock.now()).toBe(12_375_000);

    wall.advance(125); // done: where the edge's own timeline is by now
    expect(clock.now()).toBe(12_650_000);

    wall.advance(100); // back to 1x
    expect(clock.now()).toBe(12_750_000);
  });

  it("takes back a tenth of the difference when an edge is behind the estimate", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);
    clock.sync(12_000_000);
    wall.advance(500);

    // Act & Assert
    clock.sync(12_300_000); // 200 ms behind the estimate
    expect(clock.now()).toBe(12_500_000); // no jump

    wall.advance(125); // halfway: 125 ms of time less half of the 20 ms
    expect(clock.now()).toBe(12_615_000);

    wall.advance(125); // done: 250 ms of time less a tenth of the 200 ms
    expect(clock.now()).toBe(12_730_000);

    wall.advance(100); // back to 1x
    expect(clock.now()).toBe(12_830_000);
  });

  it("holds instead of running backwards when an edge is far behind", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.sync(10_000_000);
    wall.advance(10_000); // the clock reads 20,000,000

    clock.sync(15_000_000); // 5 s behind: a tenth is 500 ms, twice what 250 ms of time gives
    wall.advance(125);
    expect(clock.now()).toBe(20_000_000);

    wall.advance(125);
    expect(clock.now()).toBe(20_000_000);

    wall.advance(250); // 500 ms in: the 500 ms is given back
    expect(clock.now()).toBe(20_000_000);

    wall.advance(100); // running again
    expect(clock.now()).toBe(20_100_000);
  });

  it("forgets everything on reset and anchors on the next edge", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.sync(12_000_000);
    wall.advance(100);
    clock.sync(12_400_000); // a correction is in progress

    clock.reset();
    expect(clock.now()).toBeNull();

    clock.sync(3_000_000); // timestamps restart after a reset
    wall.advance(250);
    expect(clock.now()).toBe(3_250_000);
  });

  it("keeps the speed across a reset", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.setSpeed(10);
    clock.sync(12_000_000);

    clock.reset();
    clock.sync(3_000_000);
    wall.advance(100);

    expect(clock.now()).toBe(4_000_000);
  });

  it("finishes a catch-up when a late edge arrives in the middle of it", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.sync(12_000_000);
    wall.advance(100);
    clock.sync(12_400_000); // 300 ms ahead: the clock heads for this edge's timeline
    wall.advance(125); // halfway: reads 12,375,000, heading for 12,525,000

    clock.sync(12_275_000); // 250 ms behind where the clock is heading
    expect(clock.now()).toBe(12_375_000); // no jump

    wall.advance(250);
    // The first edge's timeline is at 12,775,000 by now, less a tenth of the 250 ms.
    expect(clock.now()).toBe(12_750_000);
  });

  it("finishes a catch-up when the speed changes in the middle of it", () => {
    // Arrange
    const wall = fakeWall();
    const clock = createSourceClock(wall.now);

    // Act & Assert
    clock.sync(12_000_000);
    wall.advance(100);
    clock.sync(12_400_000);
    wall.advance(125); // halfway: 150 ms of the 300 ms still to make up

    clock.setSpeed(10);
    expect(clock.now()).toBe(12_375_000); // no jump

    wall.advance(250);
    // 250 ms at 10x is 2,500,000, plus the 150,000 still owed.
    expect(clock.now()).toBe(15_025_000);
  });
});
