import { z } from "zod";

import type { AgencyTour, BookingMode, RefundTier } from "@/lib/api/contracts";

/** A price typed by the agency: above 0 and at most cents (the API stores 2 decimals). */
export function priceProblem(value: string): string | undefined {
  const text = value.trim();
  if (text === "") return undefined;
  if (!Number.isFinite(Number(text)) || Number(text) <= 0) return "Enter a price above 0, or leave it empty.";
  if (!/^\d+(\.\d{1,2})?$/.test(text)) return "Use at most 2 decimals, e.g. 49.50.";
  return undefined;
}

const price = z
  .string()
  .trim()
  .superRefine((value, context) => {
    const problem = priceProblem(value);
    if (problem) context.addIssue({ code: "custom", message: problem });
  });

/** What is wrong with a seasonal price before it is sent, in the order the form reads. */
export function seasonProblem(season: { from: string; to: string; perPerson: string; privately: string }): string | undefined {
  if (!season.from || !season.to) return "Pick the first and last day of the season.";
  if (season.to < season.from) return "The last day must be on or after the first day.";
  if (!season.perPerson.trim() && !season.privately.trim()) return "Enter at least one seasonal price.";
  return priceProblem(season.perPerson) ?? priceProblem(season.privately);
}

const PIN_MISSING = "Pick the city, then put the pin on the meeting point.";

/** Title, description, place and size: the same fields for creating and editing a tour. */
export const detailsSchema = z.object({
  title: z.string().trim().min(3, "Give the tour a title.").max(200),
  description: z.string().trim().min(10, "Describe the tour in a sentence or two.").max(5000),
  countryCode: z.string().trim().regex(/^[A-Za-z]{2}$/, "Two letters, e.g. HR."),
  city: z.string().trim().min(1, "Enter the city.").max(100),
  address: z.string().trim().min(1, "Enter the meeting point.").max(300),
  // Set by picking the city and placing the pin on the map; a new tour starts without a point (NaN).
  latitude: z.number({ error: PIN_MISSING }).min(-90, PIN_MISSING).max(90, PIN_MISSING),
  longitude: z.number({ error: PIN_MISSING }).min(-180, PIN_MISSING).max(180, PIN_MISSING),
  timeZoneId: z.string().min(1, "Pick a time zone."),
  durationMinutes: z.coerce.number<number>({ error: "Enter the duration in minutes." }).int("Whole minutes only.").min(15, "At least 15 minutes.").max(20160, "At most 14 days."),
  maxGuests: z.coerce.number<number>({ error: "Enter a number of guests." }).int("Whole guests only.").min(1, "At least 1 guest.").max(500, "At most 500 guests."),
});

/** Booking mode and base prices. Shared needs a price per person, Private a trip price, Both needs both. */
export const pricingSchema = z
  .object({ bookingMode: z.enum(["Shared", "Private", "Both"]), pricePerPerson: price, privatePrice: price })
  .superRefine((value, context) => {
    if (value.bookingMode !== "Private" && !value.pricePerPerson) context.addIssue({ code: "custom", path: ["pricePerPerson"], message: "Shared tours need a price per person." });
    if (value.bookingMode !== "Shared" && !value.privatePrice) context.addIssue({ code: "custom", path: ["privatePrice"], message: "Private trips need a trip price." });
    // Same rule as the API: the whole trip can't cost less than one seat on it.
    const perPerson = Number(value.pricePerPerson);
    const privately = Number(value.privatePrice);
    if (value.bookingMode === "Both" && value.pricePerPerson && value.privatePrice && !priceProblem(value.pricePerPerson) && !priceProblem(value.privatePrice) && privately < perPerson) {
      context.addIssue({ code: "custom", path: ["privatePrice"], message: "The private trip price can't be lower than the price per person." });
    }
  });

export const createTourSchema = detailsSchema.and(pricingSchema);

export type DetailsValues = z.infer<typeof detailsSchema>;
export type PricingValues = z.infer<typeof pricingSchema>;

export function detailsBody(values: DetailsValues) {
  return {
    title: values.title,
    description: values.description,
    location: {
      countryCode: values.countryCode.toUpperCase(),
      city: values.city,
      address: values.address,
      latitude: values.latitude,
      longitude: values.longitude,
      timeZoneId: values.timeZoneId,
    },
    durationMinutes: values.durationMinutes,
    maxGuests: values.maxGuests,
  };
}

/** Empty or not offered for this mode: sent as null. */
export function pricingBody(values: PricingValues) {
  const amount = (text: string, offered: boolean) => (offered && text ? Number(text) : null);
  return {
    bookingMode: values.bookingMode as BookingMode,
    pricePerPerson: amount(values.pricePerPerson, values.bookingMode !== "Private"),
    privatePrice: amount(values.privatePrice, values.bookingMode !== "Shared"),
  };
}

export function detailsDefaults(tour?: AgencyTour): DetailsValues {
  return {
    title: tour?.title ?? "",
    description: tour?.description ?? "",
    countryCode: tour?.location.countryCode ?? "HR",
    city: tour?.location.city ?? "",
    address: tour?.location.address ?? "",
    latitude: tour?.location.latitude ?? Number.NaN,
    longitude: tour?.location.longitude ?? Number.NaN,
    timeZoneId: tour?.location.timeZoneId ?? "Europe/Zagreb",
    durationMinutes: tour?.durationMinutes ?? 180,
    maxGuests: tour?.maxGuests ?? 8,
  };
}

export function pricingDefaults(tour?: AgencyTour): PricingValues {
  return {
    bookingMode: tour?.bookingMode ?? "Shared",
    pricePerPerson: tour?.pricePerPerson ? String(tour.pricePerPerson.amount) : "",
    privatePrice: tour?.privatePrice ? String(tour.privatePrice.amount) : "",
  };
}

/** Problems with a refund policy, or none. Tiers need distinct hours and refunds that don't grow closer to departure. */
export function policyProblems(tiers: RefundTier[]): string[] {
  const problems: string[] = [];
  if (!tiers.length) problems.push("Add at least one refund tier.");
  if (tiers.some((tier) => !Number.isInteger(tier.hoursBeforeStart) || tier.hoursBeforeStart < 0)) problems.push("Hours must be whole numbers, 0 or more.");
  if (tiers.some((tier) => !Number.isFinite(tier.refundPercent) || tier.refundPercent < 0 || tier.refundPercent > 100)) problems.push("Refunds are between 0 and 100%.");
  if (new Set(tiers.map((tier) => tier.hoursBeforeStart)).size !== tiers.length) problems.push("Each tier needs different hours.");
  const sorted = [...tiers].sort((a, b) => b.hoursBeforeStart - a.hoursBeforeStart);
  if (sorted.some((tier, index) => index > 0 && tier.refundPercent > sorted[index - 1].refundPercent)) problems.push("Refunds can't grow closer to departure.");
  return problems;
}

/** All IANA time zones the browser knows, for the location picker. */
export function timeZones(): string[] {
  try {
    return Intl.supportedValuesOf("timeZone");
  } catch {
    return ["UTC", "Europe/Zagreb"];
  }
}
