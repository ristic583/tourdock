import { describe, expect, it } from "vitest";

import { meetingPointLinks } from "./meeting-point";

describe("meeting point maps", () => {
  it.each([
    ["Split", 43.5076, 16.4381],
    ["Hvar", 43.171182, 16.44134],
    ["Dubrovnik", 42.64173, 18.10685],
    ["Kotor", 42.424737, 18.769705],
  ])("centers the map and external link on the same meeting point in %s", (_city, lat, lon) => {
    const links = meetingPointLinks(lat, lon);
    const url = new URL(links.embed);
    expect(url.searchParams.get("marker")).toBe(`${lat},${lon}`);
    const [west, south, east, north] = url.searchParams.get("bbox")!.split(",").map(Number);
    expect(west).toBeLessThan(lon);
    expect(east).toBeGreaterThan(lon);
    expect(south).toBeLessThan(lat);
    expect(north).toBeGreaterThan(lat);
    expect(links.full).toContain(`#map=16/${lat}/${lon}`);
  });
});
