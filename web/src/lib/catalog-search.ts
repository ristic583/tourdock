import type { TourSort } from "@/lib/api/contracts";

/**
 * Search state of the catalog. It lives entirely in the URL (`/tours?city=…&mode=…&page=…`),
 * so every result page can be shared, bookmarked and reached with Back.
 * Client-safe: the filter sidebar uses the same parsing and URL building as the server page.
 */
export type CatalogSearch = {
  /** The trip in the visitor's own words ("calm boat trip for a family"): orders the results by meaning. */
  q?: string;
  /** What the visitor typed or picked, e.g. "Split". */
  city?: string;
  /** The picked place (from the autocomplete): the API searches within `radiusKm` of it. */
  cityId?: string;
  radiusKm?: number;
  country?: string;
  date?: string;
  guests: number;
  minPrice?: number;
  maxPrice?: number;
  mode?: "Shared" | "Private";
  sort: TourSort;
  page: number;
};

export type SearchInput = Record<string, string | string[] | undefined>;

/** Longest free-text description the API accepts. */
export const MAX_TEXT_LENGTH = 200;

export const SORT_OPTIONS: { value: TourSort; label: string }[] = [
  { value: "Relevance", label: "Best match" },
  { value: "Soonest", label: "Soonest departure" },
  { value: "PriceAsc", label: "Price: low to high" },
  { value: "PriceDesc", label: "Price: high to low" },
  { value: "Rating", label: "Top rated" },
  { value: "Nearest", label: "Nearest first" },
];

const SORTS = SORT_OPTIONS.map((option) => option.value);
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const regionNames = new Intl.DisplayNames(["en"], { type: "region" });

/** "Serbia" for "RS": the English name of a country code, or the code itself if it isn't one. */
export function countryLabel(code: string): string {
  try {
    return regionNames.of(code) ?? code;
  } catch {
    return code;
  }
}

/** What the search is around or in, for headings: the typed or picked place, else the country. */
export function placeLabel(search: Pick<CatalogSearch, "city" | "country">): string | undefined {
  return search.city ?? (search.country ? countryLabel(search.country) : undefined);
}

/** Radius options for a place search; the API defaults to 25 km. */
export const RADIUS_OPTIONS = [10, 25, 50, 100] as const;
export const DEFAULT_RADIUS_KM = 25;

type SortContext = Pick<CatalogSearch, "q" | "city" | "cityId">;

/** A described trip lists the best matches first; a place lists the nearest tours first; otherwise the soonest. */
export function defaultSort(search: SortContext): TourSort {
  if (search.q) return "Relevance";
  return search.city || search.cityId ? "Nearest" : "Soonest";
}

/** Whether an order makes sense for this search ("Nearest" needs a place, "Best match" a description). */
function sortFits(sort: TourSort, search: SortContext): boolean {
  if (sort === "Nearest") return Boolean(search.city || search.cityId);
  if (sort === "Relevance") return Boolean(search.q);
  return true;
}

/** The sort choices that make sense for this search. */
export function sortOptions(search: SortContext) {
  return SORT_OPTIONS.filter((option) => sortFits(option.value, search));
}

function one(value: SearchInput[string]): string | undefined {
  return typeof value === "string" ? value : undefined;
}

function integer(value: string | undefined, fallback: number, minimum: number, maximum: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed >= minimum && parsed <= maximum ? parsed : fallback;
}

/** Like `integer`, but a number above the range is capped instead of reset ("999 guests" means "as many as allowed"). */
function capped(value: string | undefined, fallback: number, minimum: number, maximum: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > maximum ? maximum : integer(value, fallback, minimum, maximum);
}

function money(value: string | undefined): number | undefined {
  if (!value?.trim()) return undefined;
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : undefined;
}

export function parseCatalogSearch(input: SearchInput): CatalogSearch {
  const sort = one(input.sort) as TourSort | undefined;
  const mode = one(input.mode);
  const date = one(input.date);
  let minPrice = money(one(input.minPrice));
  let maxPrice = money(one(input.maxPrice));
  // A hand-edited or shared URL can carry an inverted range; read it the way the sidebar does instead of failing.
  if (minPrice !== undefined && maxPrice !== undefined && minPrice > maxPrice) [minPrice, maxPrice] = [maxPrice, minPrice];
  const city = one(input.city)?.trim() || undefined;
  const cityId = one(input.cityId);
  const q = one(input.q)?.trim().slice(0, MAX_TEXT_LENGTH).trim() || undefined;
  const place = { city, cityId: cityId && UUID.test(cityId) ? cityId.toLowerCase() : undefined };
  const radius = integer(one(input.radiusKm), DEFAULT_RADIUS_KM, 1, 100);
  return {
    q,
    ...place,
    radiusKm: place.city || place.cityId ? radius : undefined,
    country: /^[A-Za-z]{2}$/.test(one(input.country)?.trim() ?? "") ? one(input.country)!.trim().toUpperCase() : undefined,
    date: date && /^\d{4}-\d{2}-\d{2}$/.test(date) ? date : undefined,
    guests: capped(one(input.guests), 1, 1, 100),
    minPrice,
    maxPrice,
    mode: mode === "Shared" || mode === "Private" ? mode : undefined,
    sort: sort && SORTS.includes(sort) && sortFits(sort, { q, ...place }) ? sort : defaultSort({ q, ...place }),
    page: integer(one(input.page), 1, 1, 1000),
  };
}

/** `/tours?…` for the search with the given changes. Defaults are left out so URLs stay short. */
export function searchHref(search: CatalogSearch, changes: Partial<CatalogSearch> = {}): string {
  const next = { ...search, ...changes };
  const query = new URLSearchParams();
  if (next.q) query.set("q", next.q);
  if (next.city) query.set("city", next.city);
  if (next.cityId) query.set("cityId", next.cityId);
  if ((next.city || next.cityId) && next.radiusKm && next.radiusKm !== DEFAULT_RADIUS_KM) query.set("radiusKm", String(next.radiusKm));
  if (next.country) query.set("country", next.country);
  if (next.date) query.set("date", next.date);
  if (next.guests !== 1) query.set("guests", String(next.guests));
  if (next.minPrice !== undefined) query.set("minPrice", String(next.minPrice));
  if (next.maxPrice !== undefined) query.set("maxPrice", String(next.maxPrice));
  if (next.mode) query.set("mode", next.mode);
  if (next.sort !== defaultSort(next)) query.set("sort", next.sort);
  if (next.page !== 1) query.set("page", String(next.page));
  const text = query.toString();
  return text ? `/tours?${text}` : "/tours";
}

/** Whether any sidebar filter (price, booking type, sort) differs from the default. */
export function hasFilters(search: CatalogSearch): boolean {
  return search.minPrice !== undefined || search.maxPrice !== undefined || Boolean(search.mode) || search.sort !== defaultSort(search);
}

/** Page numbers for the pager: first, last, and the current page with one neighbour each side. */
export function pageList(page: number, totalPages: number): (number | "gap")[] {
  const pages = new Set([1, totalPages, page - 1, page, page + 1].filter((n) => n >= 1 && n <= totalPages));
  const sorted = [...pages].sort((a, b) => a - b);
  const result: (number | "gap")[] = [];
  for (const n of sorted) {
    const previous = result.at(-1);
    if (typeof previous === "number" && n - previous > 1) result.push(n - previous === 2 ? previous + 1 : "gap");
    result.push(n);
  }
  return result;
}
