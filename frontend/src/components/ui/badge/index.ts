import type { VariantProps } from "class-variance-authority"
import { cva } from "class-variance-authority"

export { default as Badge } from "./Badge.vue"

export const badgeVariants = cva(
  "inline-flex items-center justify-center rounded-full border px-2 py-0.5 text-xs font-medium w-fit whitespace-nowrap shrink-0 [&>svg]:size-3 gap-1 [&>svg]:pointer-events-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive transition-[color,box-shadow] overflow-hidden",
  {
    variants: {
      variant: {
        default:
          "border-transparent bg-primary text-primary-foreground [a&]:hover:bg-primary/90",
        secondary:
          "border-transparent bg-secondary text-secondary-foreground [a&]:hover:bg-secondary/90",
        destructive:
         "border-transparent bg-danger text-danger-foreground [a&]:hover:bg-danger/90 focus-visible:ring-danger/20 dark:focus-visible:ring-danger/40",
        success:
          "border-success/30 bg-success-muted text-success [a&]:hover:bg-success-muted/80",
        warning:
          "border-warning/35 bg-warning-muted text-warning [a&]:hover:bg-warning-muted/80",
        info:
          "border-info/30 bg-info-muted text-info [a&]:hover:bg-info-muted/80",
        attack:
          "border-attack/35 bg-attack-muted text-attack [a&]:hover:bg-attack-muted/80",
        defense:
          "border-defense/35 bg-defense-muted text-defense [a&]:hover:bg-defense-muted/80",
        neutral:
          "border-status-neutral/25 bg-status-neutral-muted text-status-neutral [a&]:hover:bg-status-neutral-muted/80",
        outline:
          "text-foreground [a&]:hover:bg-accent [a&]:hover:text-accent-foreground",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
)
export type BadgeVariants = VariantProps<typeof badgeVariants>
