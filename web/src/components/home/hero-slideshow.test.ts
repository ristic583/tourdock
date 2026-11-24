import { describe, expect, it } from "vitest";

import { slideClass } from "./hero-slideshow";

describe("hero slides", () => {
  it("only fade: the shown photo is opaque, the others transparent", () => {
    expect(slideClass(1, 1)).toContain("opacity-100");
    expect(slideClass(0, 1)).toContain("opacity-0");
    expect(slideClass(1, 1)).toContain("transition-opacity");
  });

  it("never zoom or move a photo", () => {
    for (const classes of [slideClass(0, 0), slideClass(2, 0)]) {
      expect(classes).not.toMatch(/scale|translate|transform/);
    }
  });
});
