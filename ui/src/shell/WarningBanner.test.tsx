import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import type { HostMessage } from "@/protocol/messages";
import { useAppStore } from "@/state/store";
import { WarningBanner } from "./WarningBanner";

const notFound = "COM9 was not found. Replaying the newest capture.";
const inverted = "The reduced-carrier level looks inverted.";

let seq = 0;

/** A status of a running replay as the host sends it, with the warning it carries, if any. */
const status = (warning?: string, detail = ""): HostMessage => ({
  type: "sourceStatus",
  seq: ++seq,
  payload: {
    source: "replay",
    name: "replay",
    state: "running",
    detail,
    speed: 1,
    ...(warning === undefined ? {} : { warning }),
  },
});

const arrive = (message: HostMessage) =>
  act(() => useAppStore.getState().dispatch(message));

describe("WarningBanner", () => {
  beforeEach(() => {
    seq = 0;
    useAppStore.setState(useAppStore.getInitialState(), true);
  });

  it("draws nothing while the host has no warning", () => {
    render(<WarningBanner />);

    expect(screen.queryByRole("status")).not.toBeInTheDocument();

    arrive(status(""));
    expect(screen.queryByRole("status")).not.toBeInTheDocument();

    // A host from before the field was added
    arrive(status());
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("shows the host's warning as a status, in the host's words", () => {
    render(<WarningBanner />);

    arrive(status(notFound));

    const banner = screen.getByRole("status");
    expect(banner).toHaveTextContent(notFound);
    // Not by its colour alone
    expect(banner).toHaveTextContent(/^Warning:/);
  });

  it("goes away when the host stops warning", () => {
    render(<WarningBanner />);
    arrive(status(notFound));

    arrive(status(""));

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("stays through the later statuses of the run", () => {
    render(<WarningBanner />);
    arrive(status(notFound));

    arrive(status(notFound, "reset: loop"));

    expect(screen.getByRole("status")).toHaveTextContent(notFound);
  });

  it("goes away when it is closed, and stays away while the host sends the same text", async () => {
    const user = userEvent.setup();
    render(<WarningBanner />);
    arrive(status(notFound));

    await user.click(screen.getByRole("button", { name: "Close the warning" }));

    expect(screen.queryByRole("status")).not.toBeInTheDocument();

    arrive(status(notFound, "reset: loop"));
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("comes back for another warning", async () => {
    const user = userEvent.setup();
    render(<WarningBanner />);
    arrive(status(notFound));
    await user.click(screen.getByRole("button", { name: "Close the warning" }));

    arrive(status(inverted));

    expect(screen.getByRole("status")).toHaveTextContent(inverted);
  });

  it("comes back for the same warning after a status without one", async () => {
    const user = userEvent.setup();
    render(<WarningBanner />);
    arrive(status(inverted));
    await user.click(screen.getByRole("button", { name: "Close the warning" }));

    // The cause went away, and then came again
    arrive(status(""));
    arrive(status(inverted));

    expect(screen.getByRole("status")).toHaveTextContent(inverted);
  });
});
