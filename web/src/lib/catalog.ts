import "server-only";

import { cache } from "react";

import type { Departure, PublicTour, TourSearchResponse } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import type { CatalogSearch } from "@/lib/catalog-search";

export const PAGE_SIZE = 12;

export async function searchTours(search: CatalogSearch): Promise<TourSearchResponse> {
  return unwrap<TourSearchResponse>(
    serverApi().GET("/api/v1/tours", {
      params: {
        query: {
          Q: search.q,
          City: search.city,
          CityId: search.cityId,
          RadiusKm: search.radiusKm,
          Country: search.country,
          Date: search.date,
          Guests: search.guests,
          MinPrice: search.minPrice,
          MaxPrice: search.maxPrice,
          Mode: search.mode,
          Sort: search.sort,
          Page: search.page,
          PageSize: PAGE_SIZE,
        },
      },
    }),
  );
}

/**
 * One request per tour and render: `generateMetadata` and the page both call this,
 * and React's `cache()` makes the second call reuse the first.
 */
export const getTour = cache(async (tourId: string): Promise<PublicTour> =>
  unwrap<PublicTour>(serverApi().GET("/api/v1/tours/{tourId}", { params: { path: { tourId } } })),
);

/**
 * Bookable departures for a party size. `from`/`to` are local dates at the tour (the API defaults to 30 days
 * from today). Search cards don't need this: each result carries its next departure with live seats.
 */
export const getDepartures = cache(async (tourId: string, guests: number, from?: string, to?: string): Promise<Departure[]> =>
  unwrap<Departure[]>(
    serverApi().GET("/api/v1/tours/{tourId}/departures", {
      params: { path: { tourId }, query: { guests, from, to } },
    }),
  ),
);
