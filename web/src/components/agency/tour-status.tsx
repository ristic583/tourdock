import { StatusPill, type StatusTone } from "@/components/ui/status-pill";
import type { TourStatus } from "@/lib/api/contracts";

const TONE: Record<TourStatus, StatusTone> = { Published: "confirmed", Draft: "pending", Unpublished: "expired" };

export function TourStatusPill({ status }: { status: TourStatus }) {
  return <StatusPill tone={TONE[status]}>{status}</StatusPill>;
}
