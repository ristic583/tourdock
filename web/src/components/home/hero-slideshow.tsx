"use client";

import Image from "next/image";
import { useEffect, useState } from "react";

import { cn } from "@/lib/utils";

export type HeroSlide = { src: string; caption: string; position?: string };

const INTERVAL_MS = 6500;

/** A photo is either shown or faded out; it never moves or changes size. */
export function slideClass(index: number, active: number): string {
  return cn("transition-opacity duration-[1600ms] ease-in-out motion-reduce:transition-none", index === active ? "opacity-100" : "opacity-0");
}

/**
 * Full-bleed background photos that change with a slow cross-fade, and nothing else. Every slide stays mounted and only
 * its opacity changes, so the transition never flashes an empty frame. It holds still for people who prefer reduced
 * motion and while the tab is hidden.
 */
export function HeroSlideshow({ slides }: { slides: HeroSlide[] }) {
  const [active, setActive] = useState(0);

  useEffect(() => {
    if (slides.length < 2) return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") setActive((index) => (index + 1) % slides.length);
    }, INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [slides.length]);

  return (
    <div className="absolute inset-0 overflow-hidden bg-[#0b1f2a]" aria-hidden="true">
      {slides.map((slide, index) => (
        <Image
          key={slide.src}
          src={slide.src}
          alt=""
          fill
          sizes="100vw"
          preload={index === 0}
          className={cn("object-cover", slideClass(index, active))}
          style={{ objectPosition: slide.position ?? "center" }}
        />
      ))}
      <div className="absolute inset-0 bg-gradient-to-b from-black/55 via-black/25 to-black/65" />
      <div className="absolute inset-0 bg-gradient-to-r from-black/45 via-black/10 to-transparent" />
      <p className="absolute right-4 bottom-4 text-xs font-medium text-white/80 sm:right-6">{slides[active]?.caption}</p>
    </div>
  );
}
