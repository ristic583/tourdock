import type { RatingSummary, ReportReason } from "@/lib/api/contracts";

/** Reviews per page on the tour page (the server loads page 1, the client the rest: both must agree). */
export const REVIEWS_PAGE_SIZE = 5;

export const REPORT_REASONS: { value: ReportReason; label: string }[] = [
  { value: "Spam", label: "Spam or advertising" },
  { value: "Offensive", label: "Offensive or abusive" },
  { value: "PersonalInformation", label: "Shares personal information" },
  { value: "NotAboutTheTour", label: "Not about this tour" },
  { value: "Other", label: "Something else" },
];

export function reasonLabel(reason: ReportReason): string {
  return REPORT_REASONS.find((r) => r.value === reason)?.label ?? reason;
}

/** Star rows for the summary, 5 to 1, each with its share of all reviews (0–100, rounded). */
export function distributionRows(summary: RatingSummary): { stars: number; count: number; percent: number }[] {
  const d = summary.distribution;
  const counts = [d.five, d.four, d.three, d.two, d.one];
  return counts.map((count, index) => ({ stars: 5 - index, count, percent: summary.count ? Math.round((count / summary.count) * 100) : 0 }));
}

/** "June 2026" for when the reviewer travelled. */
export function travelledLabel(travelledAt: string, timeZone?: string): string {
  return new Intl.DateTimeFormat("en-GB", { month: "long", year: "numeric", timeZone: timeZone ?? "UTC" }).format(new Date(travelledAt));
}

/** "7 Nov 2026" for when the review was posted. */
export function postedLabel(createdAt: string): string {
  return new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(createdAt));
}

/** Whether a completed booking can still get a review (the API sends reviewableUntil for completed bookings). */
export function canStillReview(reviewableUntil: string | null | undefined, now: number): boolean {
  return Boolean(reviewableUntil) && new Date(reviewableUntil!).getTime() > now;
}

export function ratingWords(rating: number): string {
  return ["", "Poor", "Fair", "Good", "Very good", "Excellent"][rating] ?? "";
}
