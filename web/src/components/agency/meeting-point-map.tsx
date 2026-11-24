"use client";

import "leaflet/dist/leaflet.css";

import type { LeafletMouseEvent, Map as LeafletMap, Marker } from "leaflet";
import { useEffect, useRef } from "react";

export type Point = { latitude: number; longitude: number };

// Whole Adriatic coast in view before anything is picked.
const OVERVIEW = { latitude: 43.6, longitude: 16.4, zoom: 6 };
const PICKED_ZOOM = 15;

type Props = {
  /** The meeting point; null until the agency picks a city or clicks the map. */
  value: Point | null;
  onChange: (point: Point) => void;
  /** Bump to re-centre on `value` (e.g. after picking a city), without moving the view on every drag. */
  focusKey?: string;
  label: string;
};

/**
 * Pick the exact meeting point: click the map or drag the pin. OpenStreetMap tiles through Leaflet; loaded in the
 * browser only (Leaflet needs `window`). The pin is a styled div, so no marker image assets are needed.
 */
export function MeetingPointMap({ value, onChange, focusKey, label }: Props) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<LeafletMap | null>(null);
  const marker = useRef<Marker | null>(null);
  const placePin = useRef<((point: Point) => void) | null>(null);
  const latest = useRef({ value, onChange });
  useEffect(() => {
    latest.current = { value, onChange };
  });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const L = await import("leaflet");
      if (cancelled || !container.current || map.current) return;
      const start = latest.current.value;
      const instance = L.map(container.current, { scrollWheelZoom: false }).setView(
        start ? [start.latitude, start.longitude] : [OVERVIEW.latitude, OVERVIEW.longitude],
        start ? PICKED_ZOOM : OVERVIEW.zoom,
      );
      L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
      }).addTo(instance);

      const icon = L.divIcon({
        className: "",
        html: '<span class="meeting-pin" aria-hidden="true"></span>',
        iconSize: [28, 36],
        iconAnchor: [14, 34],
      });
      const place = (point: Point) => {
        if (marker.current) {
          marker.current.setLatLng([point.latitude, point.longitude]);
          return;
        }
        marker.current = L.marker([point.latitude, point.longitude], { draggable: true, icon, keyboard: true, title: "Meeting point" }).addTo(instance);
        marker.current.on("dragend", () => {
          const at = marker.current!.getLatLng();
          latest.current.onChange(round({ latitude: at.lat, longitude: at.lng }));
        });
      };
      placePin.current = place;
      if (start) place(start);
      instance.on("click", (event: LeafletMouseEvent) => {
        const point = round({ latitude: event.latlng.lat, longitude: event.latlng.lng });
        place(point);
        latest.current.onChange(point);
      });
      map.current = instance;
    })();
    return () => {
      cancelled = true;
      map.current?.remove();
      map.current = null;
      marker.current = null;
      placePin.current = null;
    };
  }, []);

  // Re-centre when the caller asks (a city was picked), placing the pin there.
  useEffect(() => {
    const current = latest.current.value;
    if (!focusKey || !current || !map.current) return;
    map.current.setView([current.latitude, current.longitude], PICKED_ZOOM);
    placePin.current?.(current);
  }, [focusKey]);

  return <div ref={container} role="application" aria-label={label} className="h-72 w-full overflow-hidden rounded-control border border-line bg-surface-2" />;
}

/** Five decimals is about a metre: plenty for a meeting point, and tidy in the form. */
function round(point: Point): Point {
  return { latitude: Math.round(point.latitude * 1e5) / 1e5, longitude: Math.round(point.longitude * 1e5) / 1e5 };
}
