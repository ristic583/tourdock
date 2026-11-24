import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { ApiError } from "@/lib/api/problem";

import { AssistantChat } from "./assistant-chat";
import { plainText, showsOn } from "./chat-state";

const api = vi.hoisted(() => ({ status: vi.fn(), ask: vi.fn() }));
vi.mock("@/lib/api/assistant-client", () => ({ assistantApi: api }));
vi.mock("next/navigation", () => ({ usePathname: () => "/tours" }));

describe("AssistantChat", () => {
  beforeEach(() => {
    sessionStorage.clear();
    api.status.mockReset().mockResolvedValue({ enabled: true });
    api.ask.mockReset();
  });

  it("stays hidden when the assistant isn't configured", async () => {
    api.status.mockResolvedValue({ enabled: false });
    render(<AssistantChat />);
    await waitFor(() => expect(api.status).toHaveBeenCalled());
    expect(screen.queryByRole("button", { name: /ask tourdock/i })).toBeNull();
  });

  it("asks, shows the answer with links to the cited tours and continues the same conversation", async () => {
    api.ask.mockResolvedValue({
      conversationId: "c1",
      reply: "Try the **Sunset Cruise**.",
      tours: [{ id: "t1", title: "Sunset Cruise", url: "https://example.com/tours/t1", details: null, departures: [] }],
      turnsLeft: 19,
    });
    render(<AssistantChat />);
    fireEvent.click(await screen.findByRole("button", { name: /ask tourdock/i }));

    fireEvent.change(screen.getByLabelText("Your question"), { target: { value: "A boat trip?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("Try the Sunset Cruise.")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Sunset Cruise" }).getAttribute("href")).toBe("/tours/t1");
    expect(api.ask).toHaveBeenCalledWith("A boat trip?", undefined);

    fireEvent.change(screen.getByLabelText("Your question"), { target: { value: "For four?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    await waitFor(() => expect(api.ask).toHaveBeenLastCalledWith("For four?", "c1"));
  });

  it("shows a cited tour's photo, place, price and next dates, each date linking to that day", async () => {
    api.ask.mockResolvedValue({
      conversationId: "c2",
      reply: "The Lake Cruise fits.",
      tours: [
        {
          id: "t2",
          title: "Lake Cruise",
          url: "https://example.com/tours/t2",
          details: {
            id: "t2", title: "Lake Cruise", city: "Ohrid", countryCode: "MK", timeZoneId: "Europe/Skopje", agencyName: "Lake Co",
            durationMinutes: 360, maxGuests: 12, bookingMode: "Shared", pricePerPerson: { amount: 35, currency: "EUR" }, privatePrice: null,
            nextDepartureAt: "2027-04-01T16:00:00Z", nextDeparture: null, averageRating: 4.6, reviewCount: 12,
            coverPhotoUrl: "https://photos.example/t2.jpg", distanceKm: null,
          },
          departures: [
            { id: "d1", startsAt: "2027-04-01T16:00:00Z", localStart: "2027-04-01T18:00:00", remainingSeats: 2, capacity: 12 },
            { id: "d2", startsAt: "2027-04-03T07:00:00Z", localStart: "2027-04-03T09:00:00", remainingSeats: 12, capacity: 12 },
          ],
        },
      ],
      turnsLeft: 19,
    });
    render(<AssistantChat />);
    fireEvent.click(await screen.findByRole("button", { name: /ask tourdock/i }));
    fireEvent.change(screen.getByLabelText("Your question"), { target: { value: "A lake trip?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    const card = (await screen.findByRole("heading", { name: "Lake Cruise" })).closest("article")!;
    expect(card.textContent).toContain("Ohrid, MK");
    expect(card.textContent).toContain("€35");
    expect(card.textContent).toContain("/ person");
    expect(card.querySelector("img")?.getAttribute("src")).toBe("https://photos.example/t2.jpg");
    const dates = screen.getByRole("list", { name: "Next departures of Lake Cruise" });
    expect(dates.querySelectorAll("li")).toHaveLength(2);
    const first = screen.getByRole("link", { name: /Thu 1 Apr/ });
    expect(first.getAttribute("href")).toBe("/tours/t2?date=2027-04-01");
    expect(first.textContent).toContain("2 seats");
  });

  it("shows why a question wasn't answered and drops it from the chat", async () => {
    api.ask.mockRejectedValue(new ApiError(503, { status: 503, code: "Assistant.Unavailable", detail: "The travel assistant is unavailable right now." }));
    render(<AssistantChat />);
    fireEvent.click(await screen.findByRole("button", { name: /ask tourdock/i }));

    fireEvent.click(screen.getByRole("button", { name: /calm boat trip/i }));

    expect((await screen.findByRole("alert")).textContent).toContain("unavailable");
    expect(screen.queryByText("A calm boat trip for a family with small kids", { selector: "p" })).toBeNull();
  });
});

describe("chat helpers", () => {
  it("offers the chat only where people browse tours", () => {
    expect(showsOn("/")).toBe(true);
    expect(showsOn("/tours/abc")).toBe(true);
    expect(showsOn("/agency/1/tours")).toBe(false);
    expect(showsOn("/account")).toBe(false);
  });

  it("strips markdown the model might add", () => {
    expect(plainText("## Pick\n**Bold** and\n- item")).toBe("Pick\nBold and\n• item");
  });
});
