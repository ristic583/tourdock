"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { useEffect, useRef, useState } from "react";

type Phase = "idle" | "loading" | "done";

/** Wait this long before showing the bar, so instant (prefetched) navigations don't flash it. */
const SHOW_AFTER_MS = 120;
/** Give up and hide the bar if a navigation never finishes (offline, aborted). */
const GIVE_UP_MS = 15_000;

/** Is this click a same-tab navigation to another page of this site? */
export function isPageNavigation(event: Pick<MouseEvent, "button" | "metaKey" | "ctrlKey" | "shiftKey" | "altKey" | "defaultPrevented">, anchor: HTMLAnchorElement, current: URL): boolean {
  if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return false;
  if ((anchor.target && anchor.target !== "_self") || anchor.hasAttribute("download")) return false;
  const next = new URL(anchor.href, current);
  if (next.origin !== current.origin) return false;
  return next.pathname !== current.pathname || next.search !== current.search;
}

/**
 * A slim bar at the top of the page while the next page loads. The App Router has no navigation events, so it starts on
 * link clicks and GET form submits and finishes when the URL (path or query) changes.
 */
export function NavigationProgress() {
  const pathname = usePathname();
  const search = useSearchParams().toString();
  const [phase, setPhase] = useState<Phase>("idle");
  const timers = useRef<number[]>([]);
  const location = `${pathname}?${search}`;
  const [seen, setSeen] = useState(location);

  function clear() {
    timers.current.forEach((timer) => window.clearTimeout(timer));
    timers.current = [];
  }

  // The URL changed: the new page is rendering, finish the bar.
  if (location !== seen) {
    setSeen(location);
    if (phase !== "idle") setPhase("done");
  }

  useEffect(() => {
    if (phase !== "done") return;
    clear();
    const timer = window.setTimeout(() => setPhase("idle"), 350);
    return () => window.clearTimeout(timer);
  }, [phase]);

  useEffect(() => {
    function start() {
      clear();
      timers.current.push(
        window.setTimeout(() => setPhase("loading"), SHOW_AFTER_MS),
        window.setTimeout(() => setPhase("idle"), GIVE_UP_MS),
      );
    }
    function onClick(event: MouseEvent) {
      const anchor = (event.target as Element | null)?.closest?.("a[href]");
      if (!(anchor instanceof HTMLAnchorElement)) return;
      // Let other handlers run first: one of them may preventDefault.
      queueMicrotask(() => isPageNavigation(event, anchor, new URL(window.location.href)) && start());
    }
    function onSubmit(event: SubmitEvent) {
      const form = event.target as HTMLFormElement;
      if (form.method.toLowerCase() !== "get" || form.target === "_blank") return;
      queueMicrotask(() => !event.defaultPrevented && start());
    }
    document.addEventListener("click", onClick);
    document.addEventListener("submit", onSubmit);
    return () => {
      document.removeEventListener("click", onClick);
      document.removeEventListener("submit", onSubmit);
      clear();
    };
  }, []);

  // A navigation that lands on the same URL (or an interrupted one) never changes the location: stop on popstate too.
  useEffect(() => {
    const stop = () => setPhase((current) => (current === "idle" ? current : "done"));
    window.addEventListener("popstate", stop);
    return () => window.removeEventListener("popstate", stop);
  }, []);

  if (phase === "idle") return null;
  return (
    <div aria-hidden className="pointer-events-none fixed inset-x-0 top-0 z-[70] h-[3px]">
      <div
        data-phase={phase}
        className="nav-progress h-full origin-left bg-primary shadow-[0_0_8px_var(--primary)] data-[phase=done]:scale-x-100 data-[phase=done]:opacity-0 data-[phase=done]:transition-opacity data-[phase=done]:duration-300"
      />
    </div>
  );
}
