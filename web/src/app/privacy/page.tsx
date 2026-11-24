import type { Metadata } from "next";

import { TextPage } from "@/components/layout/text-page";

export const metadata: Metadata = { title: "Privacy", description: "What TourDock stores about you and why." };

export default function PrivacyPage() {
  return (
    <TextPage eyebrow="Privacy" title="Your data on TourDock" intro="TourDock is a portfolio demo with fictional content. This page explains what the app stores and why, as a real service would.">
      <section aria-labelledby="p-what">
        <h2 id="p-what">What we store</h2>
        <ul>
          <li>Your account: name, email address and a salted password hash (never the password itself).</li>
          <li>Your bookings, payments and refunds: needed to run the trip and for the agency&apos;s records.</li>
          <li>Reviews you write, shown with your first name and the initial of your last name.</li>
        </ul>
      </section>
      <section aria-labelledby="p-share">
        <h2 id="p-share">Who sees it</h2>
        <p>The agency running your tour sees the lead traveler&apos;s name, email and the booking details. Nothing is sold or shared with advertisers. Card numbers never reach TourDock: the payment provider handles them.</p>
        <p>If you describe a trip in the search box, that text is sent to our embedding provider (Voyage AI) to rank tours by meaning. It isn&apos;t linked to your account or written to our logs.</p>
        <p>Questions you ask the travel assistant go to Anthropic (Claude) to write the answer. The conversation is kept for 24 hours after your last message, so you can continue it, and then deleted. It isn&apos;t linked to your account.</p>
      </section>
      <section aria-labelledby="p-logs">
        <h2 id="p-logs">Logs and cookies</h2>
        <p>Logs record ids, not email addresses or names. Cookies keep you signed in and remember your display currency and theme; there are no tracking cookies.</p>
      </section>
      <section aria-labelledby="p-email">
        <h2 id="p-email">Emails</h2>
        <p>We email you about your account and your bookings only: confirmations, cancellations, refunds and a review request after the trip.</p>
      </section>
    </TextPage>
  );
}
