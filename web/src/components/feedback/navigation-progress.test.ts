import { describe, expect, it } from "vitest";

import { isPageNavigation } from "./navigation-progress";

const current = new URL("http://localhost:3000/tours?city=Split");
const click = { button: 0, metaKey: false, ctrlKey: false, shiftKey: false, altKey: false, defaultPrevented: false };

function anchor(href: string, attrs: Record<string, string> = {}): HTMLAnchorElement {
  const element = document.createElement("a");
  element.href = new URL(href, current).toString();
  for (const [key, value] of Object.entries(attrs)) element.setAttribute(key, value);
  return element;
}

describe("isPageNavigation", () => {
  it("starts for a plain click to another page or query", () => {
    expect(isPageNavigation(click, anchor("/tours/123"), current)).toBe(true);
    expect(isPageNavigation(click, anchor("/tours?city=Hvar"), current)).toBe(true);
  });

  it("ignores in-page anchors, new tabs, downloads, other sites and modified clicks", () => {
    expect(isPageNavigation(click, anchor("/tours?city=Split#reviews"), current)).toBe(false);
    expect(isPageNavigation(click, anchor("/tours/123", { target: "_blank" }), current)).toBe(false);
    expect(isPageNavigation(click, anchor("/file.ics", { download: "" }), current)).toBe(false);
    expect(isPageNavigation(click, anchor("https://example.com/"), current)).toBe(false);
    expect(isPageNavigation({ ...click, metaKey: true }, anchor("/tours/123"), current)).toBe(false);
    expect(isPageNavigation({ ...click, defaultPrevented: true }, anchor("/tours/123"), current)).toBe(false);
  });
});
