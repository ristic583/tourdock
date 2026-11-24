"use client";

import { Check, Copy } from "lucide-react";
import { useId, useRef, useState } from "react";

type State = "idle" | "copied" | "selected";

/**
 * A value to paste somewhere else (an address, a command), with a button that copies it. Where the browser won't let
 * the page write to the clipboard (plain HTTP, denied permission), the button selects the text for the visitor to copy.
 */
export function CopyField({ label, value, multiline = false }: { label: string; value: string; multiline?: boolean }) {
  const id = useId();
  const code = useRef<HTMLElement>(null);
  const [state, setState] = useState<State>("idle");

  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setState("copied");
    } catch {
      const selection = window.getSelection();
      if (code.current && selection) {
        const range = document.createRange();
        range.selectNodeContents(code.current);
        selection.removeAllRanges();
        selection.addRange(range);
      }
      setState("selected");
    }
    setTimeout(() => setState("idle"), 4000);
  }

  return (
    <div className="mt-2">
      <span id={id} className="sr-only">
        {label}
      </span>
      <div className="flex items-start gap-2 rounded-control border border-line bg-surface-2 py-1.5 pr-1.5 pl-3">
        <code ref={code} aria-labelledby={id} className={`min-w-0 flex-1 overflow-x-auto py-1 font-mono text-[13px] ${multiline ? "whitespace-pre" : "break-all"}`}>
          {value}
        </code>
        <button
          type="button"
          onClick={copy}
          className="inline-flex flex-none items-center gap-1 rounded-control px-2 py-1 text-xs font-semibold text-primary hover:bg-primary-soft"
          aria-label={state === "copied" ? `${label} copied` : `Copy ${label}`}
        >
          {state === "copied" ? <Check className="size-3.5" aria-hidden /> : <Copy className="size-3.5" aria-hidden />}
          <span aria-live="polite">{state === "copied" ? "Copied" : state === "selected" ? "Press Ctrl+C" : "Copy"}</span>
        </button>
      </div>
    </div>
  );
}
