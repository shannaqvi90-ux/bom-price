import type { RequisitionStatus } from "@/types/api";

export interface Filters {
  status: RequisitionStatus | "";
  from: string;
  to: string;
}

interface Props {
  value: Filters;
  onChange: (next: Filters) => void;
}

const STATUS_OPTIONS: Array<RequisitionStatus | ""> = [
  "",
  // V3 statuses (current workflow):
  "Draft",
  "Costing",
  "MdPricing",
  "CustomerConfirm",
  "MdFinalSign",
  "Signed",
  "Cancelled",
  "Rejected",
  // Legacy V2.3 statuses (retained for filtering historical reqs):
  "BomPending",
  "BomInProgress",
  "CostingPending",
  "CostingInProgress",
  "MdReview",
  "Approved",
];

export function RequisitionFilters({ value, onChange }: Props) {
  return (
    <div className="flex flex-wrap items-end gap-3 rounded-lg border border-border bg-card p-4">
      <div className="flex flex-col gap-1">
        <label htmlFor="filter-status" className="text-xs font-medium text-muted-foreground">
          Status
        </label>
        <select
          id="filter-status"
          className="h-9 rounded-md border border-input bg-background px-2 text-sm"
          value={value.status}
          onChange={(e) => onChange({ ...value, status: e.target.value as Filters["status"] })}
        >
          {STATUS_OPTIONS.map((s) => (
            <option key={s || "all"} value={s}>
              {s || "All"}
            </option>
          ))}
        </select>
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="filter-from" className="text-xs font-medium text-muted-foreground">
          From
        </label>
        <input
          id="filter-from"
          type="date"
          className="h-9 rounded-md border border-input bg-background px-2 text-sm"
          value={value.from}
          onChange={(e) => onChange({ ...value, from: e.target.value })}
        />
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="filter-to" className="text-xs font-medium text-muted-foreground">
          To
        </label>
        <input
          id="filter-to"
          type="date"
          className="h-9 rounded-md border border-input bg-background px-2 text-sm"
          value={value.to}
          onChange={(e) => onChange({ ...value, to: e.target.value })}
        />
      </div>

      <button
        type="button"
        className="h-9 rounded-md border border-input bg-background px-3 text-sm hover:bg-muted"
        onClick={() => onChange({ status: "", from: "", to: "" })}
      >
        Clear
      </button>
    </div>
  );
}
