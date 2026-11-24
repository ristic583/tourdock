import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";

import { FilterSidebar, SortSelect } from "@/components/catalog/filter-sidebar";
import { MobileDisclosure } from "@/components/catalog/mobile-disclosure";
import { Pagination } from "@/components/catalog/pagination";
import { SearchBar } from "@/components/catalog/search-bar";
import { TourCard } from "@/components/catalog/tour-card";
import { Button } from "@/components/ui/button";
import { PAGE_SIZE, searchTours } from "@/lib/catalog";
import { getDisplayRate } from "@/lib/display-currency";
import { DEFAULT_RADIUS_KM, countryLabel, defaultSort, hasFilters, parseCatalogSearch, placeLabel, searchHref, type CatalogSearch } from "@/lib/catalog-search";

export const metadata: Metadata = { title: "Find a tour", description: "Browse boat trips, fishing charters and guided excursions from local agencies." };
export const instant = false;

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

function summary(search: CatalogSearch): string {
  const parts = [
    search.date ? new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", timeZone: "UTC" }).format(new Date(`${search.date}T00:00:00Z`)) : "Any date",
    `${search.guests} ${search.guests === 1 ? "guest" : "guests"}`,
    ...(search.city || search.cityId ? [`within ${search.radiusKm ?? DEFAULT_RADIUS_KM} km`] : []),
    "available departures only",
  ];
  return parts.join(" · ");
}

/** What the description did to the order: best matches first, or the usual order when smart search was unavailable. */
function semanticNote(search: CatalogSearch, semantic: boolean | null): string | undefined {
  if (!search.q || semantic === null) return undefined;
  if (!semantic) return "Smart search is unavailable right now, so these are in the usual order.";
  return search.sort === "Relevance" ? `Best matches for “${search.q}” first.` : `Matching “${search.q}”, sorted as you chose.`;
}

export default async function ToursPage({ searchParams }: Props) {
  const search = parseCatalogSearch(await searchParams);
  const [results, rate] = await Promise.all([searchTours(search), getDisplayRate()]);
  // A page past the end (old bookmark, fewer results now, hand-edited URL) goes to the last page that exists.
  const lastPage = Math.max(results.totalPages, 1);
  if (search.page > lastPage) redirect(searchHref(search, { page: lastPage }));
  const from = results.totalCount ? (results.page - 1) * PAGE_SIZE + 1 : 0;
  const to = Math.min(results.page * PAGE_SIZE, results.totalCount);
  const activeFilters = [search.minPrice !== undefined || search.maxPrice !== undefined, Boolean(search.mode)].filter(Boolean).length;
  const where = search.city ? ` near ${search.city}` : search.country ? ` in ${countryLabel(search.country)}` : "";
  const matchNote = semanticNote(search, results.semantic);
  const heading = `${results.totalCount} ${results.totalCount === 1 ? "tour" : "tours"}${where}`;

  return (
    <div className="mx-auto max-w-page px-4 pt-8 pb-16 sm:px-6 sm:pt-10">
      <h1 className="sr-only">Find a tour</h1>
      <MobileDisclosure
        summary={
          <>
            <span className="block truncate font-semibold">{placeLabel(search) ?? "Anywhere"}</span>
            <span className="block truncate text-sm text-muted-foreground">{summary(search).split(" · ").slice(0, 2).join(" · ")} · edit search</span>
          </>
        }
      >
        <SearchBar search={search} keepFilters />
      </MobileDisclosure>

      <div className="mt-4 grid grid-cols-[minmax(0,1fr)] gap-4 lg:mt-8 lg:flex lg:flex-wrap lg:items-start lg:gap-8">
        <div className="max-w-full flex-[1_1_260px] lg:max-w-[300px]">
          <MobileDisclosure
            summary={
              <>
                <span className="block font-semibold">Filters{activeFilters ? ` · ${activeFilters} on` : ""}</span>
                <span className="block truncate text-sm text-muted-foreground">Price and booking type</span>
              </>
            }
          >
            <FilterSidebar key={`${search.minPrice}-${search.maxPrice}`} search={search} />
          </MobileDisclosure>
        </div>

        <section aria-labelledby="results-heading" className="min-w-0 flex-[999_1_560px]">
          <div className="mb-5 flex flex-wrap items-end justify-between gap-4">
            <div>
              <h2 id="results-heading" className="text-[26px] font-bold">{heading}</h2>
              <p className="mt-1 text-sm text-muted-foreground">{summary(search)}</p>
              {matchNote && <p className="mt-1 text-sm text-muted-foreground" role="status">{matchNote}</p>}
            </div>
            <SortSelect search={search} />
          </div>

          {results.items.length ? (
            <div className="grid grid-cols-[repeat(auto-fill,minmax(270px,1fr))] gap-[22px]">
              {results.items.map((tour, index) => (
                <TourCard key={tour.id} tour={tour} index={index} guests={search.guests} date={search.date} rate={rate} />
              ))}
            </div>
          ) : (
            <div className="rounded-card border border-dashed border-line bg-surface p-8">
              <h3 className="text-xl font-bold">Nothing matched this search</h3>
              <p className="mt-2 text-muted-foreground">Try another destination, a different date, fewer guests or a wider price range.</p>
              <div className="mt-5 flex flex-wrap gap-3">
                {hasFilters(search) && (
                  <Button asChild variant="outline">
                    <Link href={searchHref(search, { minPrice: undefined, maxPrice: undefined, mode: undefined, sort: defaultSort(search), page: 1 })}>Reset filters</Link>
                  </Button>
                )}
                <Button asChild variant="outline"><Link href="/tours">See all tours</Link></Button>
              </div>
            </div>
          )}

          <Pagination search={search} totalPages={results.totalPages} />
          {results.totalCount > 0 && (
            <p className="mt-3 text-center text-[13px] text-muted-foreground">Showing {from}–{to} of {results.totalCount}</p>
          )}
        </section>
      </div>
    </div>
  );
}
