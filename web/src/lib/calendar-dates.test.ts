import { describe, expect, it } from "vitest";

import { addDays, addMonths, formatPickedDate, inRange, isIsoDate, monthGrid } from "./calendar-dates";

describe("calendar dates", () => {
  it("accepts only real ISO dates", () => {
    expect(isIsoDate("2026-11-14")).toBe(true);
    expect(isIsoDate("2026-02-31")).toBe(false);
    expect(isIsoDate("14.11.2026")).toBe(false);
    expect(isIsoDate(undefined)).toBe(false);
  });

  it("moves across month and year ends, and over the DST change", () => {
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(addDays("2026-10-24", 2)).toBe("2026-10-26");
    expect(addMonths("2026-01-31", 1)).toBe("2026-02-01");
    expect(addMonths("2026-01-15", -1)).toBe("2025-12-01");
  });

  it("builds a Monday-first 6-week grid", () => {
    const grid = monthGrid("2026-11-14");
    expect(grid).toHaveLength(42);
    expect(grid[0]).toBe("2026-10-26"); // 1 Nov 2026 is a Sunday
    expect(grid[6]).toBe("2026-11-01");
  });

  it("checks optional inclusive bounds", () => {
    expect(inRange("2026-11-14", "2026-11-14", "2026-11-30")).toBe(true);
    expect(inRange("2026-11-13", "2026-11-14")).toBe(false);
    expect(inRange("2026-12-01", undefined, "2026-11-30")).toBe(false);
  });

  it("formats a picked date without the browser's placeholder style", () => {
    expect(formatPickedDate("2026-11-14")).toBe("Sat 14 Nov 2026");
  });
});
