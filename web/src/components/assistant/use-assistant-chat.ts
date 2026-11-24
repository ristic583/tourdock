"use client";

import { useState } from "react";

import { assistantApi } from "@/lib/api/assistant-client";
import { ApiError } from "@/lib/api/problem";

import { clearChat, loadChat, plainText, saveChat, type ChatState } from "./chat-state";

/** The chat's state and actions: a question is added to the chat only once it has an answer. */
export function useAssistantChat() {
  const [chat, setChat] = useState<ChatState>({ messages: [] });
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();

  /** Picks up the chat from earlier in this tab (read on open, not on render: session storage is browser-only). */
  function resume() {
    setChat(loadChat());
  }

  async function send(text: string) {
    const message = text.trim();
    if (!message || pending) return;
    const before = chat;
    const asked: ChatState = { ...chat, messages: [...chat.messages, { role: "user", text: message }] };
    setChat(asked);
    setError(undefined);
    setPending(true);
    try {
      const reply = await assistantApi.ask(message, chat.conversationId);
      const answered: ChatState = {
        conversationId: reply.conversationId,
        turnsLeft: reply.turnsLeft,
        messages: [...asked.messages, { role: "assistant", text: plainText(reply.reply), tours: reply.tours }],
      };
      setChat(answered);
      saveChat(answered);
    } catch (e) {
      setChat(before);
      setError(e instanceof ApiError ? e.userMessage : "Something went wrong. Please try again.");
    } finally {
      setPending(false);
    }
  }

  function restart() {
    clearChat();
    setChat({ messages: [] });
    setError(undefined);
  }

  return { chat, pending, error, resume, send, restart };
}
