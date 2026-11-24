"use client";

import { MapPin } from "lucide-react";
import { useEffect, useId, useRef, useState, type KeyboardEvent, type MouseEvent } from "react";

import type { CitySuggestion, CountrySuggestion, PlaceSuggestions } from "@/lib/api/contracts";
import { browserApi } from "@/lib/api/browser";
import { cn } from "@/lib/utils";

type Option =
  | { kind: "country"; key: string; country: CountrySuggestion }
  | { kind: "city"; key: string; city: CitySuggestion; under?: string };

/** Countries first, each followed by its places, then the other places: one flat list for the arrow keys. */
function flatten(data: PlaceSuggestions): Option[] {
  return [
    ...data.countries.flatMap((country): Option[] => [
      { kind: "country", key: `country-${country.code}`, country },
      ...country.places.map((city): Option => ({ kind: "city", key: `${country.code}-${city.id}`, city, under: country.name })),
    ]),
    ...data.cities.map((city): Option => ({ kind: "city", key: city.id, city })),
  ];
}

/** 🇷🇸 for "RS": the two regional-indicator letters that systems draw as the country's flag. */
export function flagEmoji(code: string) {
  return /^[A-Za-z]{2}$/.test(code) ? String.fromCodePoint(...[...code.toUpperCase()].map((c) => 0x1f1e6 + c.charCodeAt(0) - 65)) : "";
}

function Flag({ code, className }: { code: string; className?: string }) {
  return <span className={cn("shrink-0 leading-none", className)} aria-hidden>{flagEmoji(code)}</span>;
}

function toursText(count: number) {
  return `${count} ${count === 1 ? "tour" : "tours"}`;
}

type CityComboboxProps = {
  id?: string;
  /** Text field name for plain forms (the search bar); the picked place goes in `<idName>`. */
  name?: string;
  idName?: string;
  /**
   * Hidden field for a picked country's code (the search bar). Setting it also suggests countries: "Serbia" lists the
   * country with its top places nested underneath. Without it the list holds places only (the agency's tour form).
   */
  countryName?: string;
  defaultValue?: string;
  defaultCityId?: string;
  defaultCountry?: string;
  placeholder?: string;
  /** "bare" for the search bar, "field" like the other inputs. */
  variant?: "bare" | "field";
  /** Called with the picked place, or null once the visitor edits the text again. */
  onPick?: (city: CitySuggestion | null) => void;
  /** Every change of the text, picked or typed (for controlled forms). */
  onTextChange?: (text: string) => void;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
};

/**
 * Place autocomplete (WAI-ARIA combobox with a listbox). Asks the API as the visitor types (debounced, older requests
 * cancelled), ignoring case and accents, and lists places with tours nearby first. Typing without picking still works:
 * the API then searches around the place the text most likely means, or in the country it names.
 */
export function CityCombobox({
  id: idProp,
  name,
  idName,
  countryName,
  defaultValue = "",
  defaultCityId = "",
  defaultCountry = "",
  placeholder = "City or destination",
  variant = "bare",
  onPick,
  onTextChange,
  ...aria
}: CityComboboxProps) {
  const generated = useId();
  const id = idProp ?? generated;
  const listId = `${id}-places`;
  const [text, setText] = useState(defaultValue);
  const [cityId, setCityId] = useState(defaultCityId);
  const [country, setCountry] = useState(defaultCountry);
  const [options, setOptions] = useState<Option[]>([]);
  const withCountries = Boolean(countryName);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const typed = useRef(false);

  useEffect(() => {
    const query = text.trim();
    if (!typed.current || !query) {
      setOptions([]);
      return;
    }
    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      const params = { params: { query: { q: query, limit: 8 } }, signal: controller.signal };
      let next: Option[] = [];
      if (withCountries) {
        const { data } = await browserApi.GET("/api/v1/places", params).catch(() => ({ data: undefined }));
        if (data) next = flatten(data as PlaceSuggestions);
      } else {
        const { data } = await browserApi.GET("/api/v1/cities", params).catch(() => ({ data: undefined }));
        next = ((data as CitySuggestion[] | undefined) ?? []).map((city) => ({ kind: "city", key: city.id, city }));
      }
      if (controller.signal.aborted) return;
      setOptions(next);
      setActive(-1);
      setOpen(true);
    }, 150);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [text, withCountries]);

  function pick(option: Option) {
    typed.current = false;
    const label = option.kind === "country" ? option.country.name : option.city.name;
    setText(label);
    onTextChange?.(label);
    setOptions([]);
    setOpen(false);
    if (option.kind === "country") {
      setCountry(option.country.code);
      setCityId("");
      onPick?.(null);
    } else {
      setCountry("");
      setCityId(option.city.id);
      onPick?.(option.city);
    }
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (!open || options.length === 0) return;
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      setActive((index) => (index + step + options.length) % options.length);
    } else if (event.key === "Enter" && active >= 0) {
      event.preventDefault(); // pick instead of submitting the form
      pick(options[active]);
    } else if (event.key === "Escape") {
      setOpen(false);
    }
  }

  const expanded = open && options.length > 0;
  return (
    <div className="relative w-full min-w-0">
      <input
        id={id}
        // A picked country is searched by its code alone; the name in the box would also be read as a place.
        name={country ? undefined : name}
        role="combobox"
        aria-expanded={expanded}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={expanded && active >= 0 ? `${listId}-${active}` : undefined}
        aria-invalid={aria["aria-invalid"]}
        aria-describedby={aria["aria-describedby"]}
        autoComplete="off"
        value={text}
        placeholder={placeholder}
        onChange={(event) => {
          typed.current = true;
          setText(event.target.value);
          onTextChange?.(event.target.value);
          if (country) setCountry("");
          if (cityId) {
            setCityId("");
            onPick?.(null);
          }
        }}
        onKeyDown={onKeyDown}
        onFocus={() => options.length > 0 && setOpen(true)}
        onBlur={() => setOpen(false)}
        className={cn(
          "w-full min-w-0 outline-none placeholder:font-normal placeholder:text-muted-foreground",
          variant === "bare" && "border-0 bg-transparent p-0 text-base font-medium text-ink",
          variant === "field" &&
            "h-11 rounded-control border border-line bg-surface px-3 text-[15px] text-ink focus-visible:border-primary focus-visible:outline-2 focus-visible:outline-primary aria-invalid:border-urgent-ink",
        )}
      />
      {idName && <input type="hidden" name={idName} value={cityId} />}
      {countryName && country && <input type="hidden" name={countryName} value={country} />}
      <ul
        id={listId}
        role="listbox"
        aria-label="Places"
        hidden={!expanded}
        className="absolute top-[calc(100%+12px)] left-[-12px] z-50 max-h-80 w-[min(360px,calc(100vw-32px))] overflow-y-auto rounded-card border border-line bg-surface p-1.5 text-ink shadow-card"
      >
        {options.map((option, index) => {
          const selected = index === active;
          const rowClass = cn("flex cursor-pointer items-center gap-3 rounded-[10px] px-3 py-2.5", selected && "bg-surface-2");
          const common = {
            id: `${listId}-${index}`,
            role: "option",
            "aria-selected": selected,
            // mousedown, not click: the input's blur would close the list first.
            onMouseDown: (event: MouseEvent) => {
              event.preventDefault();
              pick(option);
            },
            onMouseEnter: () => setActive(index),
          } as const;
          // A line between the countries' groups and the other places.
          const previous = options[index - 1];
          const firstFlatCity = option.kind === "city" && !option.under && previous !== undefined && (previous.kind === "country" || Boolean(previous.under));

          if (option.kind === "country") {
            const { country } = option;
            return (
              <li
                key={option.key}
                {...common}
                aria-label={`${country.name}, whole country${country.toursCount > 0 ? `, ${toursText(country.toursCount)}` : ""}`}
                className={cn(rowClass, index > 0 && "mt-1.5 border-t border-line pt-3")}
              >
                <Flag code={country.code} className="w-4 text-base" />
                <span className="min-w-0 flex-1 truncate font-semibold">
                  {country.name}
                  <span className="font-normal text-muted-foreground"> · all places</span>
                </span>
                {country.toursCount > 0 && (
                  <span className="shrink-0 rounded-full bg-primary-soft px-2 py-0.5 text-xs font-semibold text-primary">{toursText(country.toursCount)}</span>
                )}
              </li>
            );
          }

          const { city, under } = option;
          return (
            <li
              key={option.key}
              {...common}
              aria-label={`${city.name}, ${under ?? city.countryCode}${city.toursNearby > 0 ? `, ${toursText(city.toursNearby)} nearby` : ""}`}
              className={cn(rowClass, under && "py-2 pl-9", firstFlatCity && "mt-1.5 border-t border-line pt-3")}
            >
              <MapPin className={cn("shrink-0 text-muted-foreground", under ? "size-3.5" : "size-4")} aria-hidden />
              <span className="min-w-0 flex-1 truncate font-medium">
                {city.name}
                {!under && (
                  <span className="font-normal text-muted-foreground">
                    , <Flag code={city.countryCode} className="text-sm" /> {city.countryCode}
                  </span>
                )}
              </span>
              {city.toursNearby > 0 && (
                <span className="shrink-0 rounded-full bg-primary-soft px-2 py-0.5 text-xs font-semibold text-primary">{toursText(city.toursNearby)}</span>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
