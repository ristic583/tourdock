"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DEFAULT_RADIUS_KM, defaultSort, hasFilters, RADIUS_OPTIONS, searchHref, sortOptions, type CatalogSearch } from "@/lib/catalog-search";
import type { TourSort } from "@/lib/api/contracts";
import { cn } from "@/lib/utils";

/** Upper end of the price bar. Prices above it can still be typed in. */
const PRICE_SCALE = 500;

const MODES: { value: CatalogSearch["mode"]; label: string }[] = [
  { value: undefined, label: "Any" },
  { value: "Shared", label: "Shared" },
  { value: "Private", label: "Private" },
];

function useSearchNavigation() {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  return {
    pending,
    go: (href: string) => startTransition(() => router.push(href, { scroll: false })),
  };
}

function priceValue(text: string): number | undefined {
  if (!text.trim()) return undefined;
  const value = Number(text);
  return Number.isFinite(value) && value >= 0 ? Math.round(value) : undefined;
}

/**
 * Price range and booking type. Each change becomes a new URL (and resets to page 1), so the sidebar holds no state
 * of its own beyond the price fields being typed in. Without JavaScript it is a GET form with an Apply button.
 * Render it with a `key` built from the prices, so Back and Reset refill the fields from the URL.
 */
export function FilterSidebar({ search }: { search: CatalogSearch }) {
  const { go, pending } = useSearchNavigation();
  const [min, setMin] = useState(search.minPrice?.toString() ?? "");
  const [max, setMax] = useState(search.maxPrice?.toString() ?? "");

  function applyPrice(event?: React.FormEvent) {
    event?.preventDefault();
    const minPrice = priceValue(min);
    const maxPrice = priceValue(max);
    const [low, high] = minPrice !== undefined && maxPrice !== undefined && minPrice > maxPrice ? [maxPrice, minPrice] : [minPrice, maxPrice];
    if (low === search.minPrice && high === search.maxPrice) return;
    go(searchHref(search, { minPrice: low, maxPrice: high, page: 1 }));
  }

  const low = Math.min(priceValue(min) ?? 0, PRICE_SCALE);
  const high = Math.min(priceValue(max) ?? PRICE_SCALE, PRICE_SCALE);
  const left = (Math.min(low, high) / PRICE_SCALE) * 100;
  const right = 100 - (Math.max(low, high) / PRICE_SCALE) * 100;

  return (
    <aside aria-label="Filters" className="flex flex-col gap-4" aria-busy={pending}>
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold">Filters</h2>
        {hasFilters(search) && (
          <Link href={searchHref(search, { minPrice: undefined, maxPrice: undefined, mode: undefined, sort: defaultSort(search), page: 1 })} scroll={false} className="text-sm font-semibold text-primary hover:text-primary-hover">
            Reset
          </Link>
        )}
      </div>
      <form action="/tours" noValidate onSubmit={applyPrice} className="flex flex-col gap-6 rounded-card border border-line bg-surface p-5">
        {/* Keep the rest of the search when the form is submitted without JavaScript. */}
        {search.q && <input type="hidden" name="q" value={search.q} />}
        {search.city && <input type="hidden" name="city" value={search.city} />}
        {search.cityId && <input type="hidden" name="cityId" value={search.cityId} />}
        {search.country && <input type="hidden" name="country" value={search.country} />}
        {search.date && <input type="hidden" name="date" value={search.date} />}
        {search.guests !== 1 && <input type="hidden" name="guests" value={search.guests} />}
        {search.sort !== defaultSort(search) && <input type="hidden" name="sort" value={search.sort} />}

        <fieldset>
          <legend className="mb-1 font-semibold">Price per person</legend>
          <div className="relative mx-1.5 mt-4 mb-2 h-1.5 rounded-full bg-surface-2" aria-hidden>
            <span className="absolute inset-y-0 rounded-full bg-primary" style={{ left: `${left}%`, right: `${right}%` }} />
          </div>
          <div className="mt-3 flex gap-2.5">
            <label className="flex-1 text-xs text-muted-foreground">
              Min (€)
              <Input name="minPrice" inputMode="numeric" type="number" min={0} step={1} value={min} placeholder="0" onChange={(event) => setMin(event.target.value)} onBlur={() => applyPrice()} className="mt-1" />
            </label>
            <label className="flex-1 text-xs text-muted-foreground">
              Max (€)
              <Input name="maxPrice" inputMode="numeric" type="number" min={0} step={1} value={max} placeholder="Any" onChange={(event) => setMax(event.target.value)} onBlur={() => applyPrice()} className="mt-1" />
            </label>
          </div>
        </fieldset>

        {(search.city || search.cityId) && (
          <fieldset>
            <legend className="mb-2.5 font-semibold">Distance from {search.city ?? "the place"}</legend>
            <div className="flex flex-wrap gap-2">
              {RADIUS_OPTIONS.map((km) => {
                const on = (search.radiusKm ?? DEFAULT_RADIUS_KM) === km;
                return (
                  <label key={km} className={cn("inline-flex min-h-9 cursor-pointer items-center rounded-full border px-3.5 text-sm font-medium transition-colors has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-primary", on ? "border-ink bg-ink text-bg" : "border-line bg-surface hover:border-muted-foreground")}>
                    <input type="radio" name="radiusKm" value={km} checked={on} onChange={() => go(searchHref(search, { radiusKm: km, page: 1 }))} className="sr-only" />
                    {km} km
                  </label>
                );
              })}
            </div>
          </fieldset>
        )}

        <fieldset>
          <legend className="mb-2.5 font-semibold">Booking type</legend>
          <div className="flex flex-wrap gap-2">
            {MODES.map((mode) => {
              const on = search.mode === mode.value;
              return (
                <label key={mode.label} className={cn("inline-flex min-h-9 cursor-pointer items-center rounded-full border px-3.5 text-sm font-medium transition-colors has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-primary", on ? "border-ink bg-ink text-bg" : "border-line bg-surface hover:border-muted-foreground")}>
                  <input type="radio" name="mode" value={mode.value ?? ""} checked={on} onChange={() => go(searchHref(search, { mode: mode.value, page: 1 }))} className="sr-only" />
                  {mode.label}
                </label>
              );
            })}
          </div>
          <p className="mt-2.5 text-[13px] text-muted-foreground">Shared: pay per seat. Private: the whole trip for your group.</p>
        </fieldset>

        <noscript>
          <Button type="submit" variant="outline" className="w-full">Apply filters</Button>
        </noscript>
      </form>
    </aside>
  );
}

/** Sort order, next to the result count. Changing it keeps every filter and goes back to page 1. */
export function SortSelect({ search }: { search: CatalogSearch }) {
  const { go } = useSearchNavigation();
  return (
    <label className="flex items-center gap-2.5 text-sm">
      <span className="text-muted-foreground">Sort by</span>
      <select
        value={search.sort}
        onChange={(event) => go(searchHref(search, { sort: event.target.value as TourSort, page: 1 }))}
        className="h-11 rounded-control border border-line bg-surface pr-7 pl-3 text-ink"
      >
        {sortOptions(search).map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
      </select>
    </label>
  );
}
