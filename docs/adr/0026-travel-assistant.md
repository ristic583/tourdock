# ADR 0026: Travel assistant with Claude

- **Status:** Accepted
- **Date:** 2026-11-24

## Context

Semantic search (ADR 0025) orders tours by a description, but a traveler still has to compare cards, open tour pages and
check dates. People ask questions instead: "something calm for two small kids near Split next weekend, under €50 each".
An assistant can run the searches, read the tours and answer, as long as every fact comes from TourDock and not from
the model's imagination.

## Decision

**Tools first, model second.** The assistant answers only from three read-only tools: `search_tours`, `get_tour` and
`get_departures`. They live in the Application layer (`AgentTools`) and call the same query handlers as the public API,
so validation, the published/active-agency filter, caching and live seat counts apply unchanged. The MCP server (ADR
0027) offers the same tools, so a tool is written once.

**Citations instead of trust.** Tool results go back to Claude as `search_result` blocks, one per tour, with the tour's
page as the source. Claude cites them; the API returns the cited tours with the answer, and the web shows them as
links. Tool text is written as short whole sentences, because cited passages are quoted.

**Model: Claude Haiku 5.5 at low effort**, adaptive thinking on. Chosen by an eval (below) against Claude Opus 5.5. The
model id is configuration (`ASSISTANT_MODEL`). The system prompt and tool definitions are cached (prompt caching);
today's date is a separate block after the cache breakpoint.

**One turn = a short tool loop.** Up to four model calls per message; the last may not call tools, so every turn ends
with an answer. Parallel tool calls are answered in one message. A refusal, a turn that ends without text, a timeout or
a provider error becomes `503 Assistant.Unavailable`, and nothing is saved.

**Conversations live on the server for 24 hours.** `assistant_conversations` keeps the Messages API conversation as
JSON. Claude's blocks are stored exactly as they came back (text with citations, thinking with its signature, tool
calls without `caller`) and replayed unchanged, which thinking blocks require; our blocks are rebuilt the same way
every time. The client holds only the conversation id. A job deletes conversations 24 hours after their last message.
Message text is never logged.

**Cost has three hard limits**, because the endpoint is anonymous like search:
- per client (signed-in user or IP): rate-limit policy `assistant`, 6 messages a minute and 30 a day, so no one visitor
  can use up the day's cap for everyone (the windows are in memory; a restart resets them);
- per conversation: 20 messages, then "start a new one";
- per day, all visitors: 300 messages (`assistant_daily_usage`, one atomic `INSERT ... ON CONFLICT ... RETURNING`).
Plus a prepaid API key with auto-reload off. Token usage is a metric (`tourdock.assistant.tokens` by model and kind).

**When search can't rank.** If semantic search falls back to date order (ADR 0025), `search_tours` adds a note that
the results are only filtered and may not fit the description, so the model doesn't present them as matches.

**Off without a key.** No `ANTHROPIC_API_KEY`: `GET /api/v1/assistant` says `enabled: false` and the web hides the chat.

## Eval

`tools/TourDock.AssistantEval` asks a running TourDock 30 questions (two of them follow-ups in the same conversation)
and checks each answer: the right kind of tour is cited, cited tours are in the asked country, under the asked budget,
of the asked booking type, the reply is in the traveler's language, off-topic and prompt-injection requests cite
nothing and leak nothing. It reports the pass rate, tokens, cost and latency. It calls the real model, so it's run by
hand, not in CI.

Run on 2026-11-24 against the demo catalog (38 tours), effort `low`, 40 seconds between questions so the free Voyage
key wasn't throttled (no 429s during either run):

| Model | Passed | Cost (32 messages) | Per message | Latency per message |
|---|---|---|---|---|
| Claude Haiku 5.5 | 28/30 (93%) | $0.015 | $0.0005 | 3.9 s |
| Claude Opus 5.5 | 30/30 (100%) | $0.59 | $0.018 | 6.8 s |

Haiku's two misses (a snowshoe walk, a romantic sunset trip) named fitting tours but cited none, so the web showed no
tour links; asked again, it cited them. Nothing was invented in either run. Haiku is about 39 times cheaper and almost
twice as fast, so it stays the default; Opus is one setting away (`ASSISTANT_MODEL`).

Earlier runs found two real problems: a fast run without pauses hit the free Voyage key's limit of 3 requests a minute,
so searches fell back to date order and answers suffered; and the note added for that case sat beside `search_result`
blocks in one tool result, which the API rejects (now it rides in the first result's text).

## Consequences

- ✅ Answers are grounded in live data and link to the tours they mention; the model can't book or pay.
- ✅ The same tools serve the site and MCP clients.
- ✅ Spend is bounded by limits that don't depend on the model behaving.
- ⚠️ Answers take a few seconds (one or more model calls plus tool calls); responses aren't streamed yet.
- ⚠️ A free Voyage key without a payment method allows only a few requests a minute. Under that limit the assistant's
  searches fall back to date order, and answers get worse; add a payment method (the free tokens still apply) for
  anything beyond a demo.
- ⚠️ Conversations are anonymous and short-lived by design; signed-in travelers don't see their bookings in the chat.
