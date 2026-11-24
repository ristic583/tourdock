import type { Metadata } from "next";
import Link from "next/link";

import { TextPage } from "@/components/layout/text-page";

export const metadata: Metadata = { title: "Help", description: "How booking, payment, cancellation and listing tours work on TourDock." };

export default function HelpPage() {
  return (
    <TextPage eyebrow="Help" title="How TourDock works" intro="Short answers for travelers and agencies. TourDock is a portfolio demo: payments run in a sandbox and no real money moves.">
      <section aria-labelledby="h-book">
        <h2 id="h-book">Booking a tour</h2>
        <p>Pick a departure and the number of guests, then reserve. Your seats are held for 15 minutes while you pay; if the timer runs out they go back on sale. Some tours can also be booked privately, for your group only.</p>
      </section>
      <section aria-labelledby="h-pay">
        <h2 id="h-pay">Paying</h2>
        <p>You pay on the payment provider&apos;s page and come back automatically. Retrying after a declined card is safe: a booking is never charged twice. In this demo, use the test cards shown on the payment page.</p>
      </section>
      <section aria-labelledby="h-cancel">
        <h2 id="h-cancel">Changing or cancelling</h2>
        <p>Under <Link href="/bookings" className="text-primary">My bookings</Link>, choose &quot;Change or cancel&quot; to remove some guests or cancel the whole booking. The refund follows the agency&apos;s cancellation policy, shown on every tour and before you pay. If an agency cancels, you get a full refund.</p>
      </section>
      <section aria-labelledby="h-account">
        <h2 id="h-account">Your account</h2>
        <p>Confirm your email address with the link we send after sign-up; booking and listing an agency need a confirmed address. You can review a tour after it has taken place.</p>
      </section>
      <section aria-labelledby="h-agency">
        <h2 id="h-agency">Listing your tours</h2>
        <p><Link href="/agency" className="text-primary">Register an agency</Link>, add your boats or vehicles, create a tour with photos, prices, a schedule and a cancellation policy. A TourDock admin approves new agencies before their tours go on sale; we email you when that happens.</p>
      </section>
    </TextPage>
  );
}
