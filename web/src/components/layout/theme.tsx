"use client";

import { Moon, Sun } from "lucide-react";
import { useSyncExternalStore } from "react";

import { Button } from "@/components/ui/button";

export type Theme = "light" | "dark";

const STORAGE_KEY = "td-theme";
const CHANGE_EVENT = "td-theme-change";

/**
 * Inline script that sets `data-theme` before the first paint, so there's no flash of the wrong theme.
 * An explicit choice is remembered in localStorage; otherwise the OS preference wins.
 */
export const themeScript = `(function(){try{var t=localStorage.getItem("${STORAGE_KEY}");if(t!=="light"&&t!=="dark"){t=matchMedia("(prefers-color-scheme: dark)").matches?"dark":"light"}document.documentElement.dataset.theme=t}catch(e){document.documentElement.dataset.theme="light"}})()`;

function subscribe(callback: () => void) {
  window.addEventListener(CHANGE_EVENT, callback);
  return () => window.removeEventListener(CHANGE_EVENT, callback);
}

function currentTheme(): Theme {
  return document.documentElement.dataset.theme === "dark" ? "dark" : "light";
}

export function setTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme;
  try {
    localStorage.setItem(STORAGE_KEY, theme);
  } catch {
    // Storage can be unavailable (private mode); the choice then lasts for this page only.
  }
  window.dispatchEvent(new Event(CHANGE_EVENT));
}

export function ThemeToggle() {
  // null on the server: the theme is only known in the browser.
  const theme = useSyncExternalStore<Theme | null>(subscribe, currentTheme, () => null);
  const next: Theme = theme === "dark" ? "light" : "dark";

  return (
    <Button
      variant="ghost"
      size="icon"
      className="rounded-full"
      aria-label={theme ? `Switch to ${next} theme` : "Toggle theme"}
      onClick={() => setTheme(next)}
    >
      {theme === "dark" ? <Sun className="size-5" /> : <Moon className="size-5" />}
    </Button>
  );
}
