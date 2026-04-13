import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/cn";

interface SearchableSelectProps<T> {
  options: T[];
  value: T | null;
  onChange: (v: T | null) => void;
  getLabel: (o: T) => string;
  getValue: (o: T) => string | number;
  placeholder?: string;
  disabled?: boolean;
  id?: string;
}

export function SearchableSelect<T>({
  options,
  value,
  onChange,
  getLabel,
  getValue,
  placeholder,
  disabled,
  id,
}: SearchableSelectProps<T>) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [highlight, setHighlight] = useState(0);
  const wrapperRef = useRef<HTMLDivElement>(null);

  const displayValue = open ? query : value ? getLabel(value) : "";

  const filtered = options.filter((o) =>
    getLabel(o).toLowerCase().includes(query.toLowerCase()),
  );

  useEffect(() => {
    if (!open) return;
    const onDocMouseDown = (e: MouseEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
        setOpen(false);
        setQuery("");
      }
    };
    document.addEventListener("mousedown", onDocMouseDown);
    return () => document.removeEventListener("mousedown", onDocMouseDown);
  }, [open]);

  function select(option: T) {
    onChange(option);
    setOpen(false);
    setQuery("");
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setHighlight((h) => Math.min(h + 1, filtered.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (e.key === "Enter" && filtered[highlight]) {
      e.preventDefault();
      select(filtered[highlight]);
    } else if (e.key === "Escape") {
      setOpen(false);
      setQuery("");
    }
  }

  return (
    <div ref={wrapperRef} className="relative">
      <input
        id={id}
        role="combobox"
        aria-expanded={open}
        disabled={disabled}
        value={displayValue}
        placeholder={placeholder}
        className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setQuery(e.target.value);
          setOpen(true);
          setHighlight(0);
        }}
        onKeyDown={onKeyDown}
      />
      {open && filtered.length > 0 && (
        <ul className="absolute z-20 mt-1 max-h-60 w-full overflow-auto rounded-md border border-border bg-background text-sm shadow-md">
          {filtered.map((o, i) => (
            <li
              key={getValue(o)}
              className={cn(
                "cursor-pointer px-3 py-2 hover:bg-muted",
                i === highlight && "bg-muted",
              )}
              onMouseDown={(e) => {
                e.preventDefault();
                select(o);
              }}
            >
              {getLabel(o)}
            </li>
          ))}
        </ul>
      )}
      {open && filtered.length === 0 && (
        <div className="absolute z-20 mt-1 w-full rounded-md border border-border bg-background px-3 py-2 text-sm text-muted-foreground shadow-md">
          No matches
        </div>
      )}
    </div>
  );
}
