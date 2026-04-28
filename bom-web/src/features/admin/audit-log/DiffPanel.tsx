import { useMemo } from "react";

interface Props {
  before: string;
  after?: string | null;
}

type ChangeKind = "added" | "removed" | "changed" | "unchanged";

interface KeyChange {
  key: string;
  kind: ChangeKind;
  beforeValue?: unknown;
  afterValue?: unknown;
}

function tryParse(s: string | null | undefined): unknown {
  if (s === null || s === undefined) return undefined;
  try {
    return JSON.parse(s);
  } catch {
    return undefined;
  }
}

function format(v: unknown): string {
  if (v === undefined) return "(undefined)";
  if (v === null) return "null";
  if (typeof v === "string") return JSON.stringify(v);
  if (typeof v === "number" || typeof v === "boolean") return String(v);
  return JSON.stringify(v, null, 2);
}

function diffKeys(before: unknown, after: unknown): KeyChange[] {
  const isObj = (v: unknown): v is Record<string, unknown> =>
    v !== null && typeof v === "object" && !Array.isArray(v);

  if (!isObj(before) && !isObj(after)) {
    // Both primitives or arrays — single-value comparison
    if (after === undefined) {
      return [{ key: "(value)", kind: "removed", beforeValue: before }];
    }
    if (JSON.stringify(before) !== JSON.stringify(after)) {
      return [{ key: "(value)", kind: "changed", beforeValue: before, afterValue: after }];
    }
    return [{ key: "(value)", kind: "unchanged", beforeValue: before, afterValue: after }];
  }

  const beforeObj = isObj(before) ? before : {};
  const afterObj = isObj(after) ? after : {};
  const allKeys = Array.from(
    new Set([...Object.keys(beforeObj), ...Object.keys(afterObj)]),
  ).sort();

  return allKeys.map((key) => {
    const inBefore = key in beforeObj;
    const inAfter = key in afterObj;
    const bVal = beforeObj[key];
    const aVal = afterObj[key];

    if (inBefore && !inAfter) {
      return { key, kind: "removed", beforeValue: bVal };
    }
    if (!inBefore && inAfter) {
      return { key, kind: "added", afterValue: aVal };
    }
    if (JSON.stringify(bVal) !== JSON.stringify(aVal)) {
      return { key, kind: "changed", beforeValue: bVal, afterValue: aVal };
    }
    return { key, kind: "unchanged", beforeValue: bVal, afterValue: aVal };
  });
}

function ChangeRow({ change }: { change: KeyChange }) {
  const { key, kind, beforeValue, afterValue } = change;
  const colorClass = {
    added: "border-l-2 border-emerald-500/60 bg-emerald-500/5",
    removed: "border-l-2 border-red-500/60 bg-red-500/5",
    changed: "border-l-2 border-amber-500/60 bg-amber-500/5",
    unchanged: "border-l-2 border-border",
  }[kind];

  const marker = { added: "+", removed: "−", changed: "Δ", unchanged: " " }[kind];
  const markerColor = {
    added: "text-emerald-500",
    removed: "text-red-500",
    changed: "text-amber-500",
    unchanged: "text-muted-foreground",
  }[kind];

  return (
    <div className={`grid grid-cols-[1.5rem_8rem_1fr] gap-2 px-3 py-1.5 text-xs ${colorClass}`}>
      <span className={`font-mono font-bold ${markerColor}`}>{marker}</span>
      <span className="font-mono font-medium text-foreground truncate" title={key}>
        {key}
      </span>
      <div className="font-mono text-muted-foreground">
        {kind === "added" && (
          <span className="text-emerald-500">{format(afterValue)}</span>
        )}
        {kind === "removed" && (
          <span className="text-red-500 line-through">{format(beforeValue)}</span>
        )}
        {kind === "changed" && (
          <div className="space-y-0.5">
            <div className="text-red-500 line-through">{format(beforeValue)}</div>
            <div className="text-emerald-500">{format(afterValue)}</div>
          </div>
        )}
        {kind === "unchanged" && (
          <span className="opacity-60">{format(beforeValue)}</span>
        )}
      </div>
    </div>
  );
}

export function DiffPanel({ before, after }: Props) {
  const { changes, beforeRaw, afterRaw, parseFailed } = useMemo(() => {
    const b = tryParse(before);
    const a = tryParse(after);
    const parseFailed = b === undefined || (after !== null && after !== undefined && a === undefined);
    if (parseFailed) {
      return { changes: [], beforeRaw: before, afterRaw: after, parseFailed: true };
    }
    return {
      changes: diffKeys(b, a),
      beforeRaw: before,
      afterRaw: after,
      parseFailed: false,
    };
  }, [before, after]);

  if (parseFailed) {
    return (
      <div className="grid grid-cols-2 gap-2 text-xs">
        <div>
          <div className="mb-1 font-semibold text-foreground">Before</div>
          <pre className="max-h-64 overflow-auto rounded border border-red-500/40 bg-red-500/5 p-2 text-foreground">
            {beforeRaw}
          </pre>
        </div>
        <div>
          <div className="mb-1 font-semibold text-foreground">After</div>
          <pre className="max-h-64 overflow-auto rounded border border-emerald-500/40 bg-emerald-500/5 p-2 text-foreground">
            {afterRaw ?? "(deleted)"}
          </pre>
        </div>
      </div>
    );
  }

  const changedCount = changes.filter((c) => c.kind !== "unchanged").length;

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-xs text-muted-foreground">
        <span>
          {changedCount === 0
            ? "No field-level changes"
            : `${changedCount} of ${changes.length} field${changes.length === 1 ? "" : "s"} changed`}
        </span>
        {after === null && (
          <span className="rounded-full bg-red-500/10 px-2 py-0.5 text-red-500">
            Entity deleted
          </span>
        )}
      </div>
      <div className="max-h-96 overflow-auto rounded border border-border bg-background">
        {changes.length === 0 ? (
          <div className="p-3 text-xs text-muted-foreground">(empty)</div>
        ) : (
          changes.map((c) => <ChangeRow key={c.key} change={c} />)
        )}
      </div>
    </div>
  );
}
