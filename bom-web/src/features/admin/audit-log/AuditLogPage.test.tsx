import { render, screen, fireEvent } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { AuditLogPage } from "./AuditLogPage";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";

vi.mock("@/api/admin", () => ({
  useAuditLog: () => ({
    data: {
      items: [
        {
          id: 1, adminUserId: 1, adminUserName: "Admin",
          actionType: "DeleteRequisition", entityType: "Requisition", entityId: 42,
          reason: "duplicate", beforeJson: "{\"id\":42}", afterJson: null,
          createdAt: "2026-04-26T18:00:00Z"
        }
      ],
      total: 1, page: 1, pageSize: 20
    },
    isLoading: false
  })
}));

function wrap(ui: React.ReactElement) {
  return <MemoryRouter><QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider></MemoryRouter>;
}

describe("AuditLogPage", () => {
  it("renders rows with timestamp + admin + action + entity + reason", () => {
    render(wrap(<AuditLogPage />));
    expect(screen.getByText(/Admin/)).toBeInTheDocument();
    expect(screen.getByText(/DeleteRequisition/)).toBeInTheDocument();
    expect(screen.getByText(/Requisition #42/)).toBeInTheDocument();
    expect(screen.getByText(/duplicate/)).toBeInTheDocument();
  });

  it("expand reveals diff panel with structured key-level diff", () => {
    render(wrap(<AuditLogPage />));
    fireEvent.click(screen.getAllByRole("button", { name: /diff/i })[0]);
    // After is null → "Entity deleted" badge + every key shown as removed
    expect(screen.getByText(/Entity deleted/i)).toBeInTheDocument();
    // The "id" key from the parsed BeforeJson `{"id":42}` is rendered as a row
    expect(screen.getByText("id")).toBeInTheDocument();
    expect(screen.getByText("42")).toBeInTheDocument();
  });

  it("renders filter controls", () => {
    render(wrap(<AuditLogPage />));
    expect(screen.getByText(/All actions/i)).toBeInTheDocument();
    expect(screen.getByText(/All entities/i)).toBeInTheDocument();
  });

  it("renders pagination footer", () => {
    render(wrap(<AuditLogPage />));
    expect(screen.getByRole("button", { name: /prev/i })).toBeDisabled();
    expect(screen.getByText(/page 1 of 1/i)).toBeInTheDocument();
  });
});
