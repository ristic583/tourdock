import { AlertTriangle } from "lucide-react";

/** Form-level error (for example wrong credentials). Orange is reserved for warnings like this. */
export function FormAlert({ message }: { message?: string }) {
  if (!message) return null;
  return (
    <div role="alert" className="flex items-start gap-2.5 rounded-control bg-urgent-soft px-3.5 py-3 text-sm text-urgent-ink">
      <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden />
      <span>{message}</span>
    </div>
  );
}
