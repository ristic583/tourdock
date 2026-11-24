"use client";

import { RotateCcw, Sparkles, X } from "lucide-react";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";

import { Spinner } from "@/components/ui/spinner";
import { assistantApi } from "@/lib/api/assistant-client";

import { ChatForm, ChatMessage, Intro } from "./chat-parts";
import { showsOn } from "./chat-state";
import { useAssistantChat } from "./use-assistant-chat";

const POSITION = "fixed right-4 bottom-[92px] z-40 md:bottom-6";

/**
 * The travel assistant: a floating button and a chat panel on the browsing pages. It shows up only when the API says
 * the assistant is configured. The chat survives navigation (session storage), so a traveler can open a cited tour
 * and come back to the conversation.
 */
export function AssistantChat() {
  const pathname = usePathname();
  const [enabled, setEnabled] = useState(false);
  const [open, setOpen] = useState(false);
  const assistant = useAssistantChat();

  useEffect(() => {
    assistantApi.status().then((status) => setEnabled(status.enabled)).catch(() => setEnabled(false));
  }, []);

  if (!enabled || !showsOn(pathname)) return null;

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => {
          assistant.resume();
          setOpen(true);
        }}
        aria-expanded={false}
        className={`${POSITION} flex items-center gap-2 rounded-full bg-primary px-5 py-3 font-semibold text-primary-foreground shadow-card hover:bg-primary-hover`}
      >
        <Sparkles className="size-[18px]" aria-hidden /> Ask TourDock
      </button>
    );
  }

  return <ChatPanel assistant={assistant} onClose={() => setOpen(false)} />;
}

function ChatPanel({ assistant, onClose }: { assistant: ReturnType<typeof useAssistantChat>; onClose: () => void }) {
  const { chat, pending, error, send, restart } = assistant;
  const endRef = useRef<HTMLDivElement>(null);
  // Braces matter: newer browsers return a Promise from scrollIntoView, and React would take it for a cleanup function.
  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: "end" });
  }, [chat.messages.length, pending]);

  return (
    <section
      role="dialog"
      aria-label="Travel assistant"
      className={`${POSITION} flex h-[min(560px,70dvh)] w-[min(400px,calc(100vw-2rem))] flex-col rounded-card border border-line bg-surface shadow-card`}
    >
      <header className="flex items-center gap-2 border-b border-line px-4 py-3">
        <Sparkles className="size-[18px] text-primary" aria-hidden />
        <h2 className="flex-1 font-semibold">Travel assistant</h2>
        {chat.messages.length > 0 && (
          <button type="button" onClick={restart} className="rounded-control p-1.5 hover:bg-surface-2" aria-label="Start a new chat">
            <RotateCcw className="size-4" aria-hidden />
          </button>
        )}
        <button type="button" onClick={onClose} className="rounded-control p-1.5 hover:bg-surface-2" aria-label="Close the assistant">
          <X className="size-4" aria-hidden />
        </button>
      </header>
      <div className="flex-1 space-y-3 overflow-y-auto px-4 py-3" aria-live="polite">
        {chat.messages.length === 0 && <Intro onPick={(text) => void send(text)} />}
        {chat.messages.map((message, index) => <ChatMessage key={index} {...message} />)}
        {pending && <p className="flex items-center gap-2 text-sm text-muted-foreground"><Spinner /> Searching tours…</p>}
        {error && <p className="rounded-control bg-urgent-soft px-3 py-2 text-sm text-urgent-ink" role="alert">{error}</p>}
        <div ref={endRef} />
      </div>
      <ChatForm disabled={pending || chat.turnsLeft === 0} full={chat.turnsLeft === 0} onSend={(text) => void send(text)} />
    </section>
  );
}
