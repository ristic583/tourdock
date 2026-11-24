import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import { expect, it, vi } from "vitest";

import { bookingsApi } from "@/lib/api/bookings";
import type { Booking } from "@/lib/api/contracts";

import { CheckoutView } from "./checkout-view";

vi.mock("@/lib/api/bookings", () => ({ bookingsApi: { get: vi.fn() } }));

it("shows confirmation after a successful payment was read with a still-pending booking", async () => {
  const eur = (amount: number) => ({ amount, currency: "EUR" });
  const initial: Booking = {
    id: "booking-1", tourId: "tour-1", tourTitle: "Harbour Cruise", departureId: "departure-1",
    type: "Shared", guests: 1, status: "Pending",
    unitPrice: eur(45), totalPrice: eur(45), paidAmount: eur(0), refundedAmount: eur(0), refundIfCancelledNow: eur(0),
    startsAt: "2027-06-14T16:00:00Z", localStart: "2027-06-14T18:00:00", timeZoneId: "Europe/Zagreb",
    holdExpiresAt: new Date(Date.now() + 300_000).toISOString(),
    cancellationPolicy: [{ hoursBeforeStart: 72, refundPercent: 100 }],
    createdAt: new Date().toISOString(), cancelledAt: null, cancelledBy: null, cancellationReason: null,
    lastPaymentStatus: "Succeeded", lastPaymentFailureReason: null,
  };
  vi.mocked(bookingsApi.get).mockResolvedValue({ ...initial, status: "Confirmed", paidAmount: eur(45), holdExpiresAt: null });
  // Match the application's fresh-data window: confirmation must be polled, not wait for staleTime.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000 } } });
  const view = render(
    <QueryClientProvider client={client}>
      <CheckoutView initial={initial} tour={null} traveler={{ name: "Traveler", email: "traveler@example.com" }} />
    </QueryClientProvider>,
  );
  try {
    expect(screen.getByRole("button", { name: "Payment received" })).toBeDisabled();
    await waitFor(() => expect(screen.getByRole("heading", { level: 1, name: "You're going on Harbour Cruise" })).toBeVisible(), { timeout: 3000 });
    expect(bookingsApi.get).toHaveBeenCalledWith("booking-1");
  } finally {
    view.unmount();
    client.clear();
  }
});
