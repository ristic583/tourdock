import type { Metadata } from "next";
import Link from "next/link";

import { ReplyForm } from "@/components/reviews/reply-form";
import { Stars } from "@/components/reviews/stars";
import { StatusPill } from "@/components/ui/status-pill";
import { requireAgency } from "@/lib/agency";
import type { AgencyReview, PagedResponse } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { readAccessToken } from "@/lib/auth/session";
import { postedLabel, travelledLabel } from "@/lib/reviews";

// Private, per-user back-office data: rendered on demand, never prerendered.
export const instant = false;

export const metadata: Metadata = { title: "Agency reviews" };

const PAGE_SIZE = 20;

type Props = { params: Promise<{ agencyId: string }>; searchParams: Promise<{ page?: string }> };

export default async function AgencyReviewsPage({ params, searchParams }: Props) {
  const { agencyId } = await params;
  const agency = await requireAgency(agencyId, `/agency/${agencyId}/reviews`);
  const page = Math.max(1, Number.parseInt((await searchParams).page ?? "1", 10) || 1);
  const result = await unwrap<PagedResponse<AgencyReview>>(
    serverApi(await readAccessToken()).GET("/api/v1/agencies/{agencyId}/reviews", { params: { path: { agencyId }, query: { page, pageSize: PAGE_SIZE } } }),
  );
  const isManager = agency.myRole === "Manager";
  const pages = Math.max(1, Math.ceil(result.totalCount / PAGE_SIZE));

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-3xl font-bold">Reviews</h1>
        <p className="mt-1 text-muted-foreground">
          What travelers say about your tours. {isManager ? "Reply publicly to thank them or explain what happened." : "Managers can reply publicly."} Only our moderators can hide a review.
        </p>
      </div>

      {result.items.length === 0 ? (
        <p className="rounded-card border border-dashed border-line bg-surface p-8 text-muted-foreground">No reviews yet. Travelers are asked for one after their trip.</p>
      ) : (
        <ul className="flex flex-col gap-4">
          {result.items.map((review) => (
            <li key={review.id} className="rounded-card border border-line bg-surface p-5">
              <article aria-label={`Review by ${review.authorName}`} className="flex flex-col gap-2">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <Stars rating={review.rating} />
                  <span className="font-semibold">{review.authorName}</span>
                  <Link href={`/tours/${review.tourId}`} className="text-sm text-primary hover:text-primary-hover">{review.tourTitle}</Link>
                  <span className="text-[13px] text-muted-foreground">Travelled {travelledLabel(review.travelledAt)} · {postedLabel(review.createdAt)}</span>
                  {review.isHidden && <StatusPill tone="cancelled">Hidden by moderators</StatusPill>}
                </div>
                {review.comment ? <p className="max-w-[720px] leading-relaxed whitespace-pre-line">{review.comment}</p> : <p className="text-sm text-muted-foreground">No comment, just a rating.</p>}
                {review.agencyReply && (
                  <div className="max-w-[640px] rounded-control border-l-4 border-primary bg-surface-2 px-4 py-3 text-sm">
                    <p className="font-semibold">Your reply</p>
                    <p className="mt-1 whitespace-pre-line">{review.agencyReply}</p>
                  </div>
                )}
                {isManager && <ReplyForm agencyId={agencyId} reviewId={review.id} reply={review.agencyReply} />}
              </article>
            </li>
          ))}
        </ul>
      )}

      {pages > 1 && (
        <nav aria-label="Pages" className="flex items-center gap-3 text-sm">
          {page > 1 && <Link href={`/agency/${agencyId}/reviews?page=${page - 1}`} className="text-primary">Previous</Link>}
          <span className="text-muted-foreground">Page {page} of {pages}</span>
          {page < pages && <Link href={`/agency/${agencyId}/reviews?page=${page + 1}`} className="text-primary">Next</Link>}
        </nav>
      )}
    </div>
  );
}
