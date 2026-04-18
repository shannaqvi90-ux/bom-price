import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useUpdateUser } from "./usersApi";
import { useBranches } from "@/api/lookups";
import type { User, UserRole } from "@/types/api";

const BRANCH_SCOPED_ROLES = new Set<UserRole>(["SalesPerson", "BomCreator"]);

const schema = z
  .object({
    name: z.string().min(1, "Name is required"),
    email: z.string().min(1, "Email is required").email("Invalid email format"),
    role: z.string().min(1, "Role is required"),
    branchId: z.string(),
    isActive: z.boolean(),
  })
  .refine(
    (v) =>
      !BRANCH_SCOPED_ROLES.has(v.role as UserRole) || v.branchId.length > 0,
    { path: ["branchId"], message: "Branch is required for this role." },
  );

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  user: User | null;
  onClose: () => void;
}

export function EditUserModal({ open, user, onClose }: Props) {
  const update = useUpdateUser();
  const { data: branches = [] } = useBranches({ enabled: open });
  const {
    register,
    handleSubmit,
    reset,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
  });

  useEffect(() => {
    if (user) {
      reset({
        name: user.name,
        email: user.email,
        role: user.role,
        branchId: user.branchId !== null ? String(user.branchId) : "",
        isActive: user.isActive,
      });
    }
  }, [user, reset]);

  const role = watch("role") as UserRole | "";
  const branchRequired = role !== "" && BRANCH_SCOPED_ROLES.has(role as UserRole);

  function handleClose() {
    update.reset();
    onClose();
  }

  const onSubmit = handleSubmit(async (values) => {
    if (!user) return;
    try {
      await update.mutateAsync({
        id: user.id,
        data: {
          name: values.name,
          email: values.email,
          role: values.role as UserRole,
          branchId: branchRequired && values.branchId ? Number(values.branchId) : null,
          isActive: values.isActive,
        },
      });
      update.reset();
      onClose();
    } catch {
      // error displayed via update.isError
    }
  });

  return (
    <Dialog open={open} onClose={handleClose} title="Edit User">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="edit-user-name">Name</Label>
          <Input id="edit-user-name" {...register("name")} />
          {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-user-email">Email</Label>
          <Input id="edit-user-email" type="email" {...register("email")} />
          {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-user-role">Role</Label>
          <select
            id="edit-user-role"
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
            <Label htmlFor="edit-user-branch">Branch</Label>
            <select
              id="edit-user-branch"
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

        <div className="flex items-center gap-2">
          <input
            id="edit-user-active"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...register("isActive")}
          />
          <Label htmlFor="edit-user-active">Is Active</Label>
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to update user"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || update.isPending}>
            {update.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
