import type { VariantProps } from "class-variance-authority"
import { cva } from "class-variance-authority"

export { default as Button } from "./Button.vue"

export const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap border-[2px] text-sm font-bold uppercase tracking-[0.12em] ring-offset-background transition-[transform,background-color,border-color,color] duration-75 active:translate-x-px active:translate-y-px focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/20 focus-visible:ring-offset-0 disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:size-4 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default: "[border-color:var(--button-primary-border)] bg-primary text-primary-foreground shadow-[2px_2px_0_var(--button-primary-shadow)] hover:bg-[var(--button-primary-hover)]",
        destructive:
          "[border-color:var(--button-destructive-border)] bg-destructive text-primary-foreground shadow-[2px_2px_0_var(--button-destructive-shadow)] hover:bg-[var(--button-destructive-hover)]",
        outline:
          "[border-color:var(--button-outline-border)] bg-[var(--button-outline-surface)] text-foreground shadow-[2px_2px_0_var(--button-outline-shadow)] hover:bg-[var(--button-outline-hover)]",
        secondary:
          "[border-color:var(--button-outline-border)] bg-secondary text-secondary-foreground shadow-[2px_2px_0_var(--button-outline-shadow)] hover:bg-[var(--button-outline-hover)]",
        ghost: "border-transparent bg-transparent text-foreground hover:[border-color:var(--button-ghost-border)] hover:bg-[var(--button-ghost-hover)]",
        link: "border-transparent bg-transparent text-foreground underline-offset-4 hover:underline",
      },
      size: {
        "default": "h-10 px-4 py-2",
        "sm": "h-9 px-3",
        "lg": "h-11 px-8",
        "icon": "h-10 w-10",
        "icon-sm": "size-9",
        "icon-lg": "size-11",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
)

export type ButtonVariants = VariantProps<typeof buttonVariants>
