import type { CitedTour } from "@/lib/api/contracts";

/** Longest message the API accepts. */
export const MAX_MESSAGE_LENGTH = 500;

export const EXAMPLES = [
  "A calm boat trip for a family with small kids",
  "Mountain hikes in Serbia this weekend",
  "Something special for two near Split, under €100 each",
];

export type ChatMessage = { role: "user" | "assistant"; text: string; tours?: CitedTour[] };

export type ChatState = { conversationId?: string; turnsLeft?: number; messages: ChatMessage[] };

const KEY = "tourdock-assistant";

/** The chat is offered where people browse tours, not in accounts or back offices. */
export function showsOn(pathname: string): boolean {
  return pathname === "/" || pathname === "/tours" || pathname.startsWith("/tours/");
}

/** The model is asked for plain text; this strips markdown it might still add (bold, headings, list bullets). */
export function plainText(text: string): string {
  return text
    .replace(/\*\*(.+?)\*\*/g, "$1")
    .replace(/^#{1,6}\s+/gm, "")
    .replace(/^\s*[-*]\s+/gm, "• ")
    .trim();
}

/** Kept for the browser tab's lifetime, like the server keeps the conversation (24 hours at most). */
export function loadChat(): ChatState {
  try {
    const stored = JSON.parse(sessionStorage.getItem(KEY) ?? "null") as ChatState | null;
    return stored && Array.isArray(stored.messages) ? stored : { messages: [] };
  } catch {
    return { messages: [] };
  }
}

export function saveChat(chat: ChatState): void {
  try {
    sessionStorage.setItem(KEY, JSON.stringify(chat));
  } catch {
    // Private mode or a full quota: the chat still works, it just won't survive navigation.
  }
}

export function clearChat(): void {
  try {
    sessionStorage.removeItem(KEY);
  } catch {
    // nothing stored
  }
}
