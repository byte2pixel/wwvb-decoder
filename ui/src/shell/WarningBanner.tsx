import { TriangleAlert, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useAppStore } from "@/state/store";

/**
 * The host's warning for the presenter, in a row of its own under the app bar. It draws
 * nothing while there is no warning, and while the host keeps sending the one that was closed.
 */
export function WarningBanner() {
  const warning = useAppStore((state) => state.sourceStatus?.warning ?? "");
  const closed = useAppStore((state) => state.closedWarning);
  const closeWarning = useAppStore((state) => state.closeWarning);

  if (warning === "" || warning === closed) return null;

  return (
    <output className="flex h-8 flex-none items-center gap-2 border-b bg-warn/15 px-3 text-sm text-warn">
      <TriangleAlert aria-hidden="true" className="size-4 flex-none" />
      <span className="sr-only">Warning:</span>
      <span className="truncate">{warning}</span>
      <Button
        className="ml-auto flex-none"
        variant="ghost"
        size="icon-xs"
        aria-label="Close the warning"
        onClick={() => closeWarning(warning)}
      >
        <X />
      </Button>
    </output>
  );
}
