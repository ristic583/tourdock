import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/problem";

import { ConfirmDialog } from "./confirm-dialog";

function setup(props: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) {
  const onConfirm = props.onConfirm ?? vi.fn().mockResolvedValue(undefined);
  const onDone = vi.fn();
  render(
    <ConfirmDialog
      trigger={<Button>Delete</Button>}
      title="Delete the tour?"
      description="This can't be undone."
      confirmLabel="Delete tour"
      pendingLabel="Deleting…"
      tone="danger"
      onConfirm={onConfirm}
      onDone={onDone}
      {...props}
    />,
  );
  fireEvent.click(screen.getByRole("button", { name: "Delete" }));
  return { onConfirm, onDone };
}

describe("ConfirmDialog", () => {
  it("asks before running the action and runs it once on confirm", async () => {
    const { onConfirm, onDone } = setup();
    const dialog = screen.getByRole("alertdialog", { name: "Delete the tour?" });
    expect(dialog).toHaveTextContent("This can't be undone.");
    expect(onConfirm).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Delete tour" }));
    await waitFor(() => expect(onDone).toHaveBeenCalledOnce());
    expect(onConfirm).toHaveBeenCalledOnce();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("focuses Cancel first for a dangerous action, so Enter doesn't confirm by accident", () => {
    setup();
    expect(screen.getByRole("button", { name: "Cancel" })).toHaveFocus();
  });

  it("focuses the confirm button for a harmless action", () => {
    setup({ tone: "default", confirmLabel: "Approve" });
    expect(screen.getByRole("button", { name: "Approve" })).toHaveFocus();
  });

  it("cancel closes without running the action", () => {
    const { onConfirm } = setup();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("stays open with a pending button while the action runs, then shows the API error", async () => {
    let fail!: (error: unknown) => void;
    const onConfirm = vi.fn(() => new Promise((_, reject) => { fail = reject; }));
    const { onDone } = setup({ onConfirm });
    fireEvent.click(screen.getByRole("button", { name: "Delete tour" }));

    const pending = await screen.findByRole("button", { name: "Deleting…" });
    expect(pending).toBeDisabled();
    expect(pending).toHaveAttribute("aria-busy", "true");
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();

    fail(new ApiError(409, { title: "Conflict", status: 409, code: "Tour.HasBookings", detail: "This tour has bookings." }));
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    expect(onDone).not.toHaveBeenCalled();
  });

  it("keeps confirm disabled until extra fields are valid", () => {
    setup({ confirmDisabled: true });
    expect(screen.getByRole("button", { name: "Delete tour" })).toBeDisabled();
  });
});
