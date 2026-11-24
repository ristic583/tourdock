import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { LoginForm } from "./login-form";

const login = vi.fn();
vi.mock("@/lib/auth/actions", () => ({ login: (...args: unknown[]) => login(...args) }));

describe("LoginForm", () => {
  beforeEach(() => login.mockReset());

  it("validates on the client before calling the server", async () => {
    render(<LoginForm />);
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Enter your email.")).toBeInTheDocument();
    expect(screen.getByText("Enter your password.")).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    expect(login).not.toHaveBeenCalled();
  });

  it("shows the API error and passes the return path to the action", async () => {
    login.mockResolvedValue({
      ok: false,
      error: { message: "Email or password is incorrect.", code: "Auth.InvalidCredentials" },
    });
    render(<LoginForm next="/bookings" />);

    await userEvent.type(screen.getByLabelText("Email"), "ana@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "a-long-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Email or password is incorrect.");
    expect(login).toHaveBeenCalledWith({ email: "ana@example.com", password: "a-long-password" }, "/bookings");
  });

  it("loads an OAuth consent page in full so it gets its own security policy", async () => {
    const assign = vi.fn();
    vi.stubGlobal("location", { ...window.location, assign });
    login.mockResolvedValue({ ok: true, data: { fullPageLoad: "/oauth/consent?request_uri=x" } });
    render(<LoginForm next="/oauth/consent?request_uri=x" />);

    await userEvent.type(screen.getByLabelText("Email"), "ana@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "a-long-password");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith("/oauth/consent?request_uri=x"));
    vi.unstubAllGlobals();
  });

  it("puts field errors from the API on the matching fields", async () => {
    login.mockResolvedValue({
      ok: false,
      error: { message: "Please check the highlighted fields.", fieldErrors: { email: "Unknown domain." } },
    });
    render(<LoginForm />);

    await userEvent.type(screen.getByLabelText("Email"), "ana@example.com");
    await userEvent.type(screen.getByLabelText("Password"), "x");
    await userEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Unknown domain.")).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toHaveFocus();
  });
});
