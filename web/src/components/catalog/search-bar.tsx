import { CalendarDays, MapPin, Search, Sparkles, UsersRound } from "lucide-react";
import { useId, type ComponentType, type ReactNode } from "react";

import { CityCombobox } from "@/components/catalog/city-combobox";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { MAX_TEXT_LENGTH, defaultSort, placeLabel, type CatalogSearch } from "@/lib/catalog-search";

function Field({ id, label, icon: Icon, className, children }: { id: string; label: string; icon: ComponentType<{ className?: string }>; className: string; children: ReactNode }) {
  return (
    <div className={`flex min-w-0 flex-col gap-0.5 rounded-[14px] px-4 py-2.5 focus-within:bg-surface-2 ${className}`}>
      <label htmlFor={id} className="label-caps">{label}</label>
      <div className="flex items-center gap-2">
        <Icon className="size-[18px] flex-none text-muted-foreground" aria-hidden />
        {children}
      </div>
    </div>
  );
}

const inputClass = "w-full min-w-0 border-0 bg-transparent p-0 text-base font-medium text-ink outline-none placeholder:text-muted-foreground placeholder:font-normal";

/**
 * The trip in the visitor's words, then where / when / guests, as one rounded bar (the design's hero search). A plain GET form to `/tours`,
 * so it works before hydration. Filters chosen in the sidebar ride along as hidden fields; a new search starts on page 1.
 */
export function SearchBar({ search, keepFilters = false }: { search: CatalogSearch; keepFilters?: boolean }) {
  // Unique per instance: Next keeps the previous page mounted (hidden), so two search bars can coexist.
  const id = useId();
  return (
    <form action="/tours" noValidate role="search" aria-label="Search tours" className="flex flex-wrap items-stretch gap-1 rounded-hero border border-line bg-surface p-2 shadow-card">
      <Field id={`${id}-q`} label="Describe the trip" icon={Sparkles} className="flex-[1_1_100%]">
        <input
          id={`${id}-q`}
          name="q"
          type="search"
          maxLength={MAX_TEXT_LENGTH}
          defaultValue={search.q}
          placeholder="Optional, e.g. calm boat trip for a family with small kids"
          className={inputClass}
        />
      </Field>
      <div className="mx-4 h-px flex-[1_1_100%] bg-line" aria-hidden />
      <Field id={`${id}-city`} label="Where" icon={MapPin} className="flex-[2_1_240px]">
        <CityCombobox
          id={`${id}-city`}
          name="city"
          idName="cityId"
          countryName="country"
          defaultValue={placeLabel(search)}
          defaultCityId={search.cityId}
          defaultCountry={search.city ? undefined : search.country}
          placeholder="Country, city or destination"
        />
      </Field>
      <div className="my-2.5 hidden w-px bg-line sm:block" aria-hidden />
      <Field id={`${id}-date`} label="When" icon={CalendarDays} className="flex-[1_1_180px]">
        <DatePicker id={`${id}-date`} label="When" name="date" defaultValue={search.date} variant="bare" placeholder="Any date" clearable disablePast />
      </Field>
      <div className="my-2.5 hidden w-px bg-line sm:block" aria-hidden />
      <Field id={`${id}-guests`} label="Guests" icon={UsersRound} className="flex-[1_1_140px]">
        <input id={`${id}-guests`} name="guests" type="number" min={1} max={100} inputMode="numeric" defaultValue={search.guests} className={inputClass} />
      </Field>
      {keepFilters && (
        <>
          {search.minPrice !== undefined && <input type="hidden" name="minPrice" value={search.minPrice} />}
          {search.maxPrice !== undefined && <input type="hidden" name="maxPrice" value={search.maxPrice} />}
          {search.mode && <input type="hidden" name="mode" value={search.mode} />}
          {/* A new place or description gets its own default order; an explicit price or rating order carries over. */}
          {search.sort !== defaultSort(search) && search.sort !== "Nearest" && search.sort !== "Relevance" && (
            <input type="hidden" name="sort" value={search.sort} />
          )}
        </>
      )}
      <Button type="submit" size="lg" className="flex-[1_0_auto] rounded-2xl sm:flex-none">
        <Search className="size-[18px]" aria-hidden /> Search tours
      </Button>
    </form>
  );
}
