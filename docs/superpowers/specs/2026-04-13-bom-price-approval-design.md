# BOM & Price Approval System — Design Spec
**Date:** 2026-04-13
**Companies:** Fujairah Plastic Factory – Fujairah & Fujairah Plastic Factory – Al Ain

---

## 1. Overview

A multi-role workflow application that manages the full lifecycle of a sales quotation — from initial request through BOM creation, cost entry, and MD approval — before entry into the ERP system. Available as a web app and a native iOS/Android mobile app.

---

## 2. Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend API | ASP.NET Core 8 Web API |
| Web Frontend | React 19 + Vite + Framer Motion + TailwindCSS |
| Mobile App | React Native + Expo (iOS & Android) |
| Database | PostgreSQL |
| Real-time | SignalR |
| Email | MailKit / SMTP |
| PDF Generation | QuestPDF |
| Auth | JWT (access token + refresh token) |

### Ports (conflict-free)
| Service | Port |
|---------|------|
| .NET API (HTTP) | 7300 |
| .NET API (HTTPS) | 7301 |
| Vite frontend dev server | 5300 |
| PostgreSQL (new instance) | 5500 |

### Solution Structure
```
/BomPriceApproval
  /BomPriceApproval.API        ← ASP.NET Core 8 Web API
  /bom-web                     ← React 19 + Vite web frontend
  /bom-mobile                  ← Expo React Native mobile app
```

---

## 3. System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                        Clients                          │
│   React 19 + Vite + Framer Motion    Expo (iOS/Android) │
│          localhost:5300                Native App        │
└────────────────┬────────────────────────┬───────────────┘
                 │       HTTPS / JWT       │
         ┌───────▼────────────────────────▼──────┐
         │       ASP.NET Core 8 Web API           │
         │       localhost:7300 / 7301            │
         │  - REST endpoints                      │
         │  - SignalR hub (real-time notifications)│
         │  - JWT auth middleware                 │
         │  - PDF generation (QuestPDF)           │
         │  - Email dispatch (MailKit/SMTP)       │
         └──────────────────┬────────────────────┘
                            │
               ┌────────────▼────────────┐
               │   PostgreSQL :5500      │
               │   DB: bom_price_approval│
               └─────────────────────────┘
```

---

## 4. User Roles & Permissions

| Role | Branch Access | Capabilities |
|------|--------------|--------------|
| Admin | Both | Manage users, processes, item import (Excel/CSV), system settings |
| Sales Person | Own branch | Create quotation requests, create/select customers and items, view own requisitions + status, download approved quotation PDF |
| BOM Creator | Own branch | Receive BOM tasks, build BOM/KG with raw materials + wastage per manufacturing process |
| Accountant | Both branches | Enter raw material costs (from ERP), landed cost (% or value), FOH, manage exchange rates, submit to MD |
| Managing Director | Both branches | View all BOMs + full cost breakdown, enter sales price, see profit margin + cost percentages, approve or reject quotation |

### Auth Flow
- JWT access token (short-lived, 15 min) + refresh token (7 days, stored in HttpOnly cookie for web, Expo SecureStore for mobile)
- Role + branch embedded in JWT claims
- Branch isolation enforced server-side on every query

---

## 5. Database Design

### Core Tables

```sql
-- Branch
branches (id, name)

-- Users
users (id, name, email, password_hash, role, branch_id nullable, is_active, created_at)
-- branch_id is NULL for MD and Accountants (they access both branches)

-- Customers
customers (id, name, address, email, phone_number, branch_id, created_by_user_id, created_at)
-- Sales persons only see customers where created_by_user_id = their own id

-- Items
items (id, code, description, type[finished/raw], branch_id, is_active, created_at)

-- Manufacturing Processes (configurable via admin panel)
processes (id, name, display_order, is_active, created_at)

-- Exchange Rates (managed by Accountant)
exchange_rates (id, currency_code, currency_name, rate_to_aed, set_by_user_id, effective_date, is_active, created_at)

-- Quotation Requests
quotation_requests (
  id, ref_no[REQ-XXXX], branch_id,
  sales_person_id, customer_id, item_id,
  expected_qty, currency_code, exchange_rate_snapshot,
  status[enum], created_at, updated_at
)

-- Status Enum:
-- draft → bom_pending → bom_in_progress → costing_pending
-- → costing_in_progress → md_review → approved → rejected

-- BOM Headers
bom_headers (id, quotation_request_id, item_id, created_by_user_id, total_cost_per_kg, submitted_at)

-- BOM Lines (raw material per process)
bom_lines (id, bom_header_id, process_id, raw_material_item_id, qty_per_kg, wastage_pct)

-- BOM Costs (entered by Accountant)
bom_costs (
  id, bom_header_id,
  raw_material_cost_total,
  landed_cost_type[percentage/value], landed_cost_value,
  foh_amount,
  submitted_by_user_id, submitted_at
)

-- Quotation Approvals (MD)
quotation_approvals (
  id, quotation_request_id,
  sales_price_per_kg_aed, sales_price_per_kg_foreign,
  profit_margin_pct, material_cost_pct, other_cost_pct,
  approved_by_user_id, approved_at, notes
)

-- Notifications
notifications (id, user_id, message, reference_id, reference_type, is_read, created_at)
```

---

## 6. Workflow

### Step 1 — Sales Person
1. Selects or creates a customer (own customers only)
2. Creates a new quotation request (REQ-XXXX auto-generated)
3. Selects finished good item, or creates a new item
   - System warns if a similar description already exists and shows the match before allowing creation
4. Enters expected order quantity
5. Selects quotation currency (AED default, or any active foreign currency)
6. Submits → status: `bom_pending`
7. BOM Creator receives in-app notification + email

### Step 2 — BOM Creator
1. Opens request → status changes to `bom_in_progress`
2. For each applicable manufacturing process (Extrusion, Printing, Lamination, etc.):
   - Selects raw material items
   - Enters quantity per kg
   - Enters wastage percentage
3. Submits BOM → status: `costing_pending`
4. Accountant receives in-app notification + email

### Step 3 — Accountant
1. Opens request → status changes to `costing_in_progress`
2. Reviews BOM lines
3. Enters last purchase cost per raw material (sourced from ERP)
4. Enters landed cost: choose percentage or fixed AED value
5. Enters other FOH (Factory Overhead)
6. Submits → status: `md_review`
7. MD receives in-app notification + email

### Step 4 — Managing Director
1. Reviews full BOM with all cost components
2. Dashboard shows:
   - Total cost per kg (AED)
   - Material cost %
   - Landed cost %
   - FOH %
   - Cost breakdown chart
3. Enters sales price per kg (AED)
4. Live preview updates: profit margin %, price in foreign currency (if applicable)
5. Approves → status: `approved`
   - Sales person receives in-app notification + email with PDF quotation attached
6. Or Rejects (with notes) → status: `rejected`
   - Sales person + Accountant notified

### Step 5 — Status Tracker (All Users)
- Any user can open any requisition they have access to and see the live pipeline:
  ```
  [✓] Sales Request  [✓] BOM Creation  [⟳] Costing  [ ] MD Approval  [ ] Done
       Ranj (done)       Naeem (done)    Shan (active)
  ```
- Sales persons see only their own requisitions
- BOM Creators see all branch requisitions at BOM stage
- Accountants and MD see all requisitions across accessible branches

---

## 7. Web Frontend

### Pages
```
/login
/dashboard                        ← Role-specific home: pending tasks, stats
/requisitions                     ← List (role + visibility filtered)
/requisitions/new                 ← Sales: new request form
/requisitions/:id                 ← Detail view + live status tracker
/requisitions/:id/bom             ← BOM Creator: build BOM per process
/requisitions/:id/costing         ← Accountant: enter costs
/requisitions/:id/approval        ← MD: review + set price + approve
/customers                        ← Sales: own customers only
/items                            ← Browse items (role-filtered)
/exchange-rates                   ← Accountant: manage currency exchange rates
/admin/users                      ← Admin: manage users
/admin/processes                  ← Admin: manage manufacturing processes
/admin/items/import               ← Admin: Excel (.xlsx) and CSV item import (admin selects target branch)
/notifications                    ← Notification center (all roles)
```

### UI Design Principles
- Sidebar navigation with role-filtered menu items
- Framer Motion: page transitions, status pipeline animations, notification toasts, form interactions
- TailwindCSS for consistent, responsive styling
- Recharts for MD profit/cost breakdown visuals
- Mobile-responsive (all pages work on mobile browser)
- Clean, professional aesthetic — no clutter

---

## 8. Mobile App (Expo)

**Admin panel is web-only.** Mobile covers the full workflow.

### Screens
```
Login                    ← JWT auth, tokens in Expo SecureStore
Dashboard                ← Pending tasks + role-specific quick stats
Requisitions List        ← Role-filtered list
Requisition Detail       ← Live status tracker pipeline
  New Requisition        ← Sales: create request
  BOM Entry              ← BOM Creator: add materials + wastage per process
  Costing Entry          ← Accountant: costs + landed cost + FOH
  MD Approval            ← Review costs, enter price, approve/reject
Customers                ← Sales: own customers
Notifications            ← In-app notification center
```

### Mobile-Specific
- Push notifications via Expo Push Notifications (iOS + Android)
- Offline-aware: cached data displayed when offline, syncs on reconnect
- Navigation: Expo Router with bottom tabs + stack navigation
- SecureStore for JWT token storage

---

## 9. Notifications & Email

### Trigger Matrix
| Event | Notified Users |
|-------|---------------|
| Requisition submitted by sales | BOM Creator (same branch) |
| BOM submitted | Accountant (same branch) |
| Costing submitted | MD |
| MD approves | Sales person (who created request) |
| MD rejects | Sales person + Accountant |

### Channels
- **In-app (SignalR):** Real-time push to all connected clients. Bell icon with unread count. Notification center with timestamps.
- **Email (MailKit/SMTP):** HTML email with company branding. Approved quotation email includes PDF attachment.
- SMTP config in `appsettings.json` (configurable)

---

## 10. PDF Quotation

Generated server-side with QuestPDF.

### Layout
```
┌─────────────────────────────────────────────┐
│  FUJAIRAH PLASTIC FACTORY                   │
│  [Branch Name]                  [Date]      │
│  SALES QUOTATION                REQ-XXXX    │
├─────────────────────────────────────────────┤
│  Customer: [Name]    Address: [Address]     │
│  Phone:    [Phone]   Email:   [Email]       │
├─────────────────────────────────────────────┤
│  Item Description        Qty       Unit     │
│  [Finished Good Name]    [X] kg    kg       │
├─────────────────────────────────────────────┤
│  Unit Price (USD):        XX.XX / kg        │
│  Total Price (USD):       XX,XXX.XX         │
│  Exchange Rate: 1 USD = 3.67 AED            │
│  (as of DD/MM/YYYY)                         │
├─────────────────────────────────────────────┤
│  Valid for 30 days from date of issue.      │
│  Authorized by: Eng Khaled                  │
│  ________________________                   │
│  Signature                                  │
└─────────────────────────────────────────────┘
```

- Base currency is always AED internally
- If foreign currency selected, PDF shows foreign currency price + exchange rate note
- Downloadable by sales person from web and mobile

---

## 11. Multi-Currency

- Accountant maintains an exchange rate table (currency code, name, rate to AED, effective date)
- Sales person selects quotation currency at request creation (AED is default)
- All costing workflow operates in AED
- At approval, AED sales price is converted using the exchange rate active on approval date (snapshot stored)
- MD approval screen shows price in both AED and selected foreign currency
- PDF displays foreign currency price + rate footnote

---

## 12. Key Business Rules

1. Sales person cannot see other sales persons' requisitions or customers
2. BOM Creators and Sales Persons are branch-locked; Accountants and MD access both branches
3. Item duplicate warning: system checks similar descriptions before allowing new item creation
4. Admin is the only role that can import items (Excel/CSV)
5. Exchange rates are managed exclusively by Accountants
6. Costing always stays in AED; foreign currency applies only to the final quotation output
7. Exchange rate is snapshotted at approval time — rate changes after approval do not affect the quotation
8. Admin panel (user management, process management, item import) is web-only; mobile covers workflow only
