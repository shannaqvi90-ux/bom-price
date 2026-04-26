import { useBranches } from "@/api/branches";

interface Props {
  id?: string;
  value: number | null;
  onChange: (branchId: number) => void;
  disabled?: boolean;
}

export function BranchPicker({ id, value, onChange, disabled }: Props) {
  const { data: branches, isPending } = useBranches();
  const active = (branches ?? []).filter((b) => b.isActive);

  return (
    <select
      id={id}
      className="border border-slate-300 rounded-md px-3 py-2 text-sm bg-white disabled:bg-slate-100"
      value={value ?? ""}
      disabled={disabled || isPending}
      onChange={(e) => {
        const v = Number(e.target.value);
        if (Number.isFinite(v) && v > 0) onChange(v);
      }}
    >
      <option value="" disabled>
        {isPending ? "Loading branches…" : "Select branch"}
      </option>
      {active.map((b) => (
        <option key={b.id} value={b.id}>
          {b.name}
        </option>
      ))}
    </select>
  );
}
