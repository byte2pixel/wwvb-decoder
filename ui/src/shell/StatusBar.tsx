import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { useAppStore } from "@/state/store";
import { ProtocolLog } from "./ProtocolLog";
import { Button } from "@/components/ui/button";
import type { SourceState } from "@/protocol/messages";
import { noData } from "./stages";

const stateColor: Record<SourceState, string> = {
  running: "text-good",
  stopped: "text-muted-foreground",
  error: "text-bad",
};

export function StatusBar() {
  const sourceStatus = useAppStore((state) => state.sourceStatus);
  const lastSeq = useAppStore((state) => state.lastSeq);
  const lostMessages = useAppStore((state) => state.lostMessages);

  return (
    <footer className="flex h-6 flex-none items-center gap-3 border-t bg-bar px-3 text-xs text-muted-foreground">
      {sourceStatus ? (
        <>
          <span className={stateColor[sourceStatus.state]}>
            {sourceStatus.state}
          </span>
          <span>{sourceStatus.name}</span>
          <span className="truncate">{sourceStatus.detail}</span>
        </>
      ) : (
        <div>no host</div>
      )}
      <span className="ml-auto font-mono">seq {lastSeq ?? noData}</span>
      {lostMessages > 0 && (
        <span className="font-mono text-warn">{lostMessages} lost</span>
      )}
      <Sheet>
        <SheetTrigger
          render={<Button variant="ghost" size="xs" className="h-5" />}
        >
          Log
        </SheetTrigger>
        <SheetContent side="bottom" className="max-h-[60vh]">
          <SheetHeader>
            <SheetTitle>Protocol log</SheetTitle>
          </SheetHeader>
          <ProtocolLog />
        </SheetContent>
      </Sheet>
    </footer>
  );
}
