import type { Metadata } from "next";
import Link from "next/link";

import { AdminTabs } from "@/components/admin/admin-tabs";
import { ReviewModerationActions } from "@/components/admin/review-moderation-actions";
import { Stars } from "@/components/reviews/stars";
import { StatusPill } from "@/components/ui/status-pill";
import type { ModerationReview, PagedResponse } from "@/lib/api/contracts";
import { unwrap } from "@/lib/api/client";
import { serverApi } from "@/lib/api/server";
import { requireAdmin } from "@/lib/auth/dal";
import { readAccessToken } from "@/lib/auth/session";
import { postedLabel, reasonLabel } from "@/lib/reviews";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Reviews · Admin", robots: { index: false } };
export const instant = false;

const PAGE_SIZE = 20;
const FILTERS = [
  { id: "reported", label: "Reported", query: { reported: true } },
  { id: "hidden", label: "Hidden", query: { hidden: true } },
  { id: "all", label: "All", query: {} },
] as const;

type Props = { searchParams: Promise<{ filter?: string; page?: string }> };

export default async function AdminReviewsPage({ searchParams }: Props) {
  await requireAdmin("/admin/reviews");
  const params = await searchParams;
  // Reported first: that's the moderators' to-do list.
  const filter = FILTERS.find((item) => item.id === params.filter) ?? FILTERS[0];
  const page = Math.max(1, Number.parseInt(params.page ?? "1", 10) || 1);
  const result = await unwrap<PagedResponse<ModerationReview>>(
    serverApi(await readAccessToken()).GET("/api/v1/admin/reviews", { params: { query: { ...filter.query, page, pageSize: PAGE_SIZE } } }),
  );
  const href = (changes: { filter?: string; page?: number }) => `/admin/reviews?${new URLSearchParams({ filter: changes.filter ?? filter.id, page: String(changes.page ?? 1) })}`;

  return (
    <div className="mx-auto max-w-page px-4 pt-8 pb-20 sm:px-6">
      <p className="label-caps">Admin</p>
      <h1 className="mt-1 text-[32px] font-bold">Reviews</h1>
      <AdminTabs current="/admin/reviews" />
      <p className="mt-4 text-sm text-muted-foreground">Reports never hide a review on their own. You decide; every decision is written to the audit log.</p>
      <nav aria-label="Review filter" className="mt-4 mb-5 flex flex-wrap gap-2">
        {FILTERS.map((item) => (
          <Link key={item.id} href={href({ filter: item.id })} aria-current={item.id === filter.id ? "page" : undefined} className={cn("inline-flex min-h-9 items-center rounded-full border px-3.5 text-sm font-medium", item.id === filter.id ? "border-ink bg-ink text-bg" : "border-line bg-surface text-ink hover:border-muted-foreground")}>
            {item.label}
          </Link>
        ))}
      </nav>

      {result.items.length ? (
        <ul className="flex flex-col gap-4">
          {result.items.map((review) => (
            <li key={review.id} className="flex flex-wrap items-start gap-4 rounded-card border border-line bg-surface p-5">
              <article aria-label={`Review by ${review.authorName}`} className="flex min-w-0 flex-[1_1_420px] flex-col gap-2">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <Stars rating={review.rating} />
                  <span className="font-semibold">{review.authorName}</span>
                  <Link href={`/tours/${review.tourId}`} className="text-sm text-primary hover:text-primary-hover">{review.tourTitle}</Link>
                  <span className="text-[13px] text-muted-foreground">{review.agencyName} · {postedLabel(review.createdAt)}</span>
                </div>
                {review.comment ? <p className="leading-relaxed whitespace-pre-line">{review.comment}</p> : <p className="text-sm text-muted-foreground">No comment, just a rating.</p>}
                {review.agencyReply && <p className="text-sm text-muted-foreground">Agency replied: “{review.agencyReply}”</p>}
                <div className="flex flex-wrap items-center gap-2">
                  {review.openReports > 0 && (
                    <StatusPill tone="pending">{review.openReports} open {review.openReports === 1 ? "report" : "reports"}: {review.reportReasons.map(reasonLabel).join(", ")}</StatusPill>
                  )}
                  {review.isHidden && <StatusPill tone="cancelled">Hidden: {review.hiddenReason}</StatusPill>}
                </div>
              </article>
              <ReviewModerationActions review={review} />
            </li>
          ))}
        </ul>
      ) : (
        <p className="rounded-card border border-dashed border-line bg-surface p-6 text-muted-foreground">{filter.id === "reported" ? "Nothing reported. All clear." : "No reviews here."}</p>
      )}

      {result.totalPages > 1 && (
        <nav aria-label="Pagination" className="mt-6 flex items-center justify-center gap-3 text-sm">
          {result.page > 1 && <Link href={href({ page: result.page - 1 })} className="font-semibold text-primary">← Previous</Link>}
          <span className="text-muted-foreground">Page {result.page} of {result.totalPages}</span>
          {result.page < result.totalPages && <Link href={href({ page: result.page + 1 })} className="font-semibold text-primary">Next →</Link>}
        </nav>
      )}
    </div>
  );
}
