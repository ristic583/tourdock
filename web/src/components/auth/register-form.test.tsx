import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { RegisterForm } from "./register-form";

const register = vi.fn();
vi.mock("@/lib/auth/actions", () => ({ register: (...args: unknown[]) => register(...args) }));

async function fillAndSubmit() {
  await userEvent.type(screen.getByLabelText("Your name"), "Ana");
  await userEvent.type(screen.getByLabelText("Email"), "ana@example.com");
  await userEvent.type(screen.getByLabelText("Password"), "a-long-password");
  await userEvent.click(screen.getByRole("button", { name: "Create account" }));
}

describe("RegisterForm", () => {
  beforeEach(() => register.mockReset());

  it("asks to check the inbox after sign-up, and keeps the return path for signing in", async () => {
    register.mockResolvedValue({ ok: true, data: undefined });
    render(<RegisterForm next="/tours" />);

    await fillAndSubmit();

    expect(await screen.findByRole("heading", { name: "Check your inbox" })).toBeInTheDocument();
    expect(screen.getByRole("status")).toHaveTextContent("ana@example.com");
    expect(screen.getByRole("link", { name: "Go to sign in" })).toHaveAttribute("href", "/login?next=%2Ftours");
    expect(register).toHaveBeenCalledWith({ displayName: "Ana", email: "ana@example.com", password: "a-long-password" });
  });

  it("keeps the form and shows field errors when the API refuses", async () => {
    register.mockResolvedValue({
      ok: false,
      error: { message: "Please check the highlighted fields.", fieldErrors: { password: "This password has appeared in a data breach." } },
    });
    render(<RegisterForm />);

    await fillAndSubmit();

    expect(await screen.findByText("This password has appeared in a data breach.")).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Check your inbox" })).not.toBeInTheDocument();
  });
});
