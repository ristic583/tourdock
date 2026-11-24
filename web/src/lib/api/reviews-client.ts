import type { AgencyReview, MyReview, ReportReason, ReviewSort, TourReviews } from "@/lib/api/contracts";

import { browserApi } from "./browser";
import { unwrap } from "./client";

export type ReviewInput = { rating: number; comment: string | null };

/** Review reads and writes from Client Components (through the BFF). */
export const reviewsApi = {
  forTour: (tourId: string, sort: ReviewSort, page: number, pageSize = 10) =>
    unwrap<TourReviews>(browserApi.GET("/api/v1/tours/{tourId}/reviews", { params: { path: { tourId }, query: { sort, page, pageSize } } })),

  mine: () => unwrap<MyReview[]>(browserApi.GET("/api/v1/me/reviews")),

  post: (bookingId: string, body: ReviewInput) =>
    unwrap<MyReview>(browserApi.POST("/api/v1/bookings/{bookingId}/review", { params: { path: { bookingId } }, body })),
  edit: (bookingId: string, body: ReviewInput) =>
    unwrap<MyReview>(browserApi.PUT("/api/v1/bookings/{bookingId}/review", { params: { path: { bookingId } }, body })),
  remove: (bookingId: string) => unwrap<unknown>(browserApi.DELETE("/api/v1/bookings/{bookingId}/review", { params: { path: { bookingId } } })),

  report: (reviewId: string, reason: ReportReason, note?: string) =>
    unwrap<unknown>(browserApi.POST("/api/v1/reviews/{reviewId}/reports", { params: { path: { reviewId } }, body: { reason, note: note || null } })),

  reply: (agencyId: string, reviewId: string, text: string) =>
    unwrap<AgencyReview>(browserApi.PUT("/api/v1/agencies/{agencyId}/reviews/{reviewId}/reply", { params: { path: { agencyId, reviewId } }, body: { text } })),
  removeReply: (agencyId: string, reviewId: string) =>
    unwrap<unknown>(browserApi.DELETE("/api/v1/agencies/{agencyId}/reviews/{reviewId}/reply", { params: { path: { agencyId, reviewId } } })),

  hide: (reviewId: string, reason: string) =>
    unwrap<unknown>(browserApi.POST("/api/v1/admin/reviews/{reviewId}/hide", { params: { path: { reviewId } }, body: { reason } })),
  unhide: (reviewId: string) => unwrap<unknown>(browserApi.POST("/api/v1/admin/reviews/{reviewId}/unhide", { params: { path: { reviewId } } })),
  dismissReports: (reviewId: string) =>
    unwrap<unknown>(browserApi.POST("/api/v1/admin/reviews/{reviewId}/dismiss-reports", { params: { path: { reviewId } } })),
};
