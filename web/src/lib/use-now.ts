"use client";

import { useSyncExternalStore } from "react";

function subscribe(onTick: () => void) {
  const id = window.setInterval(onTick, 1000);
  return () => window.clearInterval(id);
}

const nowInSeconds = () => Math.floor(Date.now() / 1000);

/** The current time in whole seconds, ticking every second; null during server rendering and hydration. */
export function useNowSeconds(): number | null {
  return useSyncExternalStore(subscribe, nowInSeconds, () => null);
}

export function formatRemaining(seconds: number): string {
  const s = Math.max(0, seconds);
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}
