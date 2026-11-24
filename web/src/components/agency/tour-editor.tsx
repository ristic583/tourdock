"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { CheckCircle2, Plus, Trash2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useId, useState, type ReactNode } from "react";
import { useForm, useWatch } from "react-hook-form";

import { DetailsFields, PricingFields } from "@/components/agency/tour-fields";
import { FormAlert } from "@/components/feedback/form-alert";
import { useToast } from "@/components/feedback/toaster";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { Input } from "@/components/ui/input";
import { agencyApi } from "@/lib/api/agency-client";
import type { AgencyStatus, AgencyTour, RefundTier } from "@/lib/api/contracts";
import { ApiError, toActionError } from "@/lib/api/problem";
import { policySegments, segmentLabel } from "@/lib/availability";
import { formatPickedDate } from "@/lib/calendar-dates";
import { formatMoney } from "@/lib/format";
import { applyActionError } from "@/lib/forms";
import {
  detailsBody,
  detailsDefaults,
  detailsSchema,
  policyProblems,
  pricingBody,
  pricingDefaults,
  pricingSchema,
  seasonProblem,
  type DetailsValues,
  type PricingValues,
} from "@/lib/tour-forms";
import { cn } from "@/lib/utils";

type SectionProps = { agencyId: string; tour: AgencyTour };

export function Section({ id, title, description, children }: { id: string; title: string; description?: string; children: ReactNode }) {
  return (
    <section aria-labelledby={id} className="flex flex-col gap-4 rounded-card border border-line bg-surface p-5">
      <div>
        <h2 id={id} className="text-xl font-bold">{title}</h2>
        {description && <p className="mt-1 text-sm text-muted-foreground">{description}</p>}
      </div>
      {children}
    </section>
  );
}

/** "Saved ✓" next to a save button. Announced to screen readers; fades out after a few seconds. */
function Saved({ show }: { show: boolean }) {
  const [visible, setVisible] = useState(false);
  const [seen, setSeen] = useState(show);
  if (show !== seen) {
    setSeen(show);
    setVisible(show);
  }
  useEffect(() => {
    if (!visible) return;
    const timer = window.setTimeout(() => setVisible(false), 4000);
    return () => window.clearTimeout(timer);
  }, [visible]);
  return (
    <span role="status" className="inline-flex items-center gap-1.5 text-sm font-semibold text-primary">
      {visible && <><CheckCircle2 className="size-4 animate-in fade-in-0 zoom-in-75" aria-hidden />Saved</>}
    </span>
  );
}

function errorText(error: unknown, fallback: string): string {
  return error instanceof ApiError ? error.userMessage : fallback;
}

/** Publish / unpublish and delete, next to the title. Unpublish and delete ask first. */
export function TourActions({ agencyId, tour, agencyStatus = "Active" }: SectionProps & { agencyStatus?: AgencyStatus }) {
  const router = useRouter();
  const toast = useToast();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const published = tour.status === "Published";

  async function publish() {
    setBusy(true);
    setError(undefined);
    try {
      await agencyApi.publish(agencyId, tour.id);
      toast(`“${tour.title}” is live. Travelers can find and book it.`);
      router.refresh();
    } catch (caught) {
      setError(errorText(caught, "That didn't work. Please try again."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex flex-wrap gap-2">
        {published && <Button asChild variant="outline"><a href={`/tours/${tour.id}`} target="_blank" rel="noopener">View public page<span className="sr-only"> (new tab)</span></a></Button>}
        {published ? (
          <ConfirmDialog
            tone="danger"
            trigger={<Button variant="outline">Unpublish</Button>}
            title={`Unpublish “${tour.title}”?`}
            description="Travelers can't find or book it anymore. Existing bookings and departures stay as they are, and you can publish it again later."
            confirmLabel="Unpublish tour"
            pendingLabel="Unpublishing…"
            onConfirm={() => agencyApi.unpublish(agencyId, tour.id)}
            onDone={() => {
              toast(`“${tour.title}” is unpublished.`);
              router.refresh();
            }}
          />
        ) : (
          <Button pending={busy} disabled={agencyStatus !== "Active"} aria-describedby={agencyStatus !== "Active" ? "publish-hint" : undefined} onClick={publish}>{busy ? "Publishing…" : "Publish"}</Button>
        )}
        <ConfirmDialog
          tone="danger"
          trigger={<Button variant="ghost">Delete</Button>}
          title={`Delete “${tour.title}”?`}
          description="This can't be undone. A tour with bookings can't be deleted; unpublish it instead."
          confirmLabel="Delete tour"
          pendingLabel="Deleting…"
          errorFallback="We couldn't delete this tour."
          onConfirm={() => agencyApi.deleteTour(agencyId, tour.id)}
          onDone={() => {
            toast(`“${tour.title}” is deleted.`);
            router.push(`/agency/${agencyId}/tours`);
            router.refresh();
          }}
        />
      </div>
      {!published && agencyStatus !== "Active" && (
        <p id="publish-hint" className="max-w-[320px] text-right text-sm text-muted-foreground">
          {agencyStatus === "PendingApproval" ? "You can publish once a TourDock admin approves your agency." : "Publishing is paused while the agency is suspended."}
        </p>
      )}
      <FormAlert message={error} />
    </div>
  );
}

const DETAIL_FIELDS = ["title", "description", "countryCode", "city", "address", "latitude", "longitude", "timeZoneId", "durationMinutes", "maxGuests"] as const;

export function DetailsSection({ agencyId, tour }: SectionProps) {
  const router = useRouter();
  const [saved, setSaved] = useState(false);
  const form = useForm<DetailsValues>({ resolver: zodResolver(detailsSchema), defaultValues: detailsDefaults(tour) });
  const { errors, isSubmitting, isDirty } = form.formState;
  const onSubmit = form.handleSubmit(async (values) => {
    setSaved(false);
    try {
      await agencyApi.updateDetails(agencyId, tour.id, detailsBody(values));
      form.reset(values);
      setSaved(true);
      router.refresh();
    } catch (error) {
      applyActionError(toActionError(error), form.setError, DETAIL_FIELDS);
    }
  });
  return (
    <Section id="details-heading" title="Details">
      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
        <FormAlert message={errors.root?.server?.message} />
        <DetailsFields prefix="tour" form={form} />
        <div className="flex items-center gap-3">
          <Button type="submit" pending={isSubmitting} disabled={!isDirty}>{isSubmitting ? "Saving…" : "Save details"}</Button>
          <Saved show={saved && !isDirty} />
        </div>
      </form>
    </Section>
  );
}

export function PricingSection({ agencyId, tour }: SectionProps) {
  const router = useRouter();
  const [saved, setSaved] = useState(false);
  const form = useForm<PricingValues>({ resolver: zodResolver(pricingSchema), defaultValues: pricingDefaults(tour) });
  const mode = useWatch({ control: form.control, name: "bookingMode" });
  const { errors, isSubmitting, isDirty } = form.formState;
  const onSubmit = form.handleSubmit(async (values) => {
    setSaved(false);
    try {
      await agencyApi.updatePricing(agencyId, tour.id, pricingBody(values));
      form.reset(values);
      setSaved(true);
      router.refresh();
    } catch (error) {
      applyActionError(toActionError(error), form.setError, ["bookingMode", "pricePerPerson", "privatePrice"]);
    }
  });
  return (
    <Section id="pricing-heading" title="Pricing" description="Existing bookings keep the price they were made at.">
      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
        <FormAlert message={errors.root?.server?.message} />
        <PricingFields prefix="tour" register={form.register} errors={errors} mode={mode} />
        <div className="flex items-center gap-3">
          <Button type="submit" pending={isSubmitting} disabled={!isDirty}>{isSubmitting ? "Saving…" : "Save pricing"}</Button>
          <Saved show={saved && !isDirty} />
        </div>
      </form>
    </Section>
  );
}

export function SeasonsSection({ agencyId, tour }: SectionProps) {
  const router = useRouter();
  const id = useId();
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [perPerson, setPerPerson] = useState("");
  const [privately, setPrivately] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const shared = tour.bookingMode !== "Private";
  const privateOffered = tour.bookingMode !== "Shared";

  async function run(action: () => Promise<unknown>, after?: () => void) {
    setBusy(true);
    setError(undefined);
    try {
      await action();
      after?.();
      router.refresh();
    } catch (caught) {
      setError(errorText(caught, "We couldn't save the season."));
    } finally {
      setBusy(false);
    }
  }

  const add = (event: React.FormEvent) => {
    event.preventDefault();
    const problem = seasonProblem({ from, to, perPerson: shared ? perPerson : "", privately: privateOffered ? privately : "" });
    if (problem) {
      setError(problem);
      return;
    }
    run(
      () => agencyApi.addSeason(agencyId, tour.id, { from, to, pricePerPerson: shared && perPerson ? Number(perPerson) : null, privatePrice: privateOffered && privately ? Number(privately) : null }),
      () => { setFrom(""); setTo(""); setPerPerson(""); setPrivately(""); },
    );
  };

  return (
    <Section id="seasons-heading" title="Seasonal prices" description="Override the base price between two dates (inclusive), for example high season.">
      {tour.seasonalPrices.length ? (
        <ul className="divide-y divide-line rounded-control border border-line">
          {tour.seasonalPrices.map((season) => (
            <li key={season.id} className="flex flex-wrap items-center gap-3 px-4 py-3 text-sm">
              <span className="flex-1 font-semibold">{formatPickedDate(season.from)} – {formatPickedDate(season.to)}</span>
              <span className="text-muted-foreground">{[season.pricePerPerson && `${formatMoney(season.pricePerPerson)} pp`, season.privatePrice && `${formatMoney(season.privatePrice)} trip`].filter(Boolean).join(" · ")}</span>
              <ConfirmDialog
                tone="danger"
                trigger={<Button variant="ghost" size="sm" disabled={busy} aria-label={`Remove season ${formatPickedDate(season.from)} to ${formatPickedDate(season.to)}`}><Trash2 /> Remove</Button>}
                title="Remove this season?"
                description={`The base price applies again from ${formatPickedDate(season.from)} to ${formatPickedDate(season.to)}. Existing bookings keep the price they were made at.`}
                confirmLabel="Remove season"
                pendingLabel="Removing…"
                onConfirm={() => agencyApi.removeSeason(agencyId, tour.id, season.id)}
                onDone={() => router.refresh()}
              />
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-muted-foreground">No seasons: the base price applies all year.</p>
      )}
      <form noValidate onSubmit={add} className="grid gap-3 sm:grid-cols-[repeat(auto-fit,minmax(150px,1fr))]">
        <div className="flex flex-col gap-1.5 text-sm font-semibold"><label htmlFor={`${id}-from`}>From</label><DatePicker id={`${id}-from`} label="Season from" value={from} onChange={(value) => { setError(undefined); setFrom(value); if (to && value > to) setTo(""); }} /></div>
        <div className="flex flex-col gap-1.5 text-sm font-semibold"><label htmlFor={`${id}-to`}>To</label><DatePicker id={`${id}-to`} label="Season to" min={from || undefined} value={to} onChange={(value) => { setError(undefined); setTo(value); }} /></div>
        {shared && <label className="flex flex-col gap-1.5 text-sm font-semibold" htmlFor={`${id}-pp`}>Per person (€)<Input id={`${id}-pp`} type="number" inputMode="decimal" value={perPerson} onChange={(event) => { setError(undefined); setPerPerson(event.target.value); }} /></label>}
        {privateOffered && <label className="flex flex-col gap-1.5 text-sm font-semibold" htmlFor={`${id}-pt`}>Private trip (€)<Input id={`${id}-pt`} type="number" inputMode="decimal" value={privately} onChange={(event) => { setError(undefined); setPrivately(event.target.value); }} /></label>}
        <Button type="submit" variant="outline" className="self-end" pending={busy}>{busy ? "Adding…" : "Add season"}</Button>
      </form>
      <FormAlert message={error} />
    </Section>
  );
}

export function PolicySection({ agencyId, tour }: SectionProps) {
  const router = useRouter();
  const id = useId();
  const [tiers, setTiers] = useState<RefundTier[]>(tour.cancellationPolicy);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string>();
  const problems = policyProblems(tiers);
  const segments = problems.length ? [] : policySegments(tiers);

  function change(index: number, key: keyof RefundTier, value: string) {
    setSaved(false);
    setError(undefined);
    setTiers(tiers.map((tier, i) => (i === index ? { ...tier, [key]: value === "" ? Number.NaN : Number(value) } : tier)));
  }

  async function save() {
    setBusy(true);
    setSaved(false);
    setError(undefined);
    try {
      await agencyApi.updatePolicy(agencyId, tour.id, tiers);
      setSaved(true);
      router.refresh();
    } catch (caught) {
      setError(errorText(caught, "We couldn't save the policy."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Section id="policy-heading" title="Cancellation policy" description="Refund by notice. Anything closer to departure than the last tier gets no refund.">
      <ul className="flex flex-col gap-2">
        {tiers.map((tier, index) => (
          <li key={index} className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-sm font-semibold" htmlFor={`${id}-h${index}`}>
              Hours before
              <Input id={`${id}-h${index}`} className="w-32" type="number" min={0} step={1} value={Number.isNaN(tier.hoursBeforeStart) ? "" : tier.hoursBeforeStart} onChange={(event) => change(index, "hoursBeforeStart", event.target.value)} />
            </label>
            <label className="flex flex-col gap-1 text-sm font-semibold" htmlFor={`${id}-p${index}`}>
              Refund %
              <Input id={`${id}-p${index}`} className="w-28" type="number" min={0} max={100} step={1} value={Number.isNaN(tier.refundPercent) ? "" : tier.refundPercent} onChange={(event) => change(index, "refundPercent", event.target.value)} />
            </label>
            <Button variant="ghost" size="sm" onClick={() => { setSaved(false); setTiers(tiers.filter((_, i) => i !== index)); }} aria-label={`Remove tier ${index + 1}`}><Trash2 /></Button>
          </li>
        ))}
      </ul>
      <Button variant="outline" size="sm" className="self-start" onClick={() => setTiers([...tiers, { hoursBeforeStart: 0, refundPercent: 0 }])}><Plus /> Add tier</Button>

      {segments.length > 0 && (
        <ol aria-label="Policy preview" className="grid gap-1 overflow-hidden rounded-control text-xs" style={{ gridTemplateColumns: segments.map((s) => `${s.weight}fr`).join(" ") }}>
          {segments.map((segment) => (
            <li key={segment.fromHours} className={cn("min-w-0 px-2.5 py-2", segment.refundPercent >= 100 ? "bg-primary text-primary-foreground" : segment.refundPercent > 0 ? "bg-primary-soft" : "bg-surface-2")}>
              <span className="block font-bold">{segment.refundPercent > 0 ? `${segment.refundPercent}%` : "No refund"}</span>
              <span className="block">{segmentLabel(segment)}</span>
            </li>
          ))}
        </ol>
      )}
      {problems.length > 0 && <FormAlert message={problems.join(" ")} />}
      <FormAlert message={error} />
      <div className="flex items-center gap-3">
        <Button pending={busy} disabled={problems.length > 0} onClick={save}>{busy ? "Saving…" : "Save policy"}</Button>
        <Saved show={saved} />
      </div>
    </Section>
  );
}
