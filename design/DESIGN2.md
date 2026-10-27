---
name: TravelVista
colors:
  surface: '#f8f9ff'
  surface-dim: '#cbdbf5'
  surface-bright: '#f8f9ff'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#eff4ff'
  surface-container: '#e5eeff'
  surface-container-high: '#dce9ff'
  surface-container-highest: '#d3e4fe'
  on-surface: '#0b1c30'
  on-surface-variant: '#45464d'
  inverse-surface: '#213145'
  inverse-on-surface: '#eaf1ff'
  outline: '#76777d'
  outline-variant: '#c6c6cd'
  surface-tint: '#565e74'
  primary: '#000000'
  on-primary: '#ffffff'
  primary-container: '#131b2e'
  on-primary-container: '#7c839b'
  inverse-primary: '#bec6e0'
  secondary: '#006a61'
  on-secondary: '#ffffff'
  secondary-container: '#86f2e4'
  on-secondary-container: '#006f66'
  tertiary: '#000000'
  on-tertiary: '#ffffff'
  tertiary-container: '#370e00'
  on-tertiary-container: '#e45405'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#dae2fd'
  primary-fixed-dim: '#bec6e0'
  on-primary-fixed: '#131b2e'
  on-primary-fixed-variant: '#3f465c'
  secondary-fixed: '#89f5e7'
  secondary-fixed-dim: '#6bd8cb'
  on-secondary-fixed: '#00201d'
  on-secondary-fixed-variant: '#005049'
  tertiary-fixed: '#ffdbce'
  tertiary-fixed-dim: '#ffb599'
  on-tertiary-fixed: '#370e00'
  on-tertiary-fixed-variant: '#7f2b00'
  background: '#f8f9ff'
  on-background: '#0b1c30'
  surface-variant: '#d3e4fe'
typography:
  headline-xl:
    fontFamily: Inter
    fontSize: 40px
    fontWeight: '700'
    lineHeight: 48px
    letterSpacing: -0.02em
  headline-xl-mobile:
    fontFamily: Inter
    fontSize: 28px
    fontWeight: '700'
    lineHeight: 36px
    letterSpacing: -0.01em
  headline-lg:
    fontFamily: Inter
    fontSize: 32px
    fontWeight: '600'
    lineHeight: 40px
    letterSpacing: -0.02em
  headline-lg-mobile:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-md:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-sm:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
    letterSpacing: -0.005em
  title-md:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '600'
    lineHeight: 26px
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
  body-sm:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
  label-md:
    fontFamily: Inter
    fontSize: 13px
    fontWeight: '600'
    lineHeight: 18px
    letterSpacing: 0.01em
  label-sm:
    fontFamily: Inter
    fontSize: 11px
    fontWeight: '600'
    lineHeight: 14px
    letterSpacing: 0.04em
  numeric-table:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 20px
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  gutter: 1.5rem
  gutter-sm: 1rem
  margin: 2rem
  margin-sm: 1rem
  space-xs: 0.25rem
  space-sm: 0.5rem
  space-md: 1rem
  space-lg: 1.5rem
  space-xl: 2.5rem
---

## Brand & Style

This design system embodies the clarity, reliability, and precision required for end-to-end itinerary comparison. The visual aesthetic is **Modern Architectural Editorial**: a marriage of crisp, airy whitespace, authoritative structural grid alignment, and micro-refined data density. 

The product balances two psychological states:
1. **Inspiration & Wanderlust:** Evoked through generous whitespace, luminous contrast, and fluid horizontal journey segments.
2. **Financial Precision & Confidence:** Grounded by institutional deep navies, structured comparative matrices, and deterministic status tokens.

The design language eliminates visual clutter, avoiding heavy decorative gradients or novelty skeuomorphism. Instead, it relies on soft layered ambient shadows, clear tonal surface transitions, and explicit visual hierarchy across multilingual scripts (Latin and Georgian). High-stakes travel alerts—such as unlinked self-transfers and budget thresholds—are communicated through high-contrast status chips that command attention without inducing panic.

## Colors

The color architecture is built to convey absolute financial clarity and operational trust:

- **Primary Canvas & Text (`#0F172A`, `#1E293B`):** Deep Slate and Midnight Navy serve as the structural anchor. These colors provide authoritative contrast for typography, timeline nodes, and core navigational framing, preventing the eye fatigue caused by harsh `#000000`.
- **Secondary / Positive Budget Accent (`#0D9488`, `#059669`):** A dual-tone oceanic emerald/cyan. Used for primary conversion triggers ("Lock Deal", "Customize Trip"), positive budget differentials ("$184 under budget"), and confirmed booking states.
- **Tertiary / Risk Warning (`#EA580C`, `#F97316`):** Vibrant warning amber-orange. Reserved strictly for structural warnings such as "Self-Transfer / Separate Tickets", tight airport connections (< 90m), and unbundled baggage fees.
- **Critical Alert (`#DC2626`):** Precision ruby-red, deployed exclusively for over-budget states, sold-out legs, and visa/entry restrictions.
- **Neutrals & Surfaces (`#F8FAFC`, `#F1F5F9`, `#E2E8F0`, `#64748B`):** Cool slate tints establish background layers, segmented card containers, subtle grid borders, and muted secondary metadata (flight numbers, aircraft models, cabin classes).

## Typography

The type scale is engineered for dense tabular comparisons and dual-script parity between English (Latin) and Georgian (Mkhedruli). 

- **Font Pairing & Font Stack:** In CSS implementation, fallback cascades must load `Inter, "Noto Sans Georgian", FiraGO, -apple-system, sans-serif` to guarantee optical x-height harmony, consistent vertical centering in chips, and balanced baseline alignment across languages.
- **Tabular Figures:** All numeric data—including prices, departure/arrival timestamps, layover durations, and distance calculations—must render with `font-variant-numeric: tabular-nums` to ensure exact column alignment across matrix views.
- **Uppercase & Tracking:** Reserve all-caps strictly for small labels (`label-sm`), airport IATA codes (e.g., `TBS`, `LHR`), and cabin tier indicators. Use positive letter-spacing (`+0.04em`) to maintain legibility at 11px and 12px.

## Layout & Spacing

The layout is built upon an 8pt base grid with an adaptive column strategy designed for side-by-side trip package breakdowns:

- **Desktop (1200px+):** A 12-column fluid grid within a 1440px max-width container, featuring 24px gutters (`1.5rem`) and 32px canvas margins (`2rem`). It accommodates a persistent 3-column comparative matrix: Left (Flight Path & Layover timeline), Middle (Accommodations & Transfers), Right (Unified Total vs. Target Budget).
- **Tablet (768px – 1199px):** An 8-column layout with 16px gutters (`gutter-sm`). Cards collapse into vertical stack tiers where flights sit atop hotel/transfer details, with the budget summary pinned to a sticky bottom dock.
- **Mobile (< 768px):** A 4-column layout with 16px canvas margins (`margin-sm`). Comparative itineraries reflow into horizontal swipe carousels with fixed-height route overview cards.
- **Rhythm & Whitespace:** Density inside data-heavy tables uses micro-spacers (`space-xs` and `space-sm`), whereas content grouping boundaries rely on generous gaps (`space-lg` to `space-xl`) to preserve the airy aesthetic.

## Elevation & Depth

Visual hierarchy uses a refined **Tonal Layer + Ambient Elevation** model, creating tactile floating surfaces without heavy borders:

- **Surface 0 (Base Canvas):** `#F8FAFC`. A soft slate background that recedes behind active cards.
- **Surface 1 (Card Default):** `#FFFFFF`, accompanied by an ultra-subtle border (`1px solid #E2E8F0`) and an ambient shadow: `0px 2px 8px -2px rgba(15, 23, 42, 0.04), 0px 1px 3px -1px rgba(15, 23, 42, 0.02)`.
- **Surface 2 (Hover / Active Itinerary):** Raised above neighboring packages using: `0px 12px 24px -4px rgba(15, 23, 42, 0.08), 0px 4px 8px -2px rgba(15, 23, 42, 0.03)`. The border transitions to `#CBD5E1`.
- **Surface 3 (Modals & Route Flyouts):** Elevated high-priority layers utilize: `0px 20px 32px -8px rgba(15, 23, 42, 0.12), 0px 8px 16px -4px rgba(15, 23, 42, 0.06)`.
- **Tinted Warning States:** Cards flagged with high-friction self-transfers take on a 1px border of `#FED7AA` and a subtle 2% tinted ambient warm glow to visually isolate the risk factor.

## Shapes

The design uses a balanced 16px (`1rem`) foundation for core UI components, delivering a soft, approachable frame that balances dense data tables:

- **Cards & Primary Modules:** 16px corner radius (`rounded-lg`), creating approachable yet structurally sound containers for complete trip packages.
- **Sub-Containers & Accordions:** 8px corner radius (`rounded-default`) for interior elements like flight legs, hotel amenity chips, and transfer segments.
- **Buttons & Form Fields:** 10px–12px radius, ensuring comfortable tap targets and optical continuity with surrounding cards.
- **Badge Chips & Pill Statuses:** Fully pill-shaped (`9999px`), distinguishing informational status indicators (such as baggage allowances or connection warnings) from actionable square buttons.

## Components

### Buttons
- **Primary Action ("Select Itinerary"):** Emerald base (`#0D9488`), 12px border radius, high-contrast white text, `label-md` weight. On hover, shifts to `#0F766E` with a micro-scale shift of 1.01.
- **Secondary / Action Navy:** Deep Slate (`#0F172A`) filled button for checkout and booking transitions.
- **Ghost / Outlined:** `#FFFFFF` background with `1px solid #CBD5E1` border and `#1E293B` text. Used for "Customize Legs" and "Filter Options".

### Badge Chips
- **Self-Transfer / Separate Tickets:** Amber container (`#FFF7ED`), amber-orange border (`#FDBA74`), bold burnt-orange text (`#C2410C`), accompanied by a transfer alert icon.
- **Budget Positive:** Mint-emerald tint (`#ECFDF5`), subtle border (`#A7F3D0`), dark emerald text (`#047857`).
- **Over-Budget Alert:** Crimson tint (`#FEF2F2`), border (`#FECACA`), deep red text (`#B91C1C`).

### Flight Route Timelines
- Horizontal step lines connecting origin, layover, and destination nodes. 
- Direct flights utilize a solid `#0D9488` connector line. 
- Layovers display an intermediate circular pip with layover time centered above. 
- Self-transfer layovers feature a dashed `#EA580C` connector line and an inline warning pill explicitly specifying luggage re-check requirements.

### Cards (Package & Trip Level)
- Structured with an interior 3-tier visual hierarchy:
  1. *Top Bar:* Total bundle price prominently displayed alongside budget variance tag.
  2. *Body:* Segmented travel items (Flight segment, Hotel accommodation rating and room type, Airport-to-hotel private/shuttle transfer).
  3. *Footer:* Booking flexibility tags, cancellation deadlines, and primary CTA.

### Dense Pricing & Comparison Tables
- Alternating subtle zebra striping using `#F8FAFC` and `#FFFFFF`.
- 1px divider lines using `#F1F5F9`.
- Numeric values aligned flush right with tabular figures enabled. Category headings aligned flush left in bold Slate (`#0F172A`).
- Sticky header row on desktop with subtle bottom drop shadow when scrolling through multi-trip options.

### Input Fields & Search Bars
- Background `#FFFFFF` with `#E2E8F0` border, 10px corner radius, and 16px internal padding.
- Focused state features a crisp 2px focus ring tinted in secondary brand emerald (`rgba(13, 148, 136, 0.25)`) with a `#0D9488` border.
- Integrated currency and budget slider controls featuring real-time interactive green-to-red delta track highlights.