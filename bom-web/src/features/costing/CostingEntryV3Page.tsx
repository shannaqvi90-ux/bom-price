import { useMemo, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useV3Requisition } from "@/features/requisitions/requisitionsApi";
import {
  useSaveV3CostData,
  useSubmitV3Costing,
  type V3FgCostInput,
} from "@/features/costing/costingApi";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";
import type { V3Requisition } from "@/types/api";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "PKR", "INR", "CNY"];

interface BomLineCost {
  bomLineId: number;
  costPerKg: string; // string in input state, parsed on submit
  currencyCode: string;
  wastagePercent: string; // production wastage per RM line, persists to BomLine.WastagePct
}

interface FgCostState {
  requisitionItemId: number;
  rawMaterialCosts: BomLineCost[];
  printingCostPerKg: string;
  printingCostCurrency: string;
  fohPerKg: string;
  transportPerKg: string;
  commissionPerKg: string;
}

function parseNum(s: string): number {
  if (s === "" || s === "-") return 0;
  const n = parseFloat(s);
  return Number.isFinite(n) ? n : 0;
}

function initStateFromReq(req: V3Requisition): FgCostState[] {
  return req.finishedGoods.map((fg) => ({
    requisitionItemId: fg.id,
    rawMaterialCosts: (fg.bomLines ?? []).map((bl) => {
      const existing = fg.costs?.lines?.find((c) => c.bomLineId === bl.id);
      return {
        bomLineId: bl.id,
        costPerKg:
          existing?.purchaseValuePerKg !== undefined && existing?.purchaseValuePerKg !== null
            ? String(existing.purchaseValuePerKg)
            : "",
        currencyCode: existing?.purchaseCurrency ?? "AED",
        wastagePercent:
          existing?.wastagePercent !== undefined && existing?.wastagePercent !== null
            ? String(existing.wastagePercent)
            : "0",
      };
    }),
    printingCostPerKg:
      fg.costs?.printingCostPerKg !== undefined && fg.costs?.printingCostPerKg !== null
        ? String(fg.costs.printingCostPerKg)
        : "",
    printingCostCurrency: fg.costs?.printingCostCurrency ?? "AED",
    fohPerKg: fg.costs?.fohPerKg !== undefined ? String(fg.costs.fohPerKg) : "",
    transportPerKg: fg.costs?.transportPerKg !== undefined ? String(fg.costs.transportPerKg) : "",
    commissionPerKg:
      fg.costs?.commissionPerKg !== undefined ? String(fg.costs.commissionPerKg) : "",
  }));
}

export default function CostingEntryV3Page() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useV3Requisition(reqId);

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  if (req.status !== "Costing") {
    return (
      <div className="mx-auto max-w-3xl p-6">
        <p className="text-sm">
          This requisition is not in <strong>Costing</strong> state. Status:{" "}
          <V3StatusBadge status={req.status} />.
        </p>
        <button
          className="mt-4 rounded border px-3 py-1 text-sm"
          onClick={() => navigate(`/requisitions/${reqId}`)}
        >
          Back
        </button>
      </div>
    );
  }

  // Inner form remounts (via key={req.id}) when the user navigates between reqs,
  // so its useState lazy-initializer runs once per req. This avoids the
  // setState-in-useEffect lint rule by treating the loaded req as a stable prop.
  return <CostingForm req={req} reqId={reqId} navigate={navigate} key={req.id} />;
}

interface CostingFormProps {
  req: V3Requisition;
  reqId: number;
  navigate: ReturnType<typeof useNavigate>;
}

function CostingForm({ req, reqId, navigate }: CostingFormProps) {
  const saveCost = useSaveV3CostData();
  const submitCost = useSubmitV3Costing();
  const [state, setState] = useState<FgCostState[]>(() => initStateFromReq(req));

  const fgById = useMemo(
    () => Object.fromEntries(req.finishedGoods.map((fg) => [fg.id, fg] as const)),
    [req.finishedGoods],
  );

  const buildPayload = (): V3FgCostInput[] =>
    state.map((fg) => {
      const ref = fgById[fg.requisitionItemId];
      const hasPrinting = ref?.hasPrinting ?? false;
      return {
        requisitionItemId: fg.requisitionItemId,
        rawMaterialCosts: fg.rawMaterialCosts.map((rc) => ({
          bomLineId: rc.bomLineId,
          costPerKg: parseNum(rc.costPerKg),
          currencyCode: rc.currencyCode || "AED",
          wastagePercent: parseNum(rc.wastagePercent),
        })),
        printingCostPerKg: hasPrinting ? parseNum(fg.printingCostPerKg) : null,
        printingCostCurrency: hasPrinting ? fg.printingCostCurrency || "AED" : null,
        fohPerKg: parseNum(fg.fohPerKg),
        transportPerKg: parseNum(fg.transportPerKg),
        commissionPerKg: parseNum(fg.commissionPerKg),
      };
    });

  const onSave = async () => {
    try {
      await saveCost.mutateAsync({ requisitionId: reqId, payload: { finishedGoods: buildPayload() } });
      toast.success("Cost data saved");
    } catch (err: unknown) {
      const e = err as { response?: { data?: { detail?: string; errors?: Record<string, string[]> } } };
      const detail = e?.response?.data?.detail ?? "Save failed";
      toast.error(detail);
    }
  };

  const onSubmit = async () => {
    try {
      await saveCost.mutateAsync({ requisitionId: reqId, payload: { finishedGoods: buildPayload() } });
      await submitCost.mutateAsync({ requisitionId: reqId });
      toast.success("Costing submitted — MD will set margin");
      navigate(`/requisitions/${reqId}`);
    } catch (err: unknown) {
      const e = err as { response?: { data?: { detail?: string; error?: string } } };
      const msg = e?.response?.data?.detail ?? e?.response?.data?.error ?? "Submit failed";
      toast.error(msg);
    }
  };

  return (
    <div className="mx-auto max-w-5xl p-6">
      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-semibold text-gray-900">{req.refNo}</h1>
        <V3StatusBadge status={req.status} />
      </div>
      <p className="mt-1 text-sm text-gray-500">
        Customer: {req.customer.name} · Quote currency: {req.currencyCode}
      </p>
      <p className="mt-2 text-sm text-gray-600">
        Enter cost per KG for each raw material + per-FG cost components. RM costs may be in any
        currency; FOH / Transport / Commission are AED. Save persists data; Submit also forwards to
        MD for margin pricing.
      </p>

      {state.map((fg, fgIdx) => {
        const ref = fgById[fg.requisitionItemId];
        if (!ref) return null;
        return (
          <section key={fg.requisitionItemId} className="mt-8 rounded-md border border-gray-200 p-4">
            <header className="mb-3 flex items-baseline gap-3">
              <h2 className="text-lg font-semibold text-gray-900">{ref.item.description}</h2>
              <span className="text-xs text-gray-500">
                ({ref.item.code}, {ref.expectedQty.toLocaleString()} KG
                {ref.hasPrinting ? ", printed" : ""})
              </span>
            </header>

            <h3 className="text-sm font-medium text-gray-700">Raw materials</h3>
            <table className="mt-1 w-full text-sm">
              <thead className="bg-gray-50 text-left text-xs text-gray-600">
                <tr>
                  <th className="px-3 py-1.5">RM</th>
                  <th className="px-3 py-1.5 text-right">Qty/KG</th>
                  <th className="px-3 py-1.5">Micron</th>
                  <th className="px-3 py-1.5 text-right">Cost/KG</th>
                  <th className="px-3 py-1.5">Currency</th>
                  <th className="px-3 py-1.5 text-right">Wastage %</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {(ref.bomLines ?? []).map((bl, blIdx) => {
                  const rc = fg.rawMaterialCosts[blIdx];
                  if (!rc) return null;
                  return (
                    <tr key={bl.id}>
                      <td className="px-3 py-1.5">
                        {bl.item.description} <span className="text-xs text-gray-500">({bl.item.code})</span>
                      </td>
                      <td className="px-3 py-1.5 text-right">{bl.qtyPerKg}</td>
                      <td className="px-3 py-1.5">{bl.micron ?? "—"}</td>
                      <td className="px-3 py-1.5 text-right">
                        <Input
                          type="number"
                          step="0.0001"
                          value={rc.costPerKg}
                          onChange={(e) =>
                            setState((s) => {
                              const next = [...s];
                              next[fgIdx] = { ...next[fgIdx] };
                              next[fgIdx].rawMaterialCosts = [...next[fgIdx].rawMaterialCosts];
                              next[fgIdx].rawMaterialCosts[blIdx] = { ...rc, costPerKg: e.target.value };
                              return next;
                            })
                          }
                          className="h-8 w-28 px-2 py-1 text-right text-sm"
                        />
                      </td>
                      <td className="px-3 py-1.5">
                        <Select
                          value={rc.currencyCode}
                          onChange={(e) =>
                            setState((s) => {
                              const next = [...s];
                              next[fgIdx] = { ...next[fgIdx] };
                              next[fgIdx].rawMaterialCosts = [...next[fgIdx].rawMaterialCosts];
                              next[fgIdx].rawMaterialCosts[blIdx] = { ...rc, currencyCode: e.target.value };
                              return next;
                            })
                          }
                          className="h-8 w-auto px-2 py-1 text-sm"
                        >
                          {CURRENCIES.map((c) => (
                            <option key={c} value={c}>
                              {c}
                            </option>
                          ))}
                        </Select>
                      </td>
                      <td className="px-3 py-1.5 text-right">
                        <Input
                          type="number"
                          step="0.01"
                          min={0}
                          value={rc.wastagePercent}
                          onChange={(e) =>
                            setState((s) => {
                              const next = [...s];
                              next[fgIdx] = { ...next[fgIdx] };
                              next[fgIdx].rawMaterialCosts = [...next[fgIdx].rawMaterialCosts];
                              next[fgIdx].rawMaterialCosts[blIdx] = { ...rc, wastagePercent: e.target.value };
                              return next;
                            })
                          }
                          className="h-8 w-20 px-2 py-1 text-right text-sm"
                          aria-label={`wastage-${blIdx}`}
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>

            <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
              {ref.hasPrinting && (
                <>
                  <label className="block">
                    <span className="text-xs text-gray-600">Printing cost/KG</span>
                    <Input
                      type="number"
                      step="0.0001"
                      value={fg.printingCostPerKg}
                      onChange={(e) =>
                        setState((s) => {
                          const next = [...s];
                          next[fgIdx] = { ...next[fgIdx], printingCostPerKg: e.target.value };
                          return next;
                        })
                      }
                      className="mt-1 h-9 px-2 py-1 text-right text-sm"
                    />
                  </label>
                  <label className="block">
                    <span className="text-xs text-gray-600">Printing currency</span>
                    <Select
                      value={fg.printingCostCurrency}
                      onChange={(e) =>
                        setState((s) => {
                          const next = [...s];
                          next[fgIdx] = { ...next[fgIdx], printingCostCurrency: e.target.value };
                          return next;
                        })
                      }
                      className="mt-1 h-9 px-2 py-1 text-sm"
                    >
                      {CURRENCIES.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </Select>
                  </label>
                </>
              )}
              <label className="block">
                <span className="text-xs text-gray-600">FOH/KG (AED)</span>
                <Input
                  type="number"
                  step="0.0001"
                  value={fg.fohPerKg}
                  onChange={(e) =>
                    setState((s) => {
                      const next = [...s];
                      next[fgIdx] = { ...next[fgIdx], fohPerKg: e.target.value };
                      return next;
                    })
                  }
                  className="mt-1 h-9 px-2 py-1 text-right text-sm"
                />
              </label>
              <label className="block">
                <span className="text-xs text-gray-600">Transport/KG (AED)</span>
                <Input
                  type="number"
                  step="0.0001"
                  value={fg.transportPerKg}
                  onChange={(e) =>
                    setState((s) => {
                      const next = [...s];
                      next[fgIdx] = { ...next[fgIdx], transportPerKg: e.target.value };
                      return next;
                    })
                  }
                  className="mt-1 h-9 px-2 py-1 text-right text-sm"
                />
              </label>
              <label className="block">
                <span className="text-xs text-gray-600">Commission/KG (AED)</span>
                <Input
                  type="number"
                  step="0.0001"
                  value={fg.commissionPerKg}
                  onChange={(e) =>
                    setState((s) => {
                      const next = [...s];
                      next[fgIdx] = { ...next[fgIdx], commissionPerKg: e.target.value };
                      return next;
                    })
                  }
                  className="mt-1 h-9 px-2 py-1 text-right text-sm"
                />
              </label>
            </div>
          </section>
        );
      })}

      <div className="mt-6 flex justify-end gap-3">
        <button
          onClick={() => navigate(-1)}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          Cancel
        </button>
        <button
          onClick={onSave}
          disabled={saveCost.isPending}
          className="rounded-md border border-blue-600 bg-white px-4 py-2 text-sm font-medium text-blue-600 hover:bg-blue-50 disabled:opacity-50"
        >
          Save
        </button>
        <button
          onClick={onSubmit}
          disabled={saveCost.isPending || submitCost.isPending}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          Submit to MD
        </button>
      </div>
    </div>
  );
}
