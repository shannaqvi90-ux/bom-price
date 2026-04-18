import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useCreateUser } from "./usersApi";
import { useBranches } from "@/api/lookups";
import type { UserRole } from "@/types/api";

// Roles that are branch-scoped. Admin / Accountant / ManagingDirector are
// branch-less (null BranchId means "see all branches").
const BRANCH_SCOPED_ROLES = new Set<UserRole>(["SalesPerson", "BomCreator"]);

const schema = z
  .object({
    name: z.string().min(1, "Name is required"),
    email: z.string().min(1, "Email is required").email("Invalid email format"),
    password: z.string().min(8, "Password must be at least 8 characters"),
    role: z.string().min(1, "Role is required"),
    branchId: z.string(),
  })
  .refine(
    (v) =>
      !BRANCH_SCOPED_ROLES.has(v.role as UserRole) || v.branchId.length > 0,
    { path: ["branchId"], message: "Branch is required for this role." },
  );

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  onClose: () => void;
}

export function AddUserModal({ open, onClose }: Props) {
  const create = useCreateUser();
  const { data: branches = [] } = useBranches({ enabled: open });
  const {
    register,
    handleSubmit,
    reset,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", email: "", password: "", role: "", branchId: "" },
  });

  const role = watch("role") as UserRole | "";
  const branchRequired = role !== "" && BRANCH_SCOPED_ROLES.has(role as UserRole);

  const onSubmit = handleSubmit(async (values) => {
    try {
      await create.mutateAsync({
        name: values.name,
        email: values.email,
        password: values.password,
        role: values.role as UserRole,
        branchId: branchRequired && values.branchId ? Number(values.branchId) : null,
      });
      create.reset();
      reset();
      onClose();
    } catch {
      // error displayed via create.isError
    }
  });

  function handleClose() {
    create.reset();
    reset();
    onClose();
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Add User">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="user-name">Name</Label>
          <Input id="user-name" {...register("name")} />
          {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="user-email">Email</Label>
          <Input id="user-email" type="email" {...register("email")} />
          {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="user-password">Password</Label>
          <Input id="user-password" type="password" {...register("password")} />
          {errors.password && (
            <p className="text-xs text-destructive">{errors.password.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="user-role">Role</Label>
          <select
            id="user-role"
            className="block w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
            {...register("role")}
          >
            <option value="">Select role</option>
            <option value="Admin">Admin</option>
            <option value="SalesPerson">SalesPerson</option>
            <option value="BomCreator">BomCreator</option>
            <option value="Accountant">Accountant</option>
            <option value="ManagingDirector">ManagingDirector</option>
          </select>
          {errors.role && <p className="text-xs text-destructive">{errors.role.message}</p>}
        </div>

        {branchRequired && (
          <div className="space-y-1">
            <Label htmlFor="user-branch">Branch</Label>
            <select
              id="user-branch"
              className="block w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
              {...register("branchId")}
            >
              <option value="">Select branch</option>
              {branches.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </select>
            {errors.branchId && (
              <p className="text-xs text-destructive">{errors.branchId.message}</p>
            )}
          </div>
        )}

        {create.isError && (
          <p className="text-sm text-destructive">
            {(create.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to create user"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || create.isPending}>
            {create.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
