import Link from "next/link";
import { Suspense } from "react";

import { SearchBar } from "@/components/catalog/search-bar";
import { TourCard } from "@/components/catalog/tour-card";
import { HeroSlideshow, type HeroSlide } from "@/components/home/hero-slideshow";
import { Button } from "@/components/ui/button";
import { searchTours } from "@/lib/catalog";
import { getDisplayRate } from "@/lib/display-currency";
import { parseCatalogSearch } from "@/lib/catalog-search";

/** Local photos (see public/images/demo-tours/CREDITS.md), shown full-bleed behind the headline. */
const HERO_SLIDES: HeroSlide[] = [
  { src: "/images/demo-tours/hero-santorini.jpg", caption: "Santorini, Greece" },
  { src: "/images/demo-tours/hero-uvac.jpg", caption: "Uvac canyon, Serbia" },
  { src: "/images/demo-tours/hero-fjord.jpg", caption: "Nærøyfjord, Norway" },
  { src: "/images/demo-tours/hero-kotor.jpg", caption: "Bay of Kotor, Montenegro" },
  { src: "/images/demo-tours/hero-benagil.jpg", caption: "Benagil cave, Portugal" },
  { src: "/images/demo-tours/hero-dolomites.jpg", caption: "Tre Cime, Italian Dolomites" },
];

export default function HomePage() {
  return (
    <>
      <section className="relative z-10">
        <HeroSlideshow slides={HERO_SLIDES} />
        <div className="relative mx-auto flex min-h-[560px] max-w-page flex-col justify-end px-4 pt-20 pb-10 sm:min-h-[640px] sm:px-6 sm:pb-14">
          <p className="mb-3.5 text-sm font-semibold tracking-[0.04em] text-white/90 uppercase">Tours by local captains &amp; guides</p>
          <h1 className="max-w-[720px] text-[38px] leading-[1.02] font-bold text-white drop-shadow-sm sm:text-[60px]">Find your next day out on the water, or off the map.</h1>
          <p className="mt-[18px] max-w-[540px] text-lg text-white/85">Boat trips, mountain hikes, rafting and jeep safaris from small local agencies across Europe. Book a seat, or the whole trip.</p>
          <div className="mt-9"><SearchBar search={parseCatalogSearch({})} /></div>
        </div>
      </section>

      <section className="border-t border-line bg-surface-2 py-14 sm:py-16">
        <div className="mx-auto max-w-page px-4 sm:px-6">
          <div className="flex items-end justify-between gap-4">
            <div>
              <p className="label-caps">Ready to go</p>
              <h2 className="mt-1 text-3xl font-bold">Upcoming departures</h2>
            </div>
            <Button asChild variant="outline"><Link href="/tours">Browse all tours</Link></Button>
          </div>
          <Suspense fallback={<div className="mt-7 grid grid-cols-1 gap-[22px] sm:grid-cols-2 xl:grid-cols-4">{[0, 1, 2, 3].map((item) => <div key={item} className="h-96 animate-pulse rounded-card bg-surface" />)}</div>}>
            <FeaturedTours />
          </Suspense>
        </div>
      </section>
    </>
  );
}

async function FeaturedTours() {
  const [featured, rate] = await Promise.all([searchTours(parseCatalogSearch({ page: "1", sort: "Soonest" })).catch(() => null), getDisplayRate()]);
  if (!featured?.items.length) return <p className="mt-7 rounded-card border border-line bg-surface p-5 text-muted-foreground">Tours will appear here once they are available.</p>;
  return <div className="mt-7 grid grid-cols-1 gap-[22px] sm:grid-cols-2 xl:grid-cols-4">{featured.items.slice(0, 4).map((tour, index) => <TourCard key={tour.id} tour={tour} index={index} rate={rate} />)}</div>;
}
