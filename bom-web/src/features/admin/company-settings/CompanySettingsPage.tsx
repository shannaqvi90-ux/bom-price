import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Card, CardContent } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import {
  useCompanySettings,
  useUpdateCompanySettings,
  type UpdateCompanySettingsRequest,
} from "@/api/companySettings";

interface FormState {
  companyName: string;
  address: string;
  telephone: string;
  trn: string;
  email: string;
  website: string;
  quotationValidityDays: number;
  termsAndConditions: string;
  reason: string;
}

const EMPTY_REASON: Pick<FormState, "reason"> = { reason: "" };

function toForm(s: ReturnType<typeof useCompanySettings>["data"] | undefined): FormState {
  return {
    companyName: s?.companyName ?? "",
    address: s?.address ?? "",
    telephone: s?.telephone ?? "",
    trn: s?.trn ?? "",
    email: s?.email ?? "",
    website: s?.website ?? "",
    quotationValidityDays: s?.quotationValidityDays ?? 30,
    termsAndConditions: s?.termsAndConditions ?? "",
    ...EMPTY_REASON,
  };
}

export function CompanySettingsPage() {
  const { data, isLoading, error } = useCompanySettings();
  const update = useUpdateCompanySettings();

  const [form, setForm] = useState<FormState>(() => toForm(undefined));
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    if (data) setForm(toForm(data));
  }, [data]);

  const handleSave = async () => {
    setErrors({});
    const payload: UpdateCompanySettingsRequest = {
      companyName: form.companyName,
      address: form.address,
      telephone: form.telephone,
      trn: form.trn,
      email: form.email,
      website: form.website,
      quotationValidityDays: form.quotationValidityDays,
      termsAndConditions: form.termsAndConditions,
      reason: form.reason,
    };
    try {
      await update.mutateAsync(payload);
      toast.success("Company settings updated");
      setForm((f) => ({ ...f, reason: "" }));
    } catch (e: unknown) {
      const ax = e as { response?: { data?: { errors?: Record<string, string[]> } } };
      const fieldErrors = ax?.response?.data?.errors;
      if (fieldErrors) {
        const flat: Record<string, string> = {};
        for (const [k, v] of Object.entries(fieldErrors)) flat[k] = v[0] ?? "Invalid value";
        setErrors(flat);
        toast.error("Please fix the highlighted fields");
      } else {
        toast.error("Failed to save company settings");
      }
    }
  };

  const handleDiscard = () => {
    setForm(toForm(data));
    setErrors({});
  };

  if (isLoading) return <div className="p-6 text-muted-foreground">Loading…</div>;
  if (error || !data) return <div className="p-6 text-red-600">Failed to load company settings.</div>;

  return (
    <div className="p-6 max-w-4xl mx-auto">
      <h1 className="text-2xl font-bold text-foreground mb-1">Company Settings</h1>
      <p className="text-sm text-muted-foreground mb-6">
        These values appear on every quotation PDF. Changes take effect immediately.
      </p>

      <Card>
        <CardContent className="p-6 space-y-6">
          {/* Letterhead */}
          <section>
            <h2 className="text-xs font-bold text-blue-700 dark:text-blue-300 uppercase tracking-widest mb-3">
              Letterhead
            </h2>
            <div className="space-y-3">
              <Field label="Company Name (headline)" error={errors.CompanyName}>
                <input
                  type="text"
                  value={form.companyName}
                  onChange={(e) => setForm({ ...form, companyName: e.target.value })}
                  className="input"
                />
              </Field>
              <Field label="Address (single line)" error={errors.Address}>
                <input
                  type="text"
                  value={form.address}
                  onChange={(e) => setForm({ ...form, address: e.target.value })}
                  className="input"
                />
              </Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label="Telephone" error={errors.Telephone}>
                  <input
                    type="text"
                    value={form.telephone}
                    onChange={(e) => setForm({ ...form, telephone: e.target.value })}
                    className="input"
                  />
                </Field>
                <Field label="TRN" error={errors.Trn}>
                  <input
                    type="text"
                    value={form.trn}
                    onChange={(e) => setForm({ ...form, trn: e.target.value })}
                    className="input"
                  />
                </Field>
                <Field label="Email (company)" error={errors.Email}>
                  <input
                    type="email"
                    value={form.email}
                    onChange={(e) => setForm({ ...form, email: e.target.value })}
                    className="input"
                  />
                </Field>
                <Field label="Website" error={errors.Website}>
                  <input
                    type="text"
                    value={form.website}
                    onChange={(e) => setForm({ ...form, website: e.target.value })}
                    className="input"
                  />
                </Field>
              </div>
            </div>
          </section>

          {/* Quotation defaults */}
          <section>
            <h2 className="text-xs font-bold text-blue-700 dark:text-blue-300 uppercase tracking-widest mb-3">
              Quotation Defaults
            </h2>
            <Field
              label="Quotation Validity (days)"
              hint='"Valid Until" = approval date + this many days. Range 1–365.'
              error={errors.QuotationValidityDays}
            >
              <input
                type="number"
                min={1}
                max={365}
                value={form.quotationValidityDays}
                onChange={(e) =>
                  setForm({ ...form, quotationValidityDays: Number(e.target.value) || 0 })
                }
                className="input w-32 text-right"
              />
            </Field>
          </section>

          {/* Terms & conditions */}
          <section>
            <h2 className="text-xs font-bold text-blue-700 dark:text-blue-300 uppercase tracking-widest mb-3">
              Terms &amp; Conditions
            </h2>
            <Field
              label="One per line — numbering added automatically"
              hint="Each non-empty line becomes a numbered point. Blank lines ignored."
              error={errors.TermsAndConditions}
            >
              <textarea
                rows={8}
                value={form.termsAndConditions}
                onChange={(e) => setForm({ ...form, termsAndConditions: e.target.value })}
                className="input font-mono text-sm leading-relaxed"
              />
            </Field>
          </section>

          {/* Reason + actions */}
          <section className="border-t border-border pt-4">
            <Field
              label="Reason for change (audit log)"
              hint="Min 5 chars. Recorded in admin audit log."
              error={errors.Reason}
            >
              <input
                type="text"
                value={form.reason}
                onChange={(e) => setForm({ ...form, reason: e.target.value })}
                className="input"
                placeholder="e.g. Updated TRN per new license"
              />
            </Field>

            <div className="flex justify-end gap-2 mt-4">
              <Button variant="outline" onClick={handleDiscard} disabled={update.isPending}>
                Discard Changes
              </Button>
              <Button onClick={handleSave} disabled={update.isPending}>
                {update.isPending ? "Saving…" : "Save Changes"}
              </Button>
            </div>

            {data.updatedAt && (
              <p className="text-xs text-muted-foreground mt-3">
                Last updated {new Date(data.updatedAt).toLocaleString()}
                {data.updatedByName ? ` by ${data.updatedByName}` : ""}.
              </p>
            )}
          </section>
        </CardContent>
      </Card>
    </div>
  );
}

function Field({
  label,
  hint,
  error,
  children,
}: {
  label: string;
  hint?: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label className="block text-xs font-semibold text-foreground mb-1">{label}</label>
      {children}
      {hint && !error && <p className="text-xs text-muted-foreground italic mt-1">{hint}</p>}
      {error && <p className="text-xs text-red-600 dark:text-red-400 mt-1">{error}</p>}
    </div>
  );
}
