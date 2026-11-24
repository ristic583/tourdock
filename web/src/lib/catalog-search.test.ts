import { describe, expect, it } from "vitest";

import { hasFilters, pageList, parseCatalogSearch, searchHref } from "./catalog-search";

describe("parseCatalogSearch", () => {
  it("falls back to defaults for missing or invalid values", () => {
    expect(parseCatalogSearch({ guests: "0", page: "x", sort: "Cheapest", mode: "Group", date: "14/06/2027", minPrice: "-5", cityId: "nope" })).toEqual({
      q: undefined,
      city: undefined,
      cityId: undefined,
      radiusKm: undefined,
      country: undefined,
      date: undefined,
      guests: 1,
      minPrice: undefined,
      maxPrice: undefined,
      mode: undefined,
      sort: "Soonest",
      page: 1,
    });
  });

  it("reads every filter from the URL", () => {
    const search = parseCatalogSearch({ city: " Split ", date: "2027-06-14", guests: "3", minPrice: "20", maxPrice: "150", mode: "Private", sort: "PriceAsc", page: "2" });
    expect(search).toMatchObject({ city: "Split", date: "2027-06-14", guests: 3, minPrice: 20, maxPrice: 150, mode: "Private", sort: "PriceAsc", page: 2 });
  });

  it("caps a guest count above the limit instead of resetting it", () => {
    expect(parseCatalogSearch({ guests: "999" }).guests).toBe(100);
  });

  it("searches near a picked place, nearest first by default", () => {
    const id = "65f9b5e0-6a5b-5884-891e-630196c908d4";
    expect(parseCatalogSearch({ city: "Split", cityId: id })).toMatchObject({ city: "Split", cityId: id, radiusKm: 25, sort: "Nearest" });
    expect(parseCatalogSearch({ city: "Split", cityId: id, radiusKm: "50", sort: "PriceAsc" })).toMatchObject({ radiusKm: 50, sort: "PriceAsc" });
    expect(parseCatalogSearch({ sort: "Nearest" }).sort).toBe("Soonest"); // nothing to measure from
  });

  it("swaps an inverted price range instead of sending it to the API", () => {
    expect(parseCatalogSearch({ minPrice: "100", maxPrice: "10" })).toMatchObject({ minPrice: 10, maxPrice: 100 });
  });
});

describe("searchHref with a place", () => {
  it("leaves the default radius and order out and round-trips", () => {
    const search = parseCatalogSearch({ city: "Split", cityId: "65f9b5e0-6a5b-5884-891e-630196c908d4" });
    const href = searchHref(search);
    expect(href).toBe("/tours?city=Split&cityId=65f9b5e0-6a5b-5884-891e-630196c908d4");
    expect(parseCatalogSearch(Object.fromEntries(new URL(href, "http://x").searchParams))).toEqual(search);
    expect(searchHref(search, { radiusKm: 50, sort: "Soonest" })).toContain("radiusKm=50&sort=Soonest");
  });
});

describe("searchHref", () => {
  const search = parseCatalogSearch({ city: "Split", guests: "3", mode: "Shared", page: "4" });

  it("round-trips through the parser", () => {
    const href = searchHref(search);
    expect(parseCatalogSearch(Object.fromEntries(new URL(href, "http://x").searchParams))).toEqual(search);
  });

  it("leaves defaults out of the URL", () => {
    expect(searchHref(parseCatalogSearch({}))).toBe("/tours");
    expect(searchHref(search, { mode: undefined, page: 1 })).toBe("/tours?city=Split&guests=3");
  });
});

describe("hasFilters", () => {
  it("ignores the search terms and looks only at sidebar filters", () => {
    expect(hasFilters(parseCatalogSearch({ city: "Split", guests: "4" }))).toBe(false);
    expect(hasFilters(parseCatalogSearch({ maxPrice: "100" }))).toBe(true);
    expect(hasFilters(parseCatalogSearch({ sort: "Rating" }))).toBe(true);
  });
});

describe("pageList", () => {
  it("shows every page when there are few", () => {
    expect(pageList(1, 1)).toEqual([1]);
    expect(pageList(2, 4)).toEqual([1, 2, 3, 4]);
  });

  it("collapses long runs into gaps around the current page", () => {
    expect(pageList(1, 15)).toEqual([1, 2, "gap", 15]);
    expect(pageList(7, 15)).toEqual([1, "gap", 6, 7, 8, "gap", 15]);
    expect(pageList(15, 15)).toEqual([1, "gap", 14, 15]);
  });

  it("fills a single missing page instead of drawing a gap", () => {
    expect(pageList(4, 15)).toEqual([1, 2, 3, 4, 5, "gap", 15]);
  });
});

describe("free-text description (q)", () => {
  it("reads and trims the description and orders by best match by default", () => {
    const search = parseCatalogSearch({ q: "  calm boat trip  ", city: "Split" });
    expect(search.q).toBe("calm boat trip");
    expect(search.sort).toBe("Relevance");
    expect(searchHref(search)).toBe("/tours?q=calm+boat+trip&city=Split");
  });

  it("drops best match when there is no description and caps an overlong one", () => {
    expect(parseCatalogSearch({ sort: "Relevance" }).sort).toBe("Soonest");
    expect(parseCatalogSearch({ q: "a".repeat(250) }).q).toHaveLength(200);
    expect(parseCatalogSearch({ q: "   " }).q).toBeUndefined();
  });

  it("keeps an explicit order next to a description", () => {
    const search = parseCatalogSearch({ q: "fishing", sort: "PriceAsc" });
    expect(search.sort).toBe("PriceAsc");
    expect(searchHref(search)).toBe("/tours?q=fishing&sort=PriceAsc");
  });
});
