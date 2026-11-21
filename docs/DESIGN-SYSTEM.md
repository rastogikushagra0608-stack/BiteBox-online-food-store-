# BiteBox design system

The goal is a warm, appetising, trustworthy food-tech brand: closer to a real startup than to a template. One brand accent, used with intent.

## Brand
- **Name:** BiteBox. **Tagline:** *Every Bite, Anytime.* **Assistant:** BiteBot.
- **Logo:** a takeaway box with a bite taken out of the top-right corner (inline SVG in `layout/brand.ts`, favicon in `public/favicon.svg`).

## Colour tokens (`src/styles.scss`)

| Token | Value | Use |
|---|---|---|
| `--bb-primary` | `#E8572A` coral-saffron | Primary buttons, active states, chart series, accents |
| `--bb-primary-hover` | `#CF4720` | Hover |
| `--bb-primary-soft` | `#FDEBE3` | Tints, selected options, promo cards |
| `--bb-bg` | `#FFF8F1` cream | Page background |
| `--bb-surface` | `#FFFFFF` | Cards |
| `--bb-ink` / `--bb-ink-2` / `--bb-muted` | `#1F1B16` / `#4A423A` / `#7A7067` | Text hierarchy, dark buttons, admin sidebar |
| `--bb-veg` / `--bb-nonveg` | `#1E8E3E` / `#B3261E` | The Indian veg (green square + dot) and non-veg (red triangle) marks |
| success / warning / danger / info | green / amber / red / blue (+ soft variants) | Status badges only. Always shown with a text label. |
| `--bb-star` | `#F2A516` | Ratings |

The admin charts use a single hue (coral) for magnitude, with labels in text ink. The palette was checked with the dataviz validator: coral has ≥ 3:1 contrast against the surface.

## Typography
- **Display:** *Fraunces* (a soft serif with a food-magazine feel) for headings and the wordmark.
- **UI:** *Plus Jakarta Sans* (400–700) for body text, buttons and tables.
- Both load from Google Fonts with system fallbacks. Headings use `clamp()` for fluid sizing.

## Shape, spacing, depth
- 4 px spacing scale (`--s1` … `--s8`), max content width 1200 px.
- Radius: 8 (inputs), 14 (cards), 22 (product cards and panels), pills for buttons and chips.
- Shadows are soft and warm-tinted: `--bb-shadow-sm`, `--bb-shadow`, `--bb-shadow-lg`.

## Components
Buttons (primary, dark, ghost, danger, sm, block), chips, badges, cards, inputs and selects with a coral focus ring, tables, skeleton loaders, the product card (image, ribbons such as "Bestseller" or "Only 3 left", heart, veg mark, spice flames, rating, stepper), quantity stepper, empty states, error states, toasts, cart drawer and BiteBot panel.

## Patterns
- **Every async view has loading (skeleton), empty and error states.**
- Mobile: a collapsible nav, a full-height cart drawer, a single-column checkout, and an admin sidebar that becomes an off-canvas drawer.
- Motion is subtle (fade-up, image zoom on hover) and disabled under `prefers-reduced-motion`.
- Accessibility: skip link, visible focus rings, ARIA labels on icon buttons, `aria-live` on toasts and the stepper, colour never the only signal (badges have text, veg marks have titles).
