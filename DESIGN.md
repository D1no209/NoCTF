---
name: NoCTF Developer Theme System
description: A developer-curated visual theme contract for a precise live competition platform.
colors:
  pixel-canvas: "#e4e4e4"
  pixel-ink: "#242424"
  pixel-card: "#eeeeee"
  pixel-popover: "#ededed"
  pixel-command: "#2f2f2f"
  pixel-command-foreground: "#f5f5f5"
  pixel-secondary: "#d6d6d6"
  pixel-muted: "#dddddd"
  pixel-muted-foreground: "#5a5a5a"
  pixel-accent: "#cdcdcd"
  pixel-fault: "#a13e34"
  pixel-border: "#8b8b8b"
  pixel-input: "#a2a2a2"
  pixel-focus: "#3a3a3a"
  pixel-sidebar: "#c9c9c9"
  pixel-sidebar-accent: "#bdbdbd"
  category-web: "#ff9e42"
  category-pwn: "#ff5e36"
  category-misc: "#c47aff"
  category-reverse: "#66ccff"
  category-mobile: "#f06eff"
  category-crypto: "#ffe14d"
  category-forensics: "#ffb86c"
  category-ai: "#4deaff"
  category-blockchain: "#ff7ec7"
  category-hardware: "#ff8c42"
  category-osint: "#ff9f7a"
  category-cloud: "#7ab8ff"
typography:
  pixel-display:
    fontFamily: "Fusion Pixel 10px, Courier New, Lucida Console, MS Gothic, ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace"
    fontSize: "3rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "normal"
  pixel-headline:
    fontFamily: "Fusion Pixel 10px, Courier New, Lucida Console, MS Gothic, ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace"
    fontSize: "1.5rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "normal"
  pixel-title:
    fontFamily: "Fusion Pixel 10px, Courier New, Lucida Console, MS Gothic, ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace"
    fontSize: "1.25rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "normal"
  pixel-body:
    fontFamily: "Fusion Pixel 10px, Courier New, Lucida Console, MS Gothic, ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace"
    fontSize: "0.9rem"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  pixel-label:
    fontFamily: "Fusion Pixel 10px, Courier New, Lucida Console, MS Gothic, ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace"
    fontSize: "0.9rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "0.12em"
rounded:
  square: "0px"
  pill: "9999px"
spacing:
  pixel-cell: "8px"
  grid-unit: "16px"
  xs: "0.25rem"
  sm: "0.5rem"
  md: "1rem"
  lg: "1.5rem"
  xl: "2rem"
components:
  pixel-button-primary:
    backgroundColor: "{colors.pixel-command}"
    textColor: "{colors.pixel-command-foreground}"
    typography: "{typography.pixel-label}"
    rounded: "{rounded.square}"
    padding: "0.5rem 1rem"
    height: "2.5rem"
  pixel-button-outline:
    backgroundColor: "{colors.pixel-canvas}"
    textColor: "{colors.pixel-ink}"
    typography: "{typography.pixel-label}"
    rounded: "{rounded.square}"
    padding: "0.5rem 1rem"
    height: "2.5rem"
  pixel-input:
    backgroundColor: "{colors.pixel-card}"
    textColor: "{colors.pixel-ink}"
    typography: "{typography.pixel-body}"
    rounded: "{rounded.square}"
    padding: "0.25rem 0.75rem"
    height: "2.5rem"
  pixel-badge:
    backgroundColor: "{colors.pixel-command}"
    textColor: "{colors.pixel-command-foreground}"
    typography: "{typography.pixel-label}"
    rounded: "{rounded.pill}"
    padding: "0.125rem 0.5rem"
  pixel-card-default:
    backgroundColor: "{colors.pixel-card}"
    textColor: "{colors.pixel-ink}"
    rounded: "{rounded.square}"
    padding: "1rem"
---

# Design System: NoCTF

## Overview

**Creative North Star: "The Curated Competition Control Room"**

NoCTF is one product with a developer-curated catalog of complete visual themes. Platform developers author, review, test, and ship every theme. Users may select a registered theme, but they never upload CSS, edit token values, inject fonts, or provide external visual resources. Theme choice changes presentation only. It must never alter layout, information hierarchy, permissions, status meaning, game-mode behavior, or API interaction.

The shared interface contract is calm, precise, trustworthy, sharp, energetic, and technical. Every theme must make live competition state and operational truth easy to inspect. Pixel Industrial is the current implemented default: a participant or operator reads dense state on a laptop in a brightly lit event hall, where monochrome contrast, hard edges, and tactile offset shadows remain legible through glare and pressure.

Future themes may use different palettes, fonts, radii, elevation, textures, and motion intensity, but they must preserve the same semantic CSS roles and component states. The root frontmatter records the current Pixel Industrial implementation. Additional themes require their own reviewed token set and theme-specific design document before registration.

**Key Characteristics:**

- Developer-curated themes selected from a closed registry.
- Stable semantic roles across every theme and game mode.
- Pixel Industrial as the current square, grayscale, tactile default.
- Dense but legible operational surfaces with visible fairness and system state.
- Fast state feedback, stable dimensions, and reduced-motion support.
- Category accents used for recognition, never as decorative page themes.

**The Theme Contract Rule.** Every registered theme implements the complete semantic token contract. A missing token, unreadable state, or component-specific fallback is a release blocker.

**The Closed Catalog Rule.** Users choose a theme identifier from the platform registry. Custom CSS, custom token payloads, external fonts, remote backgrounds, and arbitrary theme URLs are forbidden.

**The Presentation Only Rule.** A theme may change visual expression. It may not change workflow, component purpose, data density, status meaning, or competition logic.

## Colors

Pixel Industrial uses a restrained achromatic control palette with small, deliberate category signals. Every future theme may reinterpret the visual palette, but success, warning, fault, focus, selection, and category roles must remain distinguishable and stable.

### Primary

- **Graphite Command** (`pixel-command`, `#2f2f2f`): Primary actions, selected controls, and the strongest interface decisions.
- **Paper Signal** (`pixel-command-foreground`, `#f5f5f5`): Text and icons placed on Graphite Command.

### Secondary

- **Machine Wash** (`pixel-secondary`, `#d6d6d6`): Secondary controls and grouped inactive surfaces.
- **Interaction Zinc** (`pixel-accent`, `#cdcdcd`): Hover, active, and low-intensity selection feedback.
- **Quiet Alloy** (`pixel-muted`, `#dddddd`): Muted sections, disabled groupings, and low-priority backgrounds.

### Tertiary

- **Web Amber** (`category-web`, `#ff9e42`), **Pwn Vermilion** (`category-pwn`, `#ff5e36`), **Misc Violet** (`category-misc`, `#c47aff`), and **Reverse Cyan** (`category-reverse`, `#66ccff`) identify challenge categories.
- **Mobile Magenta** (`category-mobile`, `#f06eff`), **Crypto Yellow** (`category-crypto`, `#ffe14d`), **Forensics Apricot** (`category-forensics`, `#ffb86c`), and **AI Electric Cyan** (`category-ai`, `#4deaff`) extend that category vocabulary.
- **Blockchain Pink** (`category-blockchain`, `#ff7ec7`), **Hardware Orange** (`category-hardware`, `#ff8c42`), **OSINT Coral** (`category-osint`, `#ff9f7a`), and **Cloud Blue** (`category-cloud`, `#7ab8ff`) complete the shipped category set.

### Neutral

- **Industrial Canvas** (`pixel-canvas`, `#e4e4e4`): Main page background beneath the 16px technical grid.
- **Console Ink** (`pixel-ink`, `#242424`): Default foreground and dense operational text.
- **Equipment Face** (`pixel-card`, `#eeeeee`): Cards, inputs, tables, and control faces.
- **Raised Sheet** (`pixel-popover`, `#ededed`): Popovers, menus, and temporary surfaces.
- **Structural Steel** (`pixel-border`, `#8b8b8b`): Two-pixel borders and strong separators.
- **Input Rail** (`pixel-input`, `#a2a2a2`): Default form-control border.
- **Focus Graphite** (`pixel-focus`, `#3a3a3a`): Keyboard focus and high-confidence interactive emphasis.
- **Navigation Alloy** (`pixel-sidebar`, `#c9c9c9`): Sidebar and authoritative navigation surfaces.
- **Navigation Press** (`pixel-sidebar-accent`, `#bdbdbd`): Active and hovered navigation items.
- **Fault Brick** (`pixel-fault`, `#a13e34`): Errors and destructive actions only.

**The Semantic Stability Rule.** A theme can change a color value, but it cannot exchange semantic roles. Fault must remain fault, focus must remain focus, and category identity must remain recognizable.

**The Category Restraint Rule.** Category colors label challenges and related data. They never flood inactive pages, replace primary actions, or become a competition-mode skin.

**The No Neon Spectacle Rule.** Bright category colors are small signals. Neon black-purple surfaces, decorative glow, and saturated full-page effects are prohibited.

## Typography

**Display Font:** Fusion Pixel 10px with Courier New, Lucida Console, MS Gothic, and system monospace fallbacks.

**Body Font:** Fusion Pixel 10px with the same monospace fallback stack. Pixel Industrial keeps its mosaic character across headings, body copy, tables, forms, descriptions, and captions.

**Label/Mono Font:** Fusion Pixel 10px. Pixel Industrial is mono-forward by design.

**Character:** Pixel Industrial is direct, mechanical, and visibly constructed. Its 20px root size makes compact rem-based controls physically larger than typical browser defaults, which supports event-floor readability. Future themes may register a different developer-supplied font stack, but fonts must ship with the platform or use trusted local system families.

### Hierarchy

- **Display** (700, `3rem`, `1.25`): Authentication statements and rare identity-led headings.
- **Headline** (700, `1.5rem`, `1.25`): Page headings and major administrative workspaces.
- **Title** (700, `1.25rem`, `1.25`): Cards, dialogs, operational panels, and competition regions.
- **Body** (400, `0.9rem`, `1.5`): Default UI copy, tables, forms, and descriptions. Narrative copy stays within 65 to 75 characters per line.
- **Label** (700, `0.9rem`, `0.12em`, uppercase for pixel-styled actions): Buttons and prominent state markers. Compact badges may step down to the `0.8rem` pixel minimum.

**The Pixel Minimum Rule.** Pixel glyphs are never rendered below `0.8rem`. Compact roles gain space or truncate rather than switching fonts or shrinking the bitmap face further.

**The Theme Typography Rule.** A theme may change its developer-supplied family and character, but it must preserve the five hierarchy roles, readable CJK coverage, stable control dimensions, and code-safe fallbacks.

**The Operational Numeral Rule.** Scores, IDs, ports, timestamps, attempts, and live counters use tabular numeral behavior whenever the chosen theme font supports it.

## Elevation

Pixel Industrial uses structural elevation, not ambient blur. Two-pixel borders define equipment edges; solid offset shadows make controls feel pressed from a mechanical panel. The result is deliberately tactile and flat-faced. Other developer themes may use softer or flatter elevation, but every theme must distinguish page, surface, overlay, focus, and pressed states without decorative glass effects.

### Shadow Vocabulary

- **Button Detent** (`box-shadow: 2px 2px 0 #8c8c8c`): Primary control depth at rest.
- **Card Detent** (`box-shadow: 2px 2px 0 #bdbdbd`): Default cards and repeated item containers.
- **Panel Chassis** (`box-shadow: 4px 4px 0 #d4d4d4`): Layered operational panels.
- **Dark Panel Chassis** (`box-shadow: 6px 6px 0 #7d7d7d`): Dark Pixel Industrial panel variant used by game dashboards.
- **Floating Equipment** (`box-shadow: 6px 6px 0 #bdbdbd`): Menus and higher temporary surfaces.

**The Solid Offset Rule.** Pixel Industrial shadows have zero blur and small positive offsets. If a shadow looks soft, cinematic, or luminous, it does not belong to this theme.

**The State Before Decoration Rule.** Elevation communicates clickability, pressed state, stacking, or temporary focus. It never exists only to make a screen look expensive.

## Components

All themes render the same component tree and preserve the same variant names. Theme-specific CSS may adjust visual primitives, but it cannot create alternate business components or route-specific forks.

### Buttons

- **Shape:** Square mechanical controls (`0px`) with a two-pixel frame.
- **Primary:** Graphite Command face, Paper Signal text, bold uppercase label, `0.5rem 1rem` padding, and Button Detent shadow.
- **Hover / Focus:** Hover shifts to `#3a3a3a`. Keyboard focus uses a visible two-pixel ring. Active state moves one pixel right and down to imitate a physical press. State transitions complete in 75ms.
- **Secondary / Ghost / Tertiary:** Outline and secondary variants use light alloy faces with visible borders. Ghost actions gain a border and Interaction Zinc background only on hover. Link actions remain text-only and underlined on interaction.

### Chips

- **Style:** Status badges use a pixel-ellipse silhouette, compact `0.125rem 0.5rem` padding, and a readable text label.
- **State:** Selected and important badges use the primary semantic role. Secondary, destructive, and outline variants keep identical geometry. No state may rely on fill color alone.

### Cards / Containers

- **Corner Style:** Square equipment faces (`0px`).
- **Background:** Equipment Face over Industrial Canvas.
- **Shadow Strategy:** Card Detent at rest. Clickable cards may move by one or two pixels, but large floating lifts are foreign to Pixel Industrial.
- **Border:** Two-pixel Structural Steel frame.
- **Internal Padding:** `1rem` base rhythm with `1.5rem` for broad content regions. The card component itself provides vertical rhythm while consumers define horizontal density.
- **Decoration:** The canonical card may render a black eight-pixel corner raster. It is an identity marker, not a generic decoration for every nested surface.

### Inputs / Fields

- **Style:** Equipment Face background, two-pixel Input Rail border, square corners, `2.5rem` nominal height, and `0.25rem 0.75rem` internal padding.
- **Focus:** Border changes to the primary semantic role with a clear two-pixel focus ring.
- **Error / Disabled:** Fault Brick marks invalid fields with accompanying text. Disabled controls retain their shape, reduce opacity, and reject pointer interaction.

### Navigation

- **Style:** Public navigation uses a muted alloy bar, a two-pixel divider, pixel logo, and compact icon-label links. Admin navigation uses the sidebar semantic roles without introducing a separate theme.
- **Active State:** The current route receives a primary bottom rule and Interaction Zinc surface. Hover and focus remain visibly distinct.
- **Mobile Treatment:** Navigation collapses into the existing sheet/dialog system while preserving label order and authorization boundaries.

### Layered Panel

The stacked Panel is Pixel Industrial's signature container. A face layer sits above a ten-pixel offset base, producing a chassis-like silhouette with solid shadows. The dark panel variant is a component variant within Pixel Industrial, not a separate user theme.

### Theme Registration Contract

A developer theme is complete only when it defines every semantic CSS variable, all component states, a bundled or trusted font stack, a color-scheme declaration, localized name and description keys, a static preview asset, and an allowlisted registry identifier. User preference stores only that identifier. Unknown or retired identifiers fall back to Pixel Industrial before the application renders.

## Do's and Don'ts

### Do:

- **Do** keep the developer theme registry closed, typed, versioned, and shipped with the frontend build.
- **Do** store only an allowlisted theme identifier in user preference, then validate it again before application.
- **Do** apply the selected theme before Vue mounts so the first frame never flashes another theme.
- **Do** require every theme to pass contrast, keyboard focus, reduced-motion, CJK coverage, responsive, and critical-state checks.
- **Do** preserve component structure, permissions, data density, layout, and semantic status roles across themes.
- **Do** make fairness visible with score state, audit state, traceable action feedback, and stable live updates.
- **Do** keep administrator screens dense, calm, and operational in every theme.
- **Do** use labels and icons with color for severity, connection, mode, and delivery state.

### Don't:

- **Don't** expose theme editing, CSS uploads, token JSON, arbitrary font URLs, remote backgrounds, theme imports, or user-authored visual code.
- **Don't** allow themes to change API behavior, competition logic, score presentation meaning, permissions, workflow, or component purpose.
- **Don't** bind a user theme to CTF, AWD, AWDP, KoH, a route, or a plugin. Theme selection is a visual preference, not a game-mode branch.
- **Don't** make the interface feel like generic SaaS admin design: white cards everywhere, soft marketing polish, flat dashboards, and no sense of competition.
- **Don't** use overdone cyberpunk: neon black-purple surfaces, decorative glow, heavy visual effects, or anything that makes status and scores harder to read.
- **Don't** drift into the default CTFd feel: traditional list-heavy competition pages, weak hierarchy, and a lack of modern control-room presence.
- **Don't** hide operational truth. Health, logs, scoring, audit trails, round state, and container status must be straightforward to inspect.
- **Don't** use side-stripe colored borders, gradient text, decorative glassmorphism, hero-metric templates, or identical card grids.
- **Don't** use modals as the first answer when inline editing, sheets, or progressive disclosure can preserve workflow context.
