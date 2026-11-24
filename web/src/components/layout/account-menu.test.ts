import { describe, expect, it, vi } from "vitest";

vi.mock("@/lib/auth/actions", () => ({ logout: vi.fn() }));

const { initials } = await import("./account-menu");

describe("initials", () => {
  it.each([
    ["Ana Marić", "AM"],
    ["ana", "AN"],
    ["  Jean  Luc  Picard ", "JP"],
    ["", "?"],
  ])("%s → %s", (name, expected) => expect(initials(name)).toBe(expected));
});
