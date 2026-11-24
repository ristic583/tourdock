import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { Slot } from "radix-ui";

import { Spinner } from "@/components/ui/spinner";
import { cn } from "@/lib/utils";

// Variants follow the design's buttons: primary teal, secondary outline, urgent (cancel) and text link.
// Every size keeps the 44px minimum touch target except `sm`, which is for dense toolbars.
const buttonVariants = cva(
  "inline-flex shrink-0 cursor-pointer items-center justify-center gap-2 rounded-control border border-transparent font-semibold whitespace-nowrap transition-colors outline-none disabled:pointer-events-none disabled:opacity-50 data-pending:opacity-80 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground hover:bg-primary-hover",
        outline: "border-line bg-surface text-ink hover:border-muted-foreground",
        ghost: "text-ink hover:bg-surface-2",
        urgent: "bg-urgent-soft text-urgent-ink hover:brightness-95",
        link: "text-primary hover:text-primary-hover",
      },
      size: {
        default: "min-h-11 px-[18px] text-sm",
        sm: "min-h-9 px-3 text-sm",
        lg: "min-h-[60px] rounded-2xl px-7 text-base",
        icon: "size-11",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

function Button({
  className,
  variant = "default",
  size = "default",
  asChild = false,
  pending = false,
  disabled,
  children,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean;
    /** Work started by this button is running: shows a spinner, disables it and marks it busy. */
    pending?: boolean;
  }) {
  const Comp = asChild ? Slot.Root : "button";

  return (
    <Comp
      data-slot="button"
      data-variant={variant}
      data-size={size}
      data-pending={pending || undefined}
      aria-busy={pending || undefined}
      disabled={disabled || pending}
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    >
      {asChild ? children : (
        <>
          {pending && <Spinner />}
          {children}
        </>
      )}
    </Comp>
  );
}

export { Button, buttonVariants };
