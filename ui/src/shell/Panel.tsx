import { useId, type ReactNode } from "react";

interface PanelProps {
  /** Shown in the header. It is also the region's accessible name. */
  title: string;
  children: ReactNode;
}

/** One titled surface of the screen. See "Panels" in docs/design/ui-design.md. */
export function Panel({ title, children }: PanelProps) {
  const titleId = useId();

  return (
    <section
      aria-labelledby={titleId}
      className="flex min-h-0 flex-col overflow-hidden rounded-lg border bg-card text-card-foreground"
    >
      <header className="flex h-8 flex-none items-center gap-2.5 border-b px-3">
        <h2 id={titleId} className="text-[0.8125rem] font-semibold">
          {title}
        </h2>
      </header>
      <div className="min-h-0 flex-1">{children}</div>
    </section>
  );
}
