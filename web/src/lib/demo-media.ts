/**
 * Curated fallback photos for the fictional seeded catalog. Real tour cover photos from the API always win.
 * Source and photographer credits live in public/images/demo-tours/CREDITS.md.
 */
const DEMO_TOUR_IMAGES: Record<string, string> = {
  "Sunset Cruise along the Split Riviera": "/images/demo-tours/sunset-fishing.jpg",
  "Blue Cave & Five Islands": "/images/demo-tours/dubrovnik-boat.jpg",
  "Big Game Fishing Charter": "/images/demo-tours/fishing-onboard.jpg",
  "Morning Fishing Trip": "/images/demo-tours/fishing-charter.jpg",
  "Pakleni Islands Snorkeling": "/images/demo-tours/coastal-kayaking.jpg",
  "Konavle Valley Jeep Safari": "/images/demo-tours/jeep-safari.jpg",
  "Bay of Kotor Day Trip": "/images/demo-tours/kotor-bay.jpg",
};

export function tourImage(title: string, uploadedCoverUrl?: string | null): string | undefined {
  return uploadedCoverUrl ?? DEMO_TOUR_IMAGES[title];
}
