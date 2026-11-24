"use client";

import { ArrowLeft, ArrowRight, ImagePlus, Star, Trash2 } from "lucide-react";
import { useRouter } from "next/navigation";
import { useId, useRef, useState } from "react";

import { FormAlert } from "@/components/feedback/form-alert";
import { Button } from "@/components/ui/button";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { agencyApi } from "@/lib/api/agency-client";
import type { TourPhoto } from "@/lib/api/contracts";
import { ApiError } from "@/lib/api/problem";
import { cn } from "@/lib/utils";

export const MAX_PHOTO_BYTES = 10 * 1024 * 1024;
export const PHOTO_TYPES = ["image/jpeg", "image/png", "image/webp"];

const PHOTO_ERRORS: Record<string, string> = {
  "Photo.UnsupportedFormat": "Use a JPEG, PNG or WebP image.",
  "Photo.TooSmall": "This image is too small. Use a larger photo.",
  "Photo.TooLarge": "This image is larger than 10 MB.",
};

/** Checks a file before uploading it, with the same limits as the API. */
export function photoProblem(file: Pick<File, "type" | "size" | "name">): string | undefined {
  if (!PHOTO_TYPES.includes(file.type)) return `${file.name}: use a JPEG, PNG or WebP image.`;
  if (file.size > MAX_PHOTO_BYTES) return `${file.name}: larger than 10 MB.`;
  return undefined;
}

function message(error: unknown, fallback: string): string {
  if (error instanceof ApiError) return (error.problem.code && PHOTO_ERRORS[error.problem.code]) || error.userMessage;
  return fallback;
}

/** Upload, set the cover, reorder and delete a tour's photos. Order and cover show on the public tour page. */
export function PhotoManager({ agencyId, tourId, photos }: { agencyId: string; tourId: string; photos: TourPhoto[] }) {
  const router = useRouter();
  const inputId = useId();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState<string>();
  const [errors, setErrors] = useState<string[]>([]);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setErrors([]);
    try {
      await action();
      router.refresh();
    } catch (caught) {
      setErrors([message(caught, "That didn't work. Please try again.")]);
    } finally {
      setBusy(false);
    }
  }

  async function upload(files: FileList | null) {
    if (!files?.length) return;
    const list = [...files];
    const problems = list.map(photoProblem).filter((problem): problem is string => Boolean(problem));
    const valid = list.filter((file) => !photoProblem(file));
    setErrors(problems);
    setBusy(true);
    for (const [index, file] of valid.entries()) {
      setProgress(`Uploading ${index + 1} of ${valid.length}…`);
      try {
        await agencyApi.uploadPhoto(agencyId, tourId, file);
      } catch (caught) {
        problems.push(`${file.name}: ${message(caught, "upload failed.")}`);
        setErrors([...problems]);
      }
    }
    setProgress(undefined);
    setBusy(false);
    if (input.current) input.current.value = "";
    router.refresh();
  }

  function move(index: number, by: number) {
    const ids = photos.map((photo) => photo.id);
    const [moved] = ids.splice(index, 1);
    ids.splice(index + by, 0, moved);
    return run(() => agencyApi.reorderPhotos(agencyId, tourId, ids));
  }

  return (
    <div className="flex flex-col gap-4">
      {photos.length ? (
        <ol className="grid grid-cols-[repeat(auto-fill,minmax(180px,1fr))] gap-3" aria-label="Photos in display order">
          {photos.map((photo, index) => (
            <li key={photo.id} className={cn("overflow-hidden rounded-card border bg-surface", photo.isCover ? "border-primary" : "border-line")}>
              <div className="relative aspect-[4/3] bg-surface-2">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={photo.urls.thumbnail} alt={`Photo ${index + 1}${photo.isCover ? " (cover)" : ""}`} loading="lazy" className="absolute inset-0 size-full object-cover" />
                {photo.isCover && <span className="absolute top-2 left-2 rounded-full bg-primary px-2 py-0.5 text-xs font-semibold text-primary-foreground">Cover</span>}
              </div>
              <div className="flex flex-wrap items-center gap-1 p-2">
                <Button variant="ghost" size="sm" disabled={busy || index === 0} onClick={() => move(index, -1)} aria-label={`Move photo ${index + 1} earlier`}><ArrowLeft /></Button>
                <Button variant="ghost" size="sm" disabled={busy || index === photos.length - 1} onClick={() => move(index, 1)} aria-label={`Move photo ${index + 1} later`}><ArrowRight /></Button>
                {!photo.isCover && (
                  <Button variant="ghost" size="sm" disabled={busy} onClick={() => run(() => agencyApi.setCover(agencyId, tourId, photo.id))} aria-label={`Make photo ${index + 1} the cover`}><Star /> Cover</Button>
                )}
                <ConfirmDialog
                  tone="danger"
                  trigger={<Button variant="ghost" size="sm" className="ml-auto" disabled={busy} aria-label={`Delete photo ${index + 1}`}><Trash2 /></Button>}
                  title={`Delete photo ${index + 1}?`}
                  description={photo.isCover ? "This is the cover. The next photo becomes the cover. This can't be undone." : "It disappears from the tour page. This can't be undone."}
                  confirmLabel="Delete photo"
                  pendingLabel="Deleting…"
                  onConfirm={() => agencyApi.deletePhoto(agencyId, tourId, photo.id)}
                  onDone={() => router.refresh()}
                />
              </div>
            </li>
          ))}
        </ol>
      ) : (
        <p className="text-sm text-muted-foreground">No photos yet. Travelers see an illustration until you add some.</p>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <label htmlFor={inputId} className={cn("inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-control border border-line bg-surface px-4 text-sm font-semibold hover:border-muted-foreground has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-primary", busy && "pointer-events-none opacity-50")}>
          <ImagePlus className="size-4" aria-hidden /> Upload photos
          <input ref={input} id={inputId} type="file" accept={PHOTO_TYPES.join(",")} multiple className="sr-only" disabled={busy} onChange={(event) => upload(event.target.files)} />
        </label>
        <span className="text-[13px] text-muted-foreground">JPEG, PNG or WebP, up to 10 MB each.</span>
        <span role="status" className="text-sm font-medium">{progress}</span>
      </div>
      {errors.map((error) => <FormAlert key={error} message={error} />)}
    </div>
  );
}
