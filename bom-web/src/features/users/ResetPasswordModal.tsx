import { useState } from "react";
import { useResetPassword } from "@/api/admin";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";

interface Props {
  user: { id: number; name: string };
  onClose: () => void;
}

export function ResetPasswordModal({ user, onClose }: Props) {
  const [reason, setReason] = useState("");
  const [tempPassword, setTempPassword] = useState<string | null>(null);
  const mutation = useResetPassword();
  const valid = reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid) return;
    try {
      const result = await mutation.mutateAsync({ id: user.id, reason: reason.trim() });
      setTempPassword(result.tempPassword);
    } catch {
      // surfaced via mutation.error
    }
  }

  function handleCopy() {
    if (tempPassword) navigator.clipboard.writeText(tempPassword).catch(() => {});
  }

  function handleClose() {
    setTempPassword(null);
    onClose();
  }

  if (tempPassword === null) {
    return (
      <Dialog open onClose={handleClose} title={`Reset password — ${user.name}`}>
        <label htmlFor="reset-reason" className="block">
          <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
          <textarea
            id="reset-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
            rows={3}
          />
        </label>
        {mutation.error ? (
          <p className="text-sm text-red-600 mt-2">
            Error:{" "}
            {String(
              (mutation.error as { response?: { data?: { error?: string } } }).response?.data
                ?.error ?? (mutation.error as Error).message,
            )}
          </p>
        ) : null}
        <div className="mt-4 flex justify-end gap-2">
          <Button variant="outline" onClick={handleClose}>
            Cancel
          </Button>
          <Button disabled={!valid || mutation.isPending} onClick={handleConfirm}>
            {mutation.isPending ? "Resetting..." : "Reset"}
          </Button>
        </div>
      </Dialog>
    );
  }

  return (
    <Dialog open onClose={handleClose} title="Temporary password">
      <p className="text-sm text-amber-700 mb-4">
        This password is shown <strong>once</strong>. Copy it now and hand it to the user. They
        will be required to change it on next login.
      </p>
      <div className="bg-muted p-3 rounded font-mono text-lg break-all">{tempPassword}</div>
      <div className="mt-4 flex justify-end gap-2">
        <Button variant="outline" onClick={handleCopy}>
          Copy
        </Button>
        <Button onClick={handleClose}>I've copied it</Button>
      </div>
    </Dialog>
  );
}
