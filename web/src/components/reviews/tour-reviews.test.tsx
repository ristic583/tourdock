import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render as baseRender, screen, within } from "@testing-library/react";
import type { ReactElement } from "react";
import { describe, expect, it, vi } from "vitest";

import type { MyReview, TourReviews as Data } from "@/lib/api/contracts";
import { reviewsApi } from "@/lib/api/reviews-client";

import { TourReviews } from "./tour-reviews";

const data: Data = {
  summary: { average: 4.5, count: 2, distribution: { one: 0, two: 0, three: 0, four: 1, five: 1 } },
  reviews: {
    items: [
      {
        id: "r1",
        rating: 5,
        comment: "Dolphins at sunset!",
        authorName: "Ana M.",
        createdAt: "2026-06-10T10:00:00Z",
        editedAt: null,
        travelledAt: "2026-06-05T16:00:00Z",
        agencyReply: "Thank you, come back soon!",
        agencyReplyAt: "2026-06-11T08:00:00Z",
      },
      {
        id: "r2",
        rating: 4,
        comment: null,
        authorName: "Marko",
        createdAt: "2026-06-09T10:00:00Z",
        editedAt: "2026-06-09T12:00:00Z",
        travelledAt: "2026-06-04T16:00:00Z",
        agencyReply: null,
        agencyReplyAt: null,
      },
    ],
    page: 1,
    pageSize: 5,
    totalCount: 7,
    totalPages: 2,
  },
};

function render(ui: ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const wrap = (node: ReactElement) => <QueryClientProvider client={client}>{node}</QueryClientProvider>;
  const result = baseRender(wrap(ui));
  return { ...result, rerender: (next: ReactElement) => result.rerender(wrap(next)) };
}

describe("TourReviews", () => {
  it("shows the summary, the reviews and the agency's reply", () => {
    render(<TourReviews tourId="t1" initial={data} timeZone="Europe/Zagreb" signedIn={false} />);

    expect(screen.getByText("4.5")).toBeInTheDocument();
    expect(screen.getByText("2 reviews")).toBeInTheDocument();
    const first = screen.getByRole("article", { name: "Review by Ana M." });
    expect(within(first).getByRole("img", { name: "5 out of 5 stars" })).toBeInTheDocument();
    expect(within(first).getByText(/Travelled June 2026/)).toBeInTheDocument();
    expect(within(first).getByText("Thank you, come back soon!")).toBeInTheDocument();
    expect(within(screen.getByRole("article", { name: "Review by Marko" })).getByText(/edited/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Show more reviews (5 left)" })).toBeInTheDocument();
  });

  it("only offers reporting to signed-in visitors, and not on their own review", async () => {
    vi.spyOn(reviewsApi, "mine").mockResolvedValue([{ id: "r2" } as MyReview]);
    const { rerender } = render(<TourReviews tourId="t1" initial={data} timeZone="Europe/Zagreb" signedIn={false} />);
    expect(screen.queryByRole("button", { name: "Report" })).not.toBeInTheDocument();
    rerender(<TourReviews tourId="t1" initial={data} timeZone="Europe/Zagreb" signedIn />);
    expect(await screen.findAllByRole("button", { name: "Report" })).toHaveLength(1);
    expect(within(screen.getByRole("article", { name: "Review by Marko" })).queryByRole("button", { name: "Report" })).not.toBeInTheDocument();
  });

  it("explains when there are no reviews yet", () => {
    const empty: Data = { summary: { average: null, count: 0, distribution: { one: 0, two: 0, three: 0, four: 0, five: 0 } }, reviews: { items: [], page: 1, pageSize: 5, totalCount: 0, totalPages: 0 } };
    render(<TourReviews tourId="t1" initial={empty} timeZone="Europe/Zagreb" signedIn />);
    expect(screen.getByText(/No reviews yet/)).toBeInTheDocument();
  });
});
