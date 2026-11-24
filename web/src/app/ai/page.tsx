import type { Metadata } from "next";
import { headers } from "next/headers";
import Link from "next/link";

import { CopyField } from "@/components/ai/copy-field";
import { TextPage } from "@/components/layout/text-page";
import { siteOrigin } from "@/lib/env";
import { mcpAddresses } from "@/lib/mcp-connect";

export const metadata: Metadata = {
  title: "Use TourDock with AI",
  description: "Connect Claude, ChatGPT or another AI app to TourDock to search tours and, with your permission, book for you.",
};
// The addresses follow the origin the site is served on, known only per request (one image serves every deployment).
export const instant = false;

/** How to add TourDock's MCP server to an AI app (ADR 0027, 0028). The addresses follow the site's own origin. */
export default async function UseWithAiPage() {
  const addresses = mcpAddresses(siteOrigin(await headers()));
  return (
    <TextPage
      eyebrow="Use with AI"
      title="TourDock in your AI assistant"
      intro="Claude, ChatGPT and other AI apps can search TourDock for you and, if you allow it, hold seats and look after your bookings. They connect through MCP, the open standard these apps use for tools. You add TourDock once; your assistant decides when to use it."
    >
      <Addresses publicUrl={addresses.public} accountUrl={addresses.account} />
      <Apps accountUrl={addresses.account} />
      <section aria-labelledby="h-ask">
        <h2 id="h-ask">Things to ask</h2>
        <ul>
          <li>&quot;Find a rafting trip in Montenegro in May for two people.&quot;</li>
          <li>&quot;When are the next free dates for the sunset cruise in Split, and how much is it?&quot;</li>
          <li>&quot;Hold two seats on the first one and give me the link to pay.&quot; (account address)</li>
          <li>&quot;Which of my bookings are coming up?&quot; (account address)</li>
        </ul>
      </section>
      <section aria-labelledby="h-control">
        <h2 id="h-control">You stay in control</h2>
        <ul>
          <li>You allow access on TourDock&apos;s own page, and the app never sees your password.</li>
          <li>An app can hold seats for you, but you always pay on TourDock. It cancels a booking only after you confirm.</li>
          <li>It gets only what you allowed: your bookings, or your agency&apos;s tools if you run one.</li>
          <li>AI answers can be wrong. Prices and seats are confirmed when you book.</li>
        </ul>
        <p>
          Prefer not to set anything up? The <Link href="/tours">travel assistant</Link> on the tour pages answers the same questions.
        </p>
      </section>
    </TextPage>
  );
}

function Addresses({ publicUrl, accountUrl }: { publicUrl: string; accountUrl: string }) {
  return (
    <section aria-labelledby="h-addresses">
      <h2 id="h-addresses">Two addresses</h2>
      <h3 className="mt-4 font-semibold">Search only, no account</h3>
      <p>Tours, departures, free seats and prices. Anyone can use it.</p>
      <CopyField label="Search-only address" value={publicUrl} />
      <h3 className="mt-6 font-semibold">With your TourDock account</h3>
      <p>
        Everything above, plus holding seats for you, listing your bookings and cancelling after you confirm. Agencies can also see their
        bookings, numbers and reviews, and reply to reviews. The app first asks you to sign in to TourDock and allow access.
      </p>
      <CopyField label="Account address" value={accountUrl} />
    </section>
  );
}

function Apps({ accountUrl }: { accountUrl: string }) {
  const vsCode = JSON.stringify({ servers: { tourdock: { type: "http", url: accountUrl } } }, null, 2);
  const cursor = JSON.stringify({ mcpServers: { tourdock: { url: accountUrl } } }, null, 2);
  return (
    <section aria-labelledby="h-apps">
      <h2 id="h-apps">Add it to your app</h2>
      <h3 className="mt-4 font-semibold">Claude (web and desktop)</h3>
      <p>
        In Settings, open Connectors and choose &quot;Add custom connector&quot;. Name it TourDock and paste an address. With the account
        address, Claude opens TourDock&apos;s sign-in page: sign in and choose Allow. Custom connectors depend on your Claude plan.
      </p>
      <h3 className="mt-6 font-semibold">ChatGPT</h3>
      <p>
        Add a custom MCP connector in Settings (on some plans, only with developer mode turned on) and paste an address. For the account
        address, choose OAuth when it asks how to sign in.
      </p>
      <h3 className="mt-6 font-semibold">Claude Code</h3>
      <p>Add it from a terminal, then run /mcp in Claude Code and choose TourDock to sign in.</p>
      <CopyField label="Claude Code command" value={`claude mcp add --transport http tourdock ${accountUrl}`} />
      <h3 className="mt-6 font-semibold">VS Code</h3>
      <p>In .vscode/mcp.json (or your user MCP settings):</p>
      <CopyField label="VS Code configuration" value={vsCode} multiline />
      <h3 className="mt-6 font-semibold">Cursor</h3>
      <p>In ~/.cursor/mcp.json:</p>
      <CopyField label="Cursor configuration" value={cursor} multiline />
      <p>Any other app that supports remote MCP servers over HTTP, with OAuth sign-in, works the same way.</p>
    </section>
  );
}
