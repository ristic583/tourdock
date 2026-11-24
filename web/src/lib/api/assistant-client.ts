import type { AssistantReply } from "@/lib/api/contracts";

import { browserApi } from "./browser";
import { unwrap } from "./client";

/** The travel assistant, from Client Components (through the BFF). */
export const assistantApi = {
  status: () => unwrap<{ enabled: boolean }>(browserApi.GET("/api/v1/assistant")),
  ask: (message: string, conversationId?: string) =>
    unwrap<AssistantReply>(browserApi.POST("/api/v1/assistant/messages", { body: { message, conversationId: conversationId ?? null } })),
};
