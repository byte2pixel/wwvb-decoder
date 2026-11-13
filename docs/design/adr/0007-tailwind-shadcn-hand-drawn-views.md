# ADR-0007: Tailwind and shadcn/ui for controls, hand-drawn views, no chart library

**Date**: 2026-11-12
**Status**: accepted
**Deciders**: Mel Dommer

## Context

The visualizer has to end as a polished, professional interface shown on a projector. Until now the page was the protocol log from Step 8 with plain CSS, and the construction plan asked for "no new heavy dependencies".

Most of the screen is custom: the signal timeline, the pulse-width distribution, the 60-symbol frame, the decoded-frame table, and the clock. Standard controls are few, about ten: tabs, a select, a slider, a switch, buttons, tooltips, badges.

Constraints that shape the choice:

- The page runs only in WebView2, which is Chromium, and loads from a folder with no network on stage. Fonts and scripts must be bundled.
- The repository lints strictly, and the SonarQube Cloud gate asks for 80% coverage on new code.
- One developer writes it, and the talk is on Dec 10, 2026.
- A new tool has to be widely adopted and actively maintained.

## Decision

| Layer | Choice |
| --- | --- |
| Styling and tokens | Tailwind CSS v4 through its Vite plugin. Color, type, spacing, radius, and motion are theme variables defined once in CSS. Dark and light themes come from the same variables |
| Controls | shadcn/ui components copied into `ui/src/components/ui/`, on Base UI primitives. If shadcn does not offer its Base UI variants when the project is initialized, use Radix UI |
| Custom views | Drawn by hand: canvas for the scrolling signal view, DOM and SVG for the rest. No chart library. `d3-scale` and `d3-array` may be used for math (scales, ticks, bins). `d3-selection` is not used; React owns the DOM |
| State | Zustand holding pure reducers. Stream buffers stay outside React state, and the canvas reads them on each animation frame |
| Fonts | Inter for the interface and JetBrains Mono for data, bundled through the Fontsource packages |
| Icons | `lucide-react` |
| Motion | CSS transitions and the View Transitions API. The `motion` package is the approved fallback if those fall short |
| Tests | Vitest with jsdom, Testing Library, and user-event. Drawing logic is pure functions that return what to draw; the code that paints it is thin |
| Accessibility | The `jsx-a11y` plugin in oxlint |
| Development without the host | When `window.chrome.webview` is absent, the page plays a recorded message stream, so every state can be built and tested in a browser. No Storybook before the talk |

The canvas reads the same theme variables as the rest of the page, so a view cannot drift from the tokens.

The copied shadcn components are treated as vendored code: linted and formatted like everything else, excluded from coverage.

The screen layout is a separate decision and is not recorded here.

Adoption on 2026-11-12, from the GitHub API and the npm registry:

| Package | Latest release | Weekly downloads |
| --- | --- | --- |
| `tailwindcss` | 4.3.5, 2026-11-06 | 176 million |
| `shadcn` (CLI) | 4.23.0, 2026-10-27 | not measured; 128,000 GitHub stars |
| `@base-ui/react` | 1.9.0, 2026-10-09 | 29 million |
| `zustand` | 5.1.0, 2026-11-06 | 91 million |
| `d3-scale`, `d3-array` | 4.0.3 and 3.2.5, 2026-11-03 | 96 and 142 million |
| `lucide-react` | 1.72.0, 2026-11-11 | 132 million |
| `@fontsource-variable/inter` | 5.3.0, 2026-07-19 | 11 million |
| `@testing-library/react` | 16.3.3, 2026-08-27 | 92 million |

## Alternatives Considered

### Alternative 1: Mantine
- **Pros**: A complete component library with a dark theme, no component code in the repository, and the fastest start. Well maintained: 44 open issues, released 2026-11-09.
- **Cons**: A recognizable default look. It gives nothing for the custom views, which are most of the screen.
- **Why not**: The custom views need their own token system either way, and Tailwind supplies one that the controls then share.

### Alternative 2: Fluent UI, MUI, or Ant Design
- **Pros**: Very widely used, and they read as enterprise software out of the box.
- **Cons**: Each brings a strong house style and a styling runtime. Moving away from the default look costs more than building on unstyled primitives.
- **Why not**: The target is an instrument, not an admin dashboard.

### Alternative 3: A chart library (uPlot, Recharts, ECharts, visx)
- **Pros**: Axes, scales, and tooltips come ready made.
- **Cons**: Each has a default look that competes with the tokens. uPlot's last release was March 2025 and visx has had no commits since June 2026. Recharts draws SVG, which does not suit a strip chart at 60 frames per second.
- **Why not**: The views are simple shapes with custom annotation, so a library would be configured away more than used.

### Alternative 4: Storybook
- **Pros**: The standard workshop for building components in isolation.
- **Cons**: Another build, more configuration, and heavy development dependencies, four weeks before the talk.
- **Why not**: The recorded message stream covers the same need at a fraction of the setup cost. Worth revisiting after the talk.

## Consequences

### Positive
- One token file drives the controls, the custom views, and both themes.
- The interface does not look like a library's defaults.
- Stream data never passes through React rendering, so the signal view can hold 60 frames per second.
- The page can be developed and tested without the host running.

### Negative
- The copied components are repository code: they pass the linter and formatter, and need a coverage exclusion in both `vite.config.ts` and the Sonar settings.
- Tailwind is a new tool to learn.
- Hand-drawn views are more code than a chart library would be.
- shadcn components depend on `class-variance-authority`, which is below 1.0 and has had no release since November 2024.

### Risks
- shadcn's Base UI variants may not be available at init. Mitigation: use Radix UI, which the same components support.
- Paint code is hard to cover with tests and could fail the coverage gate. Mitigation: keep the paint layer thin and test the functions that decide what to draw.
- The bundle could pass the 500 kB gzipped limit in Step 12. Mitigation: the limit counts scripts and styles; fonts load from disk and are not counted.
