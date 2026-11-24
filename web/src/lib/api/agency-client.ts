import type { AdminAgency, AgencyTour, MyAgency, PagedResponse, RefundTier, ScheduleRuleCreated, Slot, TourPhoto, Vehicle } from "@/lib/api/contracts";
import type { components } from "@/lib/api/schema";

import { browserApi } from "./browser";
import { unwrap } from "./client";
import { toApiError } from "./problem";

type Schemas = components["schemas"];
export type VehicleInput = Schemas["VehicleRequest"];
export type CreateTourInput = Schemas["CreateTourRequest"];
export type TourDetailsInput = Schemas["TourDetailsRequest"];
export type PricingInput = Schemas["PricingRequest"];
export type SeasonInput = Schemas["SeasonRequest"];
export type ScheduleRuleInput = Schemas["ScheduleRuleRequest"];
export type SlotInput = Schemas["CreateSlotRequest"];

const a = (agencyId: string) => ({ agencyId });
const t = (agencyId: string, tourId: string) => ({ agencyId, tourId });

/** Agency back-office writes from Client Components (through the BFF). Pages re-read with router.refresh(). */
export const agencyApi = {
  register: (body: Schemas["RegisterAgencyCommand"]) => unwrap<MyAgency>(browserApi.POST("/api/v1/agencies", { body })),

  createVehicle: (agencyId: string, body: VehicleInput) =>
    unwrap<Vehicle>(browserApi.POST("/api/v1/agencies/{agencyId}/vehicles", { params: { path: a(agencyId) }, body })),
  updateVehicle: (agencyId: string, vehicleId: string, body: VehicleInput) =>
    unwrap<Vehicle>(browserApi.PUT("/api/v1/agencies/{agencyId}/vehicles/{vehicleId}", { params: { path: { agencyId, vehicleId } }, body })),
  deleteVehicle: (agencyId: string, vehicleId: string) =>
    unwrap<unknown>(browserApi.DELETE("/api/v1/agencies/{agencyId}/vehicles/{vehicleId}", { params: { path: { agencyId, vehicleId } } })),

  createTour: (agencyId: string, body: CreateTourInput) =>
    unwrap<AgencyTour>(browserApi.POST("/api/v1/agencies/{agencyId}/tours", { params: { path: a(agencyId) }, body })),
  updateDetails: (agencyId: string, tourId: string, body: TourDetailsInput) =>
    unwrap<AgencyTour>(browserApi.PUT("/api/v1/agencies/{agencyId}/tours/{tourId}", { params: { path: t(agencyId, tourId) }, body })),
  updatePricing: (agencyId: string, tourId: string, body: PricingInput) =>
    unwrap<AgencyTour>(browserApi.PUT("/api/v1/agencies/{agencyId}/tours/{tourId}/pricing", { params: { path: t(agencyId, tourId) }, body })),
  updatePolicy: (agencyId: string, tourId: string, tiers: RefundTier[]) =>
    unwrap<AgencyTour>(browserApi.PUT("/api/v1/agencies/{agencyId}/tours/{tourId}/cancellation-policy", { params: { path: t(agencyId, tourId) }, body: tiers })),
  publish: (agencyId: string, tourId: string) =>
    unwrap<AgencyTour>(browserApi.POST("/api/v1/agencies/{agencyId}/tours/{tourId}/publish", { params: { path: t(agencyId, tourId) } })),
  unpublish: (agencyId: string, tourId: string) =>
    unwrap<AgencyTour>(browserApi.POST("/api/v1/agencies/{agencyId}/tours/{tourId}/unpublish", { params: { path: t(agencyId, tourId) } })),
  deleteTour: (agencyId: string, tourId: string) =>
    unwrap<unknown>(browserApi.DELETE("/api/v1/agencies/{agencyId}/tours/{tourId}", { params: { path: t(agencyId, tourId) } })),
  addSeason: (agencyId: string, tourId: string, body: SeasonInput) =>
    unwrap<AgencyTour>(browserApi.POST("/api/v1/agencies/{agencyId}/tours/{tourId}/seasons", { params: { path: t(agencyId, tourId) }, body })),
  removeSeason: (agencyId: string, tourId: string, seasonId: string) =>
    unwrap<AgencyTour>(browserApi.DELETE("/api/v1/agencies/{agencyId}/tours/{tourId}/seasons/{seasonId}", { params: { path: { agencyId, tourId, seasonId } } })),
  addScheduleRule: (agencyId: string, tourId: string, body: ScheduleRuleInput) =>
    unwrap<ScheduleRuleCreated>(browserApi.POST("/api/v1/agencies/{agencyId}/tours/{tourId}/schedule-rules", { params: { path: t(agencyId, tourId) }, body })),
  stopScheduleRule: (agencyId: string, tourId: string, ruleId: string) =>
    unwrap<AgencyTour>(browserApi.DELETE("/api/v1/agencies/{agencyId}/tours/{tourId}/schedule-rules/{ruleId}", { params: { path: { agencyId, tourId, ruleId } } })),
  addSlot: (agencyId: string, tourId: string, body: SlotInput) =>
    unwrap<Slot>(browserApi.POST("/api/v1/agencies/{agencyId}/tours/{tourId}/slots", { params: { path: t(agencyId, tourId) }, body })),

  blockSlot: (agencyId: string, slotId: string) =>
    unwrap<Slot>(browserApi.POST("/api/v1/agencies/{agencyId}/slots/{slotId}/block", { params: { path: { agencyId, slotId } } })),
  unblockSlot: (agencyId: string, slotId: string) =>
    unwrap<Slot>(browserApi.POST("/api/v1/agencies/{agencyId}/slots/{slotId}/unblock", { params: { path: { agencyId, slotId } } })),
  cancelSlot: (agencyId: string, slotId: string, reason: string) =>
    unwrap<Slot>(browserApi.POST("/api/v1/agencies/{agencyId}/slots/{slotId}/cancel", { params: { path: { agencyId, slotId } }, body: { reason } })),

  cancelBooking: (agencyId: string, bookingId: string, reason: string) =>
    unwrap<unknown>(browserApi.POST("/api/v1/agencies/{agencyId}/bookings/{bookingId}/cancel", { params: { path: { agencyId, bookingId } }, body: { reason } })),

  setCover: (agencyId: string, tourId: string, photoId: string) =>
    unwrap<AgencyTour>(browserApi.PUT("/api/v1/agencies/{agencyId}/tours/{tourId}/photos/{photoId}/cover", { params: { path: { agencyId, tourId, photoId } } })),
  reorderPhotos: (agencyId: string, tourId: string, photoIds: string[]) =>
    unwrap<AgencyTour>(browserApi.PUT("/api/v1/agencies/{agencyId}/tours/{tourId}/photos/order", { params: { path: t(agencyId, tourId) }, body: { photoIds } })),
  deletePhoto: (agencyId: string, tourId: string, photoId: string) =>
    unwrap<unknown>(browserApi.DELETE("/api/v1/agencies/{agencyId}/tours/{tourId}/photos/{photoId}", { params: { path: { agencyId, tourId, photoId } } })),

  /** Multipart upload (field `file`). Sent with fetch directly; the BFF forwards the body and its multipart content-type to the API. */
  async uploadPhoto(agencyId: string, tourId: string, file: File): Promise<TourPhoto> {
    const body = new FormData();
    body.append("file", file);
    const response = await fetch(`/api/bff/api/v1/agencies/${agencyId}/tours/${tourId}/photos`, { method: "POST", body });
    const data: unknown = await response.json().catch(() => undefined);
    if (!response.ok) throw toApiError(response.status, data);
    return data as TourPhoto;
  },
};

export const adminApi = {
  approve: (agencyId: string) => unwrap<unknown>(browserApi.POST("/api/v1/admin/agencies/{agencyId}/approve", { params: { path: { agencyId } } })),
  suspend: (agencyId: string, reason: string) =>
    unwrap<unknown>(browserApi.POST("/api/v1/admin/agencies/{agencyId}/suspend", { params: { path: { agencyId } }, body: { reason } })),
  retryRefund: (refundId: string, note: string) =>
    unwrap<unknown>(browserApi.POST("/api/v1/admin/refunds/{refundId}/retry", { params: { path: { refundId } }, body: { note } })),
};

export type { AdminAgency, PagedResponse };
