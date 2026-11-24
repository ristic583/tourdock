import "server-only";

import { notFound } from "next/navigation";
import { cache } from "react";

import type { AgencyBooking, AgencyStats, AgencyTour, MyAgency, Slot, Vehicle } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { ApiError } from "@/lib/api/problem";
import { serverApi } from "@/lib/api/server";
import { getMyAgencies, requireUser } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";

/**
 * Data for the agency back office, read on the server with the signed-in user's token. Every page goes through
 * `requireAgency`, so a user who isn't a member gets a 404 (the API enforces this too).
 */
export const requireAgency = cache(async (agencyId: string, returnTo: string): Promise<MyAgency> => {
  await requireUser(returnTo);
  const agency = (await getMyAgencies()).find((item) => item.id === agencyId);
  if (!agency) notFound();
  return agency;
});

async function api() {
  return serverApi(await readAccessToken());
}

function orNotFound<T>(promise: Promise<T>): Promise<T> {
  return promise.catch((error) => {
    if (error instanceof ApiError && (error.status === 404 || error.status === 400)) notFound();
    throw error;
  });
}

export const listVehicles = cache(async (agencyId: string): Promise<Vehicle[]> =>
  unwrap<Vehicle[]>((await api()).GET("/api/v1/agencies/{agencyId}/vehicles", { params: { path: { agencyId } } })),
);

export const listAgencyTours = cache(async (agencyId: string): Promise<AgencyTour[]> =>
  unwrap<AgencyTour[]>((await api()).GET("/api/v1/agencies/{agencyId}/tours", { params: { path: { agencyId } } })),
);

export const getAgencyTour = cache(async (agencyId: string, tourId: string): Promise<AgencyTour> =>
  orNotFound(unwrap<AgencyTour>((await api()).GET("/api/v1/agencies/{agencyId}/tours/{tourId}", { params: { path: { agencyId, tourId } } }))),
);

/** Departures between two local dates (the API allows at most 120 days). */
export const listSlots = cache(async (agencyId: string, from: string, to: string): Promise<Slot[]> =>
  unwrap<Slot[]>((await api()).GET("/api/v1/agencies/{agencyId}/slots", { params: { path: { agencyId }, query: { from, to } } })),
);

export const listAgencyBookings = cache(async (agencyId: string, from: string, to: string): Promise<AgencyBooking[]> =>
  unwrap<AgencyBooking[]>((await api()).GET("/api/v1/agencies/{agencyId}/bookings", { params: { path: { agencyId }, query: { from, to } } })),
);

/** KPIs for departures between two local dates, inclusive (at most 366 days). */
export const getAgencyStats = cache(async (agencyId: string, from: string, to: string): Promise<AgencyStats> =>
  unwrap<AgencyStats>((await api()).GET("/api/v1/agencies/{agencyId}/stats", { params: { path: { agencyId }, query: { from, to } } })),
);

/** The time zone the agency works in: its first tour's, else UTC. Used for "today" and week boundaries. */
export function agencyTimeZone(tours: AgencyTour[]): string {
  return tours[0]?.location.timeZoneId ?? "UTC";
}
