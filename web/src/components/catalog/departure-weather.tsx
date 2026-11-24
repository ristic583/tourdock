"use client";

import { useQuery } from "@tanstack/react-query";
import { Cloud, CloudDrizzle, CloudFog, CloudLightning, CloudRain, CloudSnow, CloudSun, Droplets, Sun, TriangleAlert, Wind } from "lucide-react";

import { useDepartureSelection } from "@/components/catalog/departure-selection";
import type { DepartureWeather } from "@/lib/api/contracts";
import { browserApi } from "@/lib/api/browser";
import { unwrap } from "@/lib/api/client";
import { formatInZone } from "@/lib/format";

/** Icon for a WMO weather code (as used by Open-Meteo). */
export function WeatherIcon({ code, className }: { code: number; className?: string }) {
  const props = { className, "aria-hidden": true } as const;
  if (code <= 1) return <Sun {...props} />;
  if (code === 2) return <CloudSun {...props} />;
  if (code === 45 || code === 48) return <CloudFog {...props} />;
  if (code >= 51 && code <= 57) return <CloudDrizzle {...props} />;
  if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return <CloudRain {...props} />;
  if ((code >= 71 && code <= 77) || code === 85 || code === 86) return <CloudSnow {...props} />;
  if (code >= 95) return <CloudLightning {...props} />;
  return <Cloud {...props} />;
}

/**
 * Forecast for the departure picked in the booking panel (or the first bookable one before anything is picked).
 * Purely informational: when the weather service is down, or the departure is in the past, the card isn't shown.
 */
export function DepartureWeatherCard({ fallbackDepartureId, timeZone }: { fallbackDepartureId?: string; timeZone: string }) {
  const { departure } = useDepartureSelection();
  const departureId = departure?.id ?? fallbackDepartureId;
  const weather = useQuery({
    queryKey: ["weather", departureId],
    queryFn: () => unwrap<DepartureWeather>(browserApi.GET("/api/v1/departures/{departureId}/weather", { params: { path: { departureId: departureId! } } })),
    enabled: Boolean(departureId),
    staleTime: 10 * 60 * 1000,
    retry: false,
  });

  if (!departureId || weather.isError) return null;
  return <WeatherView weather={weather.data} loading={weather.isPending} timeZone={timeZone} />;
}

export function WeatherView({ weather, loading, timeZone }: { weather?: DepartureWeather; loading?: boolean; timeZone: string }) {
  if (loading) return <div className="h-[132px] animate-pulse rounded-card bg-surface-2" aria-hidden />;
  if (!weather || weather.status === "Unavailable" || weather.status === "Past") return null;

  const when = formatInZone(weather.startsAt, timeZone);
  const heading = (
    <h2 id="weather-heading" className="font-sans text-[15px] font-semibold tracking-normal">
      Weather on {when.date} at {when.time}
    </h2>
  );

  if (weather.status === "NotYetAvailable" || !weather.forecast) {
    const from = weather.forecastAvailableFrom ? formatInZone(weather.forecastAvailableFrom, timeZone).date : undefined;
    return (
      <section aria-labelledby="weather-heading" className="rounded-card border border-line bg-surface px-[18px] py-4">
        {heading}
        <p className="mt-1 text-sm text-muted-foreground">The forecast for this departure will be available{from ? ` from ${from}` : " closer to the date"}.</p>
      </section>
    );
  }

  const f = weather.forecast;
  return (
    <section aria-labelledby="weather-heading" className="rounded-card border border-line bg-surface px-[18px] py-4">
      {heading}
      <div className="mt-3 flex flex-wrap items-center gap-x-6 gap-y-3">
        <div className="flex items-center gap-3">
          <span className="grid size-12 place-items-center rounded-[14px] bg-primary-soft text-primary"><WeatherIcon code={f.weatherCode} className="size-6" /></span>
          <div>
            <p className="font-display text-2xl font-bold">{Math.round(f.temperatureC)}°C</p>
            <p className="text-sm text-muted-foreground">{f.summary}</p>
          </div>
        </div>
        <dl className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
          {f.precipitationProbability !== null && (
            <div className="flex items-center gap-1.5">
              <Droplets className="size-4 text-muted-foreground" aria-hidden />
              <dt className="sr-only">Chance of rain</dt>
              <dd>{f.precipitationProbability}% rain</dd>
            </div>
          )}
          <div className="flex items-center gap-1.5">
            <Wind className="size-4 text-muted-foreground" aria-hidden />
            <dt className="sr-only">Wind</dt>
            <dd>{Math.round(f.windSpeedKmh)} km/h, gusts {Math.round(f.windGustsKmh)}</dd>
          </div>
        </dl>
      </div>
      {f.roughConditions && (
        <p role="note" className="mt-3 flex items-start gap-2 rounded-control bg-[var(--status-pending-bg)] px-3.5 py-2.5 text-sm font-medium text-[var(--status-pending-ink)]">
          <TriangleAlert className="mt-0.5 size-4 flex-none" aria-hidden />
          Windy or stormy conditions forecast. The agency may reschedule if it isn&apos;t safe to go out.
        </p>
      )}
      <p className="mt-3 text-xs text-muted-foreground">
        Forecast by{" "}
        <a href="https://open-meteo.com" target="_blank" rel="noopener noreferrer" className="text-primary underline underline-offset-2 hover:text-primary-hover">Open-Meteo<span className="sr-only"> (opens in a new tab)</span></a>
        {" "}(CC BY 4.0). Forecasts change; check again before you go.
      </p>
    </section>
  );
}
