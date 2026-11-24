"use client";

import { SendHorizontal } from "lucide-react";
import Link from "next/link";
import { useId, type FormEvent } from "react";

import { ChatTourCard } from "./chat-tour-card";
import { EXAMPLES, MAX_MESSAGE_LENGTH, type ChatMessage as Message } from "./chat-state";

export function Intro({ onPick }: { onPick: (text: string) => void }) {
  return (
    <div className="space-y-3 text-sm">
      <p>Tell me what kind of trip you&apos;re after, and I&apos;ll find tours that fit, with dates, seats and prices.</p>
      <div className="flex flex-col gap-2">
        {EXAMPLES.map((example) => (
          <button key={example} type="button" onClick={() => onPick(example)} className="rounded-control border border-line px-3 py-2 text-left hover:bg-surface-2">
            {example}
          </button>
        ))}
      </div>
      <p className="text-xs text-muted-foreground">
        Use Claude or ChatGPT?{" "}
        <Link href="/ai" className="text-primary underline underline-offset-2">
          Connect TourDock to it
        </Link>
        .
      </p>
    </div>
  );
}

export function ChatMessage({ role, text, tours }: Message) {
  if (role === "user") {
    return <p className="ml-8 rounded-card bg-primary-soft px-3 py-2 text-sm whitespace-pre-wrap">{text}</p>;
  }

  return (
    <div className="mr-4 space-y-2 text-sm">
      <p className="whitespace-pre-wrap">{text}</p>
      {tours && tours.length > 0 && (
        <ul className="space-y-2">
          {tours.map((tour) => (
            <li key={tour.id}>
              <ChatTourCard tour={tour} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Enter sends, Shift+Enter adds a line. */
export function ChatForm({ disabled, full, onSend }: { disabled: boolean; full: boolean; onSend: (text: string) => void }) {
  const inputId = useId();

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const field = event.currentTarget.elements.namedItem("message") as HTMLTextAreaElement;
    onSend(field.value);
    field.value = "";
  }

  return (
    <form onSubmit={onSubmit} className="border-t border-line p-3">
      <label htmlFor={inputId} className="sr-only">Your question</label>
      <div className="flex items-end gap-2">
        <textarea
          id={inputId}
          name="message"
          rows={2}
          maxLength={MAX_MESSAGE_LENGTH}
          placeholder="Ask about tours, dates, prices…"
          disabled={disabled}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          className="min-h-11 flex-1 resize-none rounded-control border border-line bg-surface-2 px-3 py-2 text-sm outline-none focus:border-primary"
        />
        <button type="submit" disabled={disabled} aria-label="Send" className="flex size-11 items-center justify-center rounded-control bg-primary text-primary-foreground disabled:opacity-50">
          <SendHorizontal className="size-[18px]" aria-hidden />
        </button>
      </div>
      <p className="mt-2 text-xs text-muted-foreground">
        {full ? "This chat is full. Start a new one to keep going." : "AI answers can be wrong. Prices and seats are confirmed when you book."}
      </p>
    </form>
  );
}
