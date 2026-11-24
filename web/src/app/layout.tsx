import type { Metadata, Viewport } from "next";
import { Suspense } from "react";
import { Inter, Space_Grotesk } from "next/font/google";

import { AssistantChat } from "@/components/assistant/assistant-chat";
import { NavigationProgress } from "@/components/feedback/navigation-progress";
import { EmailConfirmBanner } from "@/components/layout/email-confirm-banner";
import { SiteFooter } from "@/components/layout/site-footer";
import { MobileTabBar } from "@/components/layout/mobile-tab-bar";
import { SiteHeader } from "@/components/layout/site-header";
import { themeScript } from "@/components/layout/theme";
import { Providers } from "@/components/providers";

import "./globals.css";

// Self-hosted at build time by next/font: no requests to Google from the visitor's browser.
const inter = Inter({ subsets: ["latin", "latin-ext"], variable: "--font-inter", display: "swap" });
const spaceGrotesk = Space_Grotesk({
  subsets: ["latin", "latin-ext"],
  weight: ["500", "600", "700"],
  variable: "--font-space-grotesk",
  display: "swap",
});

export const metadata: Metadata = {
  title: { default: "TourDock · Tours by local captains & guides", template: "%s · TourDock" },
  description:
    "Boat trips, fishing charters and jeep safaris from small local agencies. Book a seat, or the whole trip.",
  applicationName: "TourDock",
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#F6F7F5" },
    { media: "(prefers-color-scheme: dark)", color: "#0C1316" },
  ],
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    // data-theme is set by the inline script before hydration, hence suppressHydrationWarning.
    <html lang="en" suppressHydrationWarning className={`${inter.variable} ${spaceGrotesk.variable}`}>
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body className="flex min-h-dvh flex-col pb-[76px] md:pb-0">
        <a
          href="#main"
          className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-control focus:bg-surface focus:px-4 focus:py-2"
        >
          Skip to content
        </a>
        <Providers>
          <Suspense fallback={null}>
            <NavigationProgress />
          </Suspense>
          <SiteHeader />
          {/* Per-request session data, streamed in; the rest of the page doesn't wait for it. */}
          <Suspense fallback={null}>
            <EmailConfirmBanner />
          </Suspense>
          <main id="main" className="flex-1">
            {children}
          </main>
          <SiteFooter />
          <MobileTabBar />
          <AssistantChat />
        </Providers>
      </body>
    </html>
  );
}
