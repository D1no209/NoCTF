---
name: NoCTF
description: A competition control surface for live CTF, AWD, AWDP, and KoH events.
colors:
  background: "oklch(0.984 0.006 255)"
  foreground: "oklch(0.18 0.04 260)"
  surface-card: "oklch(1 0 0)"
  primary: "oklch(0.56 0.23 262)"
  primary-hover: "oklch(0.504 0.207 262)"
  secondary: "oklch(0.95 0.03 258)"
  muted: "oklch(0.955 0.017 252)"
  accent: "oklch(0.93 0.045 258)"
  border: "oklch(0.9 0.023 252)"
  destructive: "oklch(0.577 0.245 27.325)"
  sidebar: "oklch(0.16 0.055 260)"
  sidebar-accent: "oklch(0.25 0.08 262)"
  chart-orange: "oklch(0.646 0.222 41.116)"
  chart-cyan: "oklch(0.6 0.118 184.704)"
  chart-ink: "oklch(0.398 0.07 227.392)"
typography:
  display:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif"
    fontSize: "1.875rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "normal"
  headline:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif"
    fontSize: "1.5rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "normal"
  title:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "normal"
  body:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  label:
    fontFamily: "Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, sans-serif"
    fontSize: "0.75rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "0.05em"
rounded:
  sm: "0.5rem"
  md: "0.625rem"
  lg: "0.75rem"
  xl: "1rem"
spacing:
  xs: "0.25rem"
  sm: "0.5rem"
  md: "1rem"
  lg: "1.5rem"
  xl: "2rem"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.surface-card}"
    rounded: "{rounded.lg}"
    padding: "0.5rem 1rem"
    height: "2.5rem"
    typography: "{typography.body}"
  button-outline:
    backgroundColor: "{colors.surface-card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "0.5rem 1rem"
    height: "2.5rem"
    typography: "{typography.body}"
  input-default:
    backgroundColor: "{colors.surface-card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.lg}"
    padding: "0.5rem 0.75rem"
    height: "2.5rem"
    typography: "{typography.body}"
  badge-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.surface-card}"
    rounded: "9999px"
    padding: "0.125rem 0.5rem"
    typography: "{typography.label}"
  card-default:
    backgroundColor: "{colors.surface-card}"
    textColor: "{colors.foreground}"
    rounded: "{rounded.xl}"
    padding: "1.5rem"
---

# Design System: NoCTF

## 1. Overview

**Creative North Star: "The Competition Control Room"**

NoCTF should feel like a live event control surface: calm enough for organizers to trust, sharp enough for participants to feel the pace of attack-defense play. The system uses a light operational workspace for legibility, a deep sidebar shell for administrative control, and concentrated blue-violet energy for primary actions, active navigation, score state, and real-time signals.

The design rejects generic SaaS softness, overdone cyberpunk spectacle, and default CTFd list density. It is allowed to be dense, but never muddy. Every surface should make state, fairness, and system movement inspectable: competitions, scores, containers, logs, plugins, patches, and audit records all need a clear place in the visual hierarchy.

**Key Characteristics:**
- Light, cool-tinted work surfaces with deep control-shell contrast.
- Saturated blue-violet used sparingly for decisions, activity, and identity.
- Dense tables and dashboards, softened by precise spacing and rounded controls.
- Real-time states shown with labels, icons, and color, never color alone.
- Motion is fast feedback, not choreography.

## 2. Colors

The palette is a restrained product system with a charged blue-violet core and cool technical neutrals.

### Primary
- **Signal Violet** (`primary`): The NoCTF action color. Use it for primary buttons, active navigation, selected states, focus rings, and live control emphasis.
- **Pressed Signal Violet** (`primary-hover`): The hover and pressed state for primary action surfaces.

### Secondary
- **Cool Command Wash** (`secondary`): A quiet blue-tinted surface for secondary controls, status filters, and non-destructive grouping.
- **Operational Mist** (`accent`): Hover backgrounds, selected tabs, and soft interactive feedback.

### Tertiary
- **Round Orange** (`chart-orange`): Scoring contrast, round highlights, and time-sensitive chart roles.
- **Telemetry Cyan** (`chart-cyan`): Health, connectivity, and service telemetry chart roles.
- **Deep Metric Ink** (`chart-ink`): Low-frequency chart contrast where text-like authority is needed.

### Neutral
- **Control Canvas** (`background`): The main app background. It should stay cool, light, and low-noise.
- **Console Ink** (`foreground`): Primary text and dense interface labels.
- **Panel Surface** (`surface-card`): Cards, tables, dialogs, and inputs.
- **Quiet Divider** (`border`): Borders and separators.
- **Admin Shell** (`sidebar`): Dark administrative navigation and high-authority shell surfaces.
- **Admin Active Field** (`sidebar-accent`): Active and hover fields inside the admin shell.
- **Fault Red** (`destructive`): Delete, error, and destructive confirmation actions only.

### Named Rules

**The Signal Rarity Rule.** Signal Violet should mark action, selection, or live state. Do not spread it across decorative backgrounds.

**The No Neon Rule.** The interface may be energetic, but full neon cyberpunk palettes are forbidden because they weaken data reading.

## 3. Typography

**Display Font:** Inter with system sans fallbacks.
**Body Font:** Inter with system sans fallbacks.
**Label/Mono Font:** Inter for labels; use the system monospace stack only for scores, IDs, logs, flags, and code-like values.

**Character:** The typography is functional and technical, with weight doing most of the hierarchy work. It should feel like a serious operations product, not a campaign page.

### Hierarchy
- **Display** (700, `1.875rem`, `1.2`): Page-level auth titles and rare high-level headings.
- **Headline** (700, `1.5rem`, `1.25`): View headings, admin page titles, and major dashboard regions.
- **Title** (700, `1.25rem`, `1.25`): Competition cards, dialog titles, and panel headers.
- **Body** (400, `0.875rem`, `1.5`): Default UI copy, descriptions, table body text, and controls. Prose should stay under 75ch.
- **Label** (700, `0.75rem`, `0.05em`, uppercase when used as metadata): Section labels, field groups, and compact status captions.

### Named Rules

**The Data First Rule.** Use weight, spacing, and tabular numerals for hierarchy before increasing type size.

## 4. Elevation

NoCTF uses a hybrid depth model: borders define structure, tinted surfaces define grouping, and soft ambient shadows are reserved for panels, cards, dialogs, and hover lift. Shadows should feel like monitor glow on a clean control desk, not heavy material layers.

### Shadow Vocabulary
- **Panel Ambient** (`0 22px 80px rgb(15 23 42 / 0.08)`): Authentication panels, filter panels, and important grouped tool surfaces.
- **Card Ambient** (`0 16px 60px rgb(15 23 42 / 0.06)`): Default cards and repeated item containers.
- **Primary Glow** (`0 4px 12px rgb(37 99 235 / 0.25)`): Primary buttons and active navigation states.
- **Hover Lift** (`0 24px 70px rgb(37 99 235 / 0.14)`): Competition cards and other clickable surfaces when hovered.
- **Dialog Lift** (`0 30px 90px rgb(15 23 42 / 0.22)`): Blocking dialogs and sheets.

### Named Rules

**The Border Before Shadow Rule.** Use borders and tonal surfaces for default structure. Add stronger shadows only for hover, dialogs, or primary emphasis.

## 5. Components

### Buttons
- **Shape:** Gently rounded rectangles (`0.75rem` default, `0.625rem` for compact sizes).
- **Primary:** Signal Violet background, light foreground, semibold 14px type, `2.5rem` height, `0.5rem 1rem` padding, subtle primary glow.
- **Hover / Focus:** Hover deepens the primary color and increases glow. Focus uses a visible ring from the same primary family.
- **Secondary / Ghost / Tertiary:** Secondary controls use Cool Command Wash. Ghost buttons are transparent until hover. Link buttons are text-only Signal Violet.

### Chips
- **Style:** Rounded-full badges with compact padding, 12px type, and role-specific tonal fills.
- **State:** Active or important chips use Signal Violet. Mode and status chips can use secondary fills, but must include text labels.

### Cards / Containers
- **Corner Style:** Large rounded corners (`1rem`) on cards and panels.
- **Background:** Panel Surface at high opacity, often over the cool Control Canvas.
- **Shadow Strategy:** Card Ambient at rest, Hover Lift only for clickable cards.
- **Border:** Quiet Divider at rest; primary-tinted border on hover or active selection.
- **Internal Padding:** `1.5rem` for cards, `1.25rem` for compact filter panels, `2rem` for broad page rhythm.

### Inputs / Fields
- **Style:** White-tinted field, Quiet Divider border, `0.75rem` radius, `2.5rem` height, 14px text.
- **Focus:** Border shifts to Signal Violet with a soft `4px` primary ring.
- **Error / Disabled:** Fault Red for invalid state, reduced opacity for disabled controls, never rely on color without message text.

### Navigation
- **Style:** The app top bar is translucent white with blur and a thin divider. Admin navigation uses the dark Admin Shell with compact icon-text rows.
- **Active State:** Active top navigation becomes Signal Violet with light text and glow. Active sidebar rows use Admin Active Field with clear foreground contrast.
- **Mobile Treatment:** Top navigation collapses into a right sheet. Keep the same labels and ordering as desktop.

### Signature Component

**NoCTF Logo Mark:** A rounded blue-violet gradient tile with an angular inner mark. Use it as identity, not decoration. It belongs in navigation, auth, and empty or loading states where orientation matters.

## 6. Do's and Don'ts

### Do:
- **Do** make fairness visible with score state, audit state, and traceable action feedback.
- **Do** keep Admin screens denser, calmer, and more operational than participant screens.
- **Do** use Signal Violet for primary decisions, active state, and live competition signals.
- **Do** pair color with labels or icons for status, severity, connection state, and mode.
- **Do** use skeletons and stable dimensions for loading live dashboards and tables.
- **Do** preserve the cool-tinted light workspace and deep Admin Shell contrast.

### Don't:
- **Don't** make the interface feel like generic SaaS admin design: white cards everywhere, soft marketing polish, flat dashboards, and no sense of competition.
- **Don't** use overdone cyberpunk: neon black-purple surfaces, decorative glow, heavy visual effects, or anything that makes status and scores harder to read.
- **Don't** drift into the default CTFd feel: traditional list-heavy competition pages, weak hierarchy, and a lack of modern control-room presence.
- **Don't** hide operational truth. Health, logs, scoring, audit trails, round state, and container status must be straightforward to inspect.
- **Don't** use side-stripe colored borders, gradient text, decorative glassmorphism, or identical card grids.
- **Don't** use modals as the first answer when inline editing, sheets, or progressive disclosure would keep the workflow intact.
