import { Component, useId, type ReactNode } from "react";
import { cn } from "cn";
interface BoundaryState {
  error: Error | null;
}

/** Catches an error thrown while rendering its children, so one broken view does not take the screen down. */
class PanelBoundary extends Component<
  Readonly<{ children: ReactNode }>,
  BoundaryState
> {
  state: BoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): BoundaryState {
    return { error };
  }

  render() {
    if (this.state.error) {
      return (
        <p role="alert" className="p-3 text-xs text-bad">
          This view failed: {this.state.error.message}
        </p>
      );
    }
    return this.props.children;
  }
}

interface PanelProps {
  /** Shown in the header. It is also the region's accessible name. */
  title: string;
  /** The pipeline stages this panel shows, one chip each. */
  stages: readonly number[];
  /** One line of state at the right of the header. */
  state?: ReactNode;
  children: ReactNode;
  /** Extra classes for the panel element. */
  className?: string;
}

/** A panel's name in a view transition, from its title: `panel-pulse-width`. */
const transitionName = (title: string): string =>
  `panel-${title.toLowerCase().replaceAll(/[^a-z0-9]+/g, "-")}`;

/** One titled surface of the screen. See "Panels" in docs/design/ui-design.md. */
export function Panel({
  title,
  stages,
  state,
  children,
  className,
}: Readonly<PanelProps>) {
  const titleId = useId();

  return (
    <section
      aria-labelledby={titleId}
      // A panel that the overview and a focus view both show moves to its new place in the
      // view transition between them. The name is the title, which no two panels on a screen share.
      style={{ viewTransitionName: transitionName(title) }}
      className={cn(
        "flex min-h-0 flex-col overflow-hidden rounded-lg border bg-card text-card-foreground",
        className,
      )}
    >
      <header className="flex h-8 flex-none items-center gap-2.5 border-b px-3">
        <h2 id={titleId} className="text-[0.8125rem] font-semibold">
          {title}
        </h2>
        <ul aria-label="Stages" className="flex gap-1">
          {stages.map((stage) => (
            <li
              key={stage}
              className="rounded-sm bg-secondary px-1.5 font-mono text-[0.65625rem] leading-4 text-muted-foreground"
            >
              {stage}
            </li>
          ))}
        </ul>
        {state && (
          <p className="ml-auto truncate text-xs text-muted-foreground">
            {state}
          </p>
        )}
      </header>
      <div className="min-h-0 flex-1">
        <PanelBoundary>{children}</PanelBoundary>
      </div>
    </section>
  );
}
