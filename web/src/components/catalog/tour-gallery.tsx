import type { TourPhoto } from "@/lib/api/contracts";
import { tourImage } from "@/lib/demo-media";
import { cn } from "@/lib/utils";

/**
 * The tour's photos: the cover large, up to four more medium in the design's 2fr 1fr 1fr mosaic. Fewer photos get
 * a simpler layout instead of illustrated filler. Without any upload, the bundled demo photo (by title) or one
 * illustrated placeholder stands in.
 */
export function TourGallery({ title, photos }: { title: string; photos: TourPhoto[] }) {
  const ordered = [...photos].sort((a, b) => Number(b.isCover) - Number(a.isCover));
  const sources = ordered.slice(0, 5).map((photo, index) => (index === 0 ? photo.urls.large : photo.urls.medium));
  if (!sources.length) {
    const demo = tourImage(title);
    if (demo) sources.push(demo);
  }
  const more = photos.length - sources.length;
  const layout =
    sources.length >= 5
      ? "sm:grid-cols-[2fr_1fr_1fr] sm:grid-rows-2"
      : sources.length >= 2
        ? `sm:grid-cols-[2fr_1fr] ${sources.length === 2 ? "sm:grid-rows-1" : sources.length === 3 ? "sm:grid-rows-2" : "sm:grid-rows-3"}`
        : "";

  return (
    <div className={cn("relative grid h-[260px] grid-cols-1 gap-2.5 overflow-hidden rounded-hero sm:h-[410px]", layout)}>
      {sources.length ? (
        sources.map((src, index) => (
          <div key={src} className={cn("relative min-h-0", index === 0 ? (sources.length >= 3 ? "sm:row-span-full" : "") : "hidden sm:block")}>
            {/* Tour photos come from the agency's object-storage public URL (see tour-card.tsx). */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={src}
              alt={index === 0 ? `${title}, cover photo` : `${title}, photo ${index + 1}`}
              loading={index === 0 ? "eager" : "lazy"}
              fetchPriority={index === 0 ? "high" : undefined}
              className="absolute inset-0 size-full object-cover"
            />
          </div>
        ))
      ) : (
        <div className="photo-sea" aria-hidden />
      )}
      {more > 0 && (
        <span className="absolute right-4 bottom-4 rounded-control bg-white px-3.5 py-2 text-sm font-semibold text-[#0f1a20] shadow-card">+{more} more photos</span>
      )}
    </div>
  );
}
