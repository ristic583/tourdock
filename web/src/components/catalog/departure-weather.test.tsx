import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { DepartureWeather } from "@/lib/api/contracts";
import { RatesNote } from "@/components/catalog/approx-price";

import { WeatherView } from "./departure-weather";

const base: DepartureWeather = {
  departureId: "d1",
  startsAt: "2026-06-04T07:00:00+00:00",
  status: "Available",
  forecastAvailableFrom: null,
  forecast: {
    time: "2026-06-04T07:00:00+00:00",
    temperatureC: 23.4,
    precipitationProbability: 10,
    windSpeedKmh: 11.5,
    windGustsKmh: 18.2,
    weatherCode: 2,
    summary: "Partly cloudy",
    roughConditions: false,
  },
};

describe("WeatherView", () => {
  it("shows the forecast at the departure's local time", () => {
    render(<WeatherView weather={base} timeZone="Europe/Zagreb" />);
    expect(screen.getByRole("heading")).toHaveTextContent("Weather on Thu 4 Jun at 09:00");
    expect(screen.getByText("23°C")).toBeInTheDocument();
    expect(screen.getByText("Partly cloudy")).toBeInTheDocument();
    expect(screen.getByText("10% rain")).toBeInTheDocument();
    expect(screen.getByText("12 km/h, gusts 18")).toBeInTheDocument();
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
  });

  it("warns about rough conditions", () => {
    render(<WeatherView weather={{ ...base, forecast: { ...base.forecast!, windGustsKmh: 52, roughConditions: true } }} timeZone="Europe/Zagreb" />);
    expect(screen.getByRole("note")).toHaveTextContent("The agency may reschedule");
  });

  it("says when a far-off forecast becomes available", () => {
    render(<WeatherView weather={{ ...base, status: "NotYetAvailable", forecast: null, forecastAvailableFrom: "2026-05-21T07:00:00+00:00" }} timeZone="Europe/Zagreb" />);
    expect(screen.getByText(/available from Thu 21 May/)).toBeInTheDocument();
  });

  it.each(["Unavailable", "Past"] as const)("hides itself when the status is %s", (status) => {
    const { container } = render(<WeatherView weather={{ ...base, status, forecast: null }} timeZone="Europe/Zagreb" />);
    expect(container).toBeEmptyDOMElement();
  });
});

describe("RatesNote", () => {
  it("explains approximate prices and flags stale rates", () => {
    const { rerender } = render(<RatesNote rate={{ currency: "USD", rate: 1.1, date: "2026-11-06", stale: false }} />);
    expect(screen.getByText(/USD prices are approximate \(ECB rate of 6 Nov\)\. You pay in EUR\./)).toBeInTheDocument();
    rerender(<RatesNote rate={{ currency: "USD", rate: 1.1, date: "2026-11-04", stale: true }} />);
    expect(screen.getByText(/last available ECB rate, 4 Nov/)).toBeInTheDocument();
  });

  it("renders nothing for EUR", () => {
    const { container } = render(<RatesNote rate={null} />);
    expect(container).toBeEmptyDOMElement();
  });
});
