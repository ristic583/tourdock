import { describe, expect, it } from "vitest";

import { MAX_PHOTO_BYTES, photoProblem } from "./photo-manager";

describe("photoProblem", () => {
  it("accepts JPEG, PNG and WebP up to 10 MB", () => {
    expect(photoProblem({ name: "a.jpg", type: "image/jpeg", size: MAX_PHOTO_BYTES })).toBeUndefined();
    expect(photoProblem({ name: "a.webp", type: "image/webp", size: 1 })).toBeUndefined();
  });

  it("rejects other formats and large files before uploading", () => {
    expect(photoProblem({ name: "a.gif", type: "image/gif", size: 1 })).toMatch(/JPEG, PNG or WebP/);
    expect(photoProblem({ name: "a.png", type: "image/png", size: MAX_PHOTO_BYTES + 1 })).toMatch(/10 MB/);
  });
});
