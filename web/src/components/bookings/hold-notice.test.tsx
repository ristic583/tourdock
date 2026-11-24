import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { HoldNotice, formatRemaining } from "./hold-notice";

describe("HoldNotice", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-11-06T15:00:00Z"));
  });
  afterEach(() => vi.useRealTimers());

  it("counts down to the hold expiry", () => {
    render(<HoldNotice expiresAt="2026-11-06T15:04:25Z" />);
    expect(screen.getByRole("status")).toHaveTextContent("(4:25 left)");

    act(() => vi.advanceTimersByTime(5000));
    expect(screen.getByRole("status")).toHaveTextContent("(4:20 left)");
  });

  it("says when the hold has expired", () => {
    render(<HoldNotice expiresAt="2026-11-06T14:59:00Z" />);
    expect(screen.getByRole("status")).toHaveTextContent("This hold has expired.");
  });

  it("formats minutes and seconds", () => {
    expect(formatRemaining(905)).toBe("15:05");
    expect(formatRemaining(-3)).toBe("0:00");
  });
});
