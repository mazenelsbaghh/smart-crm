---
name: Smart Customer Core Design System
description: A high-velocity, neon-drenched interface for instant AI-driven customer operations on WhatsApp.
colors:
  primary: "#00f3ff"
  secondary: "#ff007f"
  neutral-bg: "#0a0e17"
  neutral-surface: "#121824"
  neutral-border: "#1e293b"
  neutral-text: "#f8fafc"
  neutral-text-muted: "#94a3b8"
  success: "#10b981"
  warning: "#f59e0b"
  error: "#ef4444"
typography:
  display:
    fontFamily: "Outfit, Inter, sans-serif"
    fontSize: "clamp(2rem, 5vw, 3.5rem)"
    fontWeight: 800
    lineHeight: 1.1
    letterSpacing: "-0.02em"
  body:
    fontFamily: "Inter, sans-serif"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.6
    letterSpacing: "normal"
rounded:
  sm: "4px"
  md: "8px"
  lg: "16px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "16px"
  lg: "24px"
  xl: "32px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "#0a0e17"
    rounded: "{rounded.md}"
    padding: "10px 20px"
  button-primary-hover:
    backgroundColor: "#66f8ff"
  input-field:
    backgroundColor: "{colors.neutral-surface}"
    textColor: "{colors.neutral-text}"
    rounded: "{rounded.md}"
    padding: "12px 16px"
---

# Design System: Smart Customer Core

## 1. Overview

**Creative North Star: "Neon Midnight Command"**

Smart Customer Core is a high-velocity workspace designed for modern operations agents who manage high-frequency WhatsApp conversations. The UI rejects the dry, sterile light-mode look of traditional corporate CRMs in favor of a sleek, dark console that reduces eye strain, maintains high focus, and feels like a modern developer tool or high-end game client. 

The interface emphasizes maximum density, speed, and real-time animation. Pages do not feel static; instead, they pulse with live updates from the WhatsApp gateway, responsive typing indicators, and immediate AI recommendations. Interactive elements utilize keyboard shortcuts to allow agents to control the entire system without taking their hands off the keyboard.

**Key Characteristics:**
- **Neon Dark Aesthetic**: Tinted space-navy backgrounds paired with sharp, glowing cybernetic accents.
- **Shortcuts-First Interaction**: Visible keyboard shortcuts on every button, tab, and menu element.
- **Instant Response Feel**: Micro-animations and transitions keep the UI feeling fast and reactive.

## 2. Colors

The color palette uses a high-contrast dark mode. All background components are tinted with the brand's primary space-navy hue (hue 220 in OKLCH) to create depth.

### Primary
- **Vibrant Cyber Cyan** (#00f3ff): Used for primary action buttons, status highlights, cursor indicators, and key interface focus states.

### Secondary
- **Vibrant Hot Pink** (#ff007f): Reserved for notifications, high-priority notifications, manual agent assignments, and critical action buttons.

### Neutral
- **Midnight Space** (#0a0e17): The core canvas background. Restful for long support shifts.
- **Slate Slate Surface** (#121824): Used for cards, chat lists, messages, and input containers.
- **Dark Slate Border** (#1e293b): Used for dividing columns and separating structural containers.
- **Slate White** (#f8fafc): Primary text for readability.
- **Slate Muted** (#94a3b8): Used for dates, secondary labels, and keyboard shortcut indicators.

### Named Rules
**The 10% Accent Rule.** Vibrant Cyan and Hot Pink accent colors must collectively occupy less than 10% of the screen area. They are used purely for guidance, focus states, and indicators. The canvas must remain restful and clean.

**The Space Navy Tint Rule.** Never use pure black (#000000) or pure white (#ffffff). All dark surfaces are tinted with space navy to create depth and cohesion.

## 3. Typography

**Display Font:** Outfit (fallback: Inter, sans-serif)
**Body Font:** Inter (fallback: system-ui, sans-serif)
**Label/Mono Font:** JetBrains Mono (fallback: monospace)

The typography prioritizes clean, high-readability letterforms. JetBrains Mono is specifically used for keyboard shortcuts (`kbd` tags) and telemetry data.

### Hierarchy
- **Display** (Extra Bold, 32px-56px, 1.1 line-height): Used for landing sections, login titles, and empty-state heroes.
- **Headline** (Bold, 24px, 1.2 line-height): Used for main dashboard section headers and drawer titles.
- **Title** (Semi-Bold, 16px, 1.4 line-height): Used for card headers, conversation names, and modal titles.
- **Body** (Regular, 14px, 1.6 line-height): Used for chat messages, logs, customer info, and general descriptions. Max line length is restricted to 70ch.
- **Label** (Medium, 12px, tracking 0.05em, uppercase): Used for table headers, metadata badges, and small telemetry details.

### Named Rules
**The KBD-Visibility Rule.** Every shortcut must be rendered in a distinct, monospaced `kbd` block (e.g. `[K]`) using JetBrains Mono, styled with a subtle border and background to stand out clearly as a keyboard trigger.

## 4. Elevation

The design utilizes a flat, high-density layer strategy. Depth is communicated through color value steps (darker background, slightly lighter surfaces) and sharp neon borders, rather than soft ambient shadows.

### Shadow Vocabulary
- **Neon Glow** (`box-shadow: 0 0 15px rgba(0, 243, 255, 0.25)`): Used exclusively to highlight active inputs, selected chat cards, or open focus items.
- **Flat UI**: Rest surfaces are completely flat with a 1px border (#1e293b).

### Named Rules
**The Interactive Elevation Rule.** Surfaces never float or have shadows at rest. Shadow is a dynamic response to user hover or focus state, manifesting as a cybernetic glow.

## 5. Components

### Buttons
- **Shape:** Rounded corners with a 8px radius.
- **Primary:** cyber cyan (#00f3ff) background with midnight navy (#0a0e17) text. Bold, uppercase, with internal padding (10px 20px).
- **Secondary:** Transparent background with a 1px border (#1e293b) and hover state that fills with slate slate surface (#121824).
- **Shortcuts Indicator:** Every primary or secondary button includes a small right-aligned monospaced shortcut tip (e.g., `↵` or `⌘K`).

### Inputs / Fields
- **Shape:** Rounded corners (8px radius).
- **Style:** Dark slate surface (#121824) background with dark slate border (#1e293b). Text is slate white (#f8fafc).
- **Focus:** 1px cyber cyan (#00f3ff) border with a neon cyan glow.

### Cards / Containers
- **Corner Style:** Rounded corners (16px radius).
- **Background:** Slate slate surface (#121824).
- **Border:** 1px flat border (#1e293b).
- **Internal Padding:** Spaced dynamically using 16px (md) or 24px (lg).

### Keyboard Shortcut Tooltips (KBD)
- **Shape:** 4px border radius.
- **Style:** Charcoal black background, Slate Muted text, JetBrains Mono font, 1px border.

## 6. Do's and Don'ts

### Do:
- **Do** provide visual keyboard shortcut indicators (`kbd` tags) directly beside every interactive control.
- **Do** use cyber cyan (#00f3ff) for primary action states and focus indicators.
- **Do** enforce a strict dark mode using space navy tints for all background levels.
- **Do** respect user reduced motion preferences by setting animation transition times to `0s` when requested.

### Don't:
- **Don't** use standard side-stripe borders as indicators on list items or chat previews. Use full border transitions or background highlights.
- **Don't** implement slow, multi-stage overlays when inline commands or shortcuts could perform the operation immediately.
- **Don't** use neon colors as text colors on light backgrounds, ensuring all interactions meet WCAG 2.1 AA guidelines.
- **Don't** use generic black-and-white scales; maintain the space navy brand tint throughout the dashboard.
