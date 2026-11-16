import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import { useAppStore } from "@/state/store";

/** The "Keyboard" table of docs/design/ui-design.md. The map itself is `keyAction`. */
const SHORTCUTS = [
  { keys: "1 to 7", does: "Focus that stage" },
  { keys: "Esc or 0", does: "Back to the overview" },
  { keys: "← → Page Up Page Down", does: "Previous and next stage" },
  { keys: "T", does: "Switch theme" },
  { keys: "L", does: "Protocol log" },
  { keys: "?", does: "This list" },
];

/** The shortcut list, in a drawer that `?` and the app bar's button open. */
export function ShortcutList() {
  const open = useAppStore((state) => state.drawer === "shortcuts");
  const setDrawer = useAppStore((state) => state.setDrawer);

  return (
    // The sheet has no trigger of its own, so it only ever asks to be closed.
    <Sheet open={open} onOpenChange={() => setDrawer(null)}>
      <SheetContent side="right">
        <SheetHeader>
          <SheetTitle>Keyboard shortcuts</SheetTitle>
        </SheetHeader>
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 px-4 pb-4 text-sm">
          {SHORTCUTS.map(({ keys, does }) => (
            <div key={keys} className="contents">
              <dt className="font-mono text-foreground-soft">{keys}</dt>
              <dd>{does}</dd>
            </div>
          ))}
        </dl>
      </SheetContent>
    </Sheet>
  );
}
