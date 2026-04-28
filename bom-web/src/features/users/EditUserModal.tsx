import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useUpdateUser } from "./usersApi";
import { useBranches } from "@/api/branches";
import { useUserBranches, useSetUserBranches } from "@/api/userBranches";
import { useGroups } from "@/api/groups";
import { useUserGroup, useSetUserGroup } from "@/api/userGroup";
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
  // Accountant multi-branch support
  const { data: existingBranchIds = [] } = useUserBranches(user?.id ?? 0, open && user?.role === "Accountant");
  const setUserBranches = useSetUserBranches(user?.id ?? 0);
  const [selectedBranchIds, setSelectedBranchIds] = useState<number[]>([]);
  // SalesPerson group support
  const { data: groups = [] } = useGroups();
  const { data: existingGroup } = useUserGroup(user?.id ?? 0, open && user?.role === "SalesPerson");
  const setUserGroup = useSetUserGroup(user?.id ?? 0);
  const [selectedGroupId, setSelectedGroupId] = useState<number | null>(null);

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

  // Sync selected branches when existing ones load or modal opens with an Accountant
  useEffect(() => {
    if (open && user?.role === "Accountant") {
      setSelectedBranchIds(existingBranchIds);
    }
  }, [open, user?.role, existingBranchIds]);

  // Sync selected group when existing one loads or modal opens with a SalesPerson
  useEffect(() => {
    if (open && user?.role === "SalesPerson") {
      setSelectedGroupId(existingGroup?.groupId ?? null);
    }
  }, [open, user?.role, existingGroup]);

  const role = watch("role") as UserRole | "";
  const branchRequired = role !== "" && BRANCH_SCOPED_ROLES.has(role as UserRole);
  const isAccountant = role === "Accountant";
  const isSalesPerson = role === "SalesPerson";

  function toggleBranch(id: number) {
    setSelectedBranchIds((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
    );
  }

  function handleClose() {
    update.reset();
    setUserBranches.reset();
    setUserGroup.reset();
    setSelectedBranchIds([]);
    setSelectedGroupId(null);
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
      // For Accountant: also persist the multi-branch selection
      if (isAccountant) {
        await setUserBranches.mutateAsync(selectedBranchIds);
      }
      // For SalesPerson: also persist the group assignment
      if (isSalesPerson) {
        await setUserGroup.mutateAsync(selectedGroupId);
      }
      update.reset();
      setUserBranches.reset();
      setUserGroup.reset();
      setSelectedBranchIds([]);
      setSelectedGroupId(null);
      onClose();
    } catch {
      // error displayed via update.isError or setUserBranches.isError
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

        {isSalesPerson && (
          <div className="space-y-1">
            <Label htmlFor="edit-user-group">Group</Label>
            <select
              id="edit-user-group"
              className="block w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
              value={selectedGroupId ?? ""}
              onChange={(e) => setSelectedGroupId(e.target.value ? Number(e.target.value) : null)}
            >
              <option value="">None</option>
              {groups.filter((g) => g.isActive).map((g) => (
                <option key={g.id} value={g.id}>
                  {g.name}
                </option>
              ))}
            </select>
            {setUserGroup.isError && (
              <p className="text-xs text-destructive">
                {(setUserGroup.error as { response?: { data?: { message?: string } } })
                  ?.response?.data?.message ?? "Failed to update group assignment"}
              </p>
            )}
          </div>
        )}

        {isAccountant && (
          <div className="space-y-1">
            <Label>Visible Branches</Label>
            <div className="rounded-md border border-input bg-background p-3 space-y-2 max-h-48 overflow-y-auto">
              {branches.length === 0 && (
                <p className="text-xs text-muted-foreground">No branches available.</p>
              )}
              {branches.map((b) => (
                <label key={b.id} className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-input"
                    checked={selectedBranchIds.includes(b.id)}
                    onChange={() => toggleBranch(b.id)}
                    aria-label={b.name}
                  />
                  <span className="text-sm">{b.name}</span>
                </label>
              ))}
            </div>
            {setUserBranches.isError && (
              <p className="text-xs text-destructive">
                {(setUserBranches.error as { response?: { data?: { message?: string } } })
                  ?.response?.data?.message ?? "Failed to update branch access"}
              </p>
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
