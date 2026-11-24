"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import type { PublicReview, ReviewSort, TourReviews as TourReviewsData } from "@/lib/api/contracts";
import { reviewsApi } from "@/lib/api/reviews-client";
import { distributionRows, postedLabel, REVIEWS_PAGE_SIZE, travelledLabel } from "@/lib/reviews";

import { ReportReviewDialog } from "./report-review-dialog";
import { Stars } from "./stars";

const SORTS: { value: ReviewSort; label: string }[] = [
  { value: "Newest", label: "Newest" },
  { value: "Highest", label: "Highest rated" },
  { value: "Lowest", label: "Lowest rated" },
];

/**
 * Rating summary and reviews on the tour page. The first page comes from the server; sorting and "Show more" load
 * through the BFF. Only reviews from completed bookings exist, so every one is from someone who went.
 */
export function TourReviews({ tourId, initial, timeZone, signedIn }: { tourId: string; initial: TourReviewsData; timeZone: string; signedIn: boolean }) {
  const [sort, setSort] = useState<ReviewSort>("Newest");
  const [items, setItems] = useState<PublicReview[]>(initial.reviews.items);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(initial.reviews.totalCount);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const summary = initial.summary;
  // Your own reviews get no "Report" button (the API refuses it anyway). Fetched only when signed in.
  const mine = useQuery({ queryKey: ["my-reviews"], queryFn: reviewsApi.mine, enabled: signedIn, staleTime: 60_000 });
  const myReviewIds = new Set(mine.data?.map((review) => review.id));

  async function load(nextSort: ReviewSort, nextPage: number) {
    setLoading(true);
    setError(undefined);
    try {
      const result = await reviewsApi.forTour(tourId, nextSort, nextPage, REVIEWS_PAGE_SIZE);
      setItems((current) => (nextPage === 1 ? result.reviews.items : [...current, ...result.reviews.items]));
      setTotal(result.reviews.totalCount);
      setPage(nextPage);
      setSort(nextSort);
    } catch {
      setError("We couldn't load more reviews. Please try again.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <section aria-labelledby="reviews-heading">
      <h2 id="reviews-heading" className="mb-4 text-2xl font-bold">Reviews</h2>
      {summary.count === 0 ? (
        <p className="text-muted-foreground">No reviews yet. Travelers can review a tour after their trip.</p>
      ) : (
        <>
          <div className="flex flex-wrap items-center gap-x-10 gap-y-4 rounded-card border border-line bg-surface p-5">
            <div>
              <p className="font-display text-5xl font-bold">{summary.average?.toFixed(1)}</p>
              <Stars rating={summary.average ?? 0} size="md" className="mt-1" />
              <p className="mt-1 text-sm text-muted-foreground">{summary.count} {summary.count === 1 ? "review" : "reviews"}</p>
            </div>
            <ul className="flex min-w-[220px] flex-1 flex-col gap-1.5" aria-label="Ratings breakdown">
              {distributionRows(summary).map((row) => (
                <li key={row.stars} className="flex items-center gap-3 text-sm">
                  <span className="w-12 whitespace-nowrap">{row.stars} star{row.stars === 1 ? "" : "s"}</span>
                  <span className="h-2 flex-1 overflow-hidden rounded-full bg-surface-2" aria-hidden>
                    <span className="block h-full rounded-full bg-star" style={{ width: `${row.percent}%` }} />
                  </span>
                  <span className="w-8 text-right text-muted-foreground">{row.count}</span>
                </li>
              ))}
            </ul>
          </div>

          <div className="mt-5 mb-3 flex items-center justify-between gap-3">
            <p className="text-sm text-muted-foreground">From travelers who completed this tour</p>
            <label className="flex items-center gap-2 text-sm">
              <span className="text-muted-foreground">Sort</span>
              <select value={sort} disabled={loading} onChange={(event) => load(event.target.value as ReviewSort, 1)} className="rounded-control border border-line bg-surface px-2 py-1.5 font-semibold">
                {SORTS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
            </label>
          </div>

          <ul className="flex flex-col divide-y divide-line">
            {items.map((review) => (
              <li key={review.id} className="py-4">
                <article aria-label={`Review by ${review.authorName}`}>
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                    <Stars rating={review.rating} />
                    <span className="font-semibold">{review.authorName}</span>
                    <span className="text-[13px] text-muted-foreground">
                      Travelled {travelledLabel(review.travelledAt, timeZone)} · {postedLabel(review.createdAt)}{review.editedAt ? " · edited" : ""}
                    </span>
                    <span className="flex-1" />
                    {signedIn && mine.isSuccess && !myReviewIds.has(review.id) && <ReportReviewDialog reviewId={review.id} />}
                  </div>
                  {review.comment && <p className="mt-2 max-w-[680px] leading-relaxed whitespace-pre-line">{review.comment}</p>}
                  {review.agencyReply && (
                    <div className="mt-3 max-w-[640px] rounded-control border-l-4 border-primary bg-surface-2 px-4 py-3 text-sm">
                      <p className="font-semibold">Reply from the agency</p>
                      <p className="mt-1 whitespace-pre-line">{review.agencyReply}</p>
                    </div>
                  )}
                </article>
              </li>
            ))}
          </ul>
          <FormAlert message={error} />
          {items.length < total && (
            <Button variant="outline" className="mt-3" disabled={loading} onClick={() => load(sort, page + 1)}>
              {loading ? "Loading…" : `Show more reviews (${total - items.length} left)`}
            </Button>
          )}
        </>
      )}
    </section>
  );
}
