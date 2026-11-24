import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { CopyField } from "./copy-field";

describe("CopyField", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("shows the value and copies it to the clipboard", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    render(<CopyField label="Account address" value="https://tours.example.com/mcp/account" />);

    expect(screen.getByText("https://tours.example.com/mcp/account")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Copy Account address" }));

    expect(writeText).toHaveBeenCalledWith("https://tours.example.com/mcp/account");
    expect(await screen.findByRole("button", { name: "Account address copied" })).toBeTruthy();
  });

  it("selects the text for the visitor to copy when the page may not write to the clipboard", async () => {
    vi.stubGlobal("navigator", { clipboard: { writeText: vi.fn().mockRejectedValue(new DOMException("denied", "NotAllowedError")) } });
    render(<CopyField label="Account address" value="https://tours.example.com/mcp/account" />);

    fireEvent.click(screen.getByRole("button", { name: "Copy Account address" }));

    expect(await screen.findByText("Press Ctrl+C")).toBeTruthy();
    expect(window.getSelection()?.toString()).toBe("https://tours.example.com/mcp/account");
  });
});
