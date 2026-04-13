import { motion } from "framer-motion";
import type { RequisitionStatus } from "@/types/api";
import { formatRelative } from "@/utils/date";
import { cn } from "@/lib/cn";

type StepState =
  | "pending"
  | "in-progress"
  | "completed"
  | "cancelled"
  | "approved"
  | "rejected";

interface Step {
  key: string;
  label: string;
  role: string;
  state: StepState;
  timestamp?: string;
}

function buildSteps(
  status: Exclude<RequisitionStatus, "Draft">,
  createdAt: string,
  updatedAt: string,
): Step[] {
  const rejected = status === "Rejected";
  const order: RequisitionStatus[] = [
    "Draft",
    "BomPending",
    "BomInProgress",
    "CostingPending",
    "CostingInProgress",
    "MdReview",
    "Approved",
  ];
  const idx = order.indexOf(status === "Rejected" ? "MdReview" : status);

  const stateFor = (from: number, to: number): StepState => {
    if (rejected) return "cancelled";
    if (idx > to) return "completed";
    if (idx >= from && idx <= to) return "in-progress";
    return "pending";
  };

  const submitted: Step = {
    key: "Submitted",
    label: "Submitted",
    role: "Sales Person",
    state: "completed",
    timestamp: formatRelative(createdAt),
  };

  const bom: Step = {
    key: "BOM",
    label: "BOM",
    role: "BOM Creator",
    state: stateFor(1, 2),
  };

  const costing: Step = {
    key: "Costing",
    label: "Costing",
    role: "Accountant",
    state: stateFor(3, 4),
  };

  const mdReview: Step = {
    key: "MD Review",
    label: "MD Review",
    role: "Managing Director",
    state: stateFor(5, 5),
  };

  let resultState: StepState = "pending";
  if (status === "Approved") resultState = "approved";
  else if (status === "Rejected") resultState = "rejected";

  const result: Step = {
    key: "Result",
    label: "Result",
    role: "",
    state: resultState,
  };

  const steps = [submitted, bom, costing, mdReview, result];
  const active = steps.find((s) => s.state === "in-progress");
  if (active) active.timestamp = formatRelative(updatedAt);

  return steps;
}

const CIRCLE_STYLES: Record<StepState, string> = {
  pending: "bg-muted border-border",
  "in-progress": "bg-amber-500/10 border-amber-500 ring-2 ring-amber-500/30",
  completed: "bg-primary border-primary",
  cancelled: "bg-muted border-border opacity-60",
  approved: "bg-emerald-500 border-emerald-500",
  rejected: "bg-rose-500 border-rose-500",
};

interface Props {
  status: Exclude<RequisitionStatus, "Draft">;
  createdAt: string;
  updatedAt: string;
}

export function RequisitionTimeline({ status, createdAt, updatedAt }: Props) {
  const steps = buildSteps(status, createdAt, updatedAt);

  return (
    <ol className="relative ml-3 border-l border-border pl-6">
      {steps.map((step, i) => (
        <motion.li
          key={step.key}
          data-testid={`step-${step.key}`}
          data-state={step.state}
          initial={{ opacity: 0, x: -8 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: i * 0.05 }}
          className="relative mb-6 last:mb-0"
        >
          <span
            className={cn(
              "absolute -left-[33px] mt-1 h-4 w-4 rounded-full border-2",
              CIRCLE_STYLES[step.state],
            )}
          />
          <div className="flex items-baseline justify-between gap-4">
            <div>
              <p className="text-sm font-medium">{step.label}</p>
              {step.role && <p className="text-xs text-muted-foreground">{step.role}</p>}
            </div>
            {step.timestamp && (
              <p className="text-xs text-muted-foreground">{step.timestamp}</p>
            )}
          </div>
        </motion.li>
      ))}
    </ol>
  );
}
