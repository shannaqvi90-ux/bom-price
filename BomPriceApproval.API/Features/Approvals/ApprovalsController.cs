using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Domain.Workflow;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Approvals;

[ApiController]
[Route("api/approvals")]
[Authorize]
public class ApprovalsController(
    AppDbContext db,
    NotificationService notificationSvc,
    EmailService emailSvc,
    PdfService pdfSvc,
    ILogger<ApprovalsController> logger) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet("{requisitionId}")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> GetReview(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();

        var readyForReview = req.Items.All(ri => ri.BomHeader?.Cost is not null);

        var items = req.Items.OrderBy(ri => ri.SortOrder).Select(ri =>
        {
            var c = ri.BomHeader?.Cost;
            if (c is null)
                return new MdReviewItemDetail(ri.Id, ri.Item.Description, ri.ExpectedQty,
                    "NotStarted", null);

            var totalCost = ri.BomHeader!.TotalCostPerKg;
            var landedCost = totalCost > 0 ? totalCost - c.RawMaterialCostTotal - c.FohAmount : 0;

            var cost = new MdReviewItemCost(
                c.RawMaterialCostTotal, landedCost, c.FohAmount, totalCost,
                totalCost > 0 ? c.RawMaterialCostTotal / totalCost * 100 : 0,
                totalCost > 0 ? landedCost / totalCost * 100 : 0,
                totalCost > 0 ? c.FohAmount / totalCost * 100 : 0);

            return new MdReviewItemDetail(ri.Id, ri.Item.Description, ri.ExpectedQty,
                "Submitted", cost);
        }).ToList();

        return Ok(new MdReviewDetail(
            req.RefNo, req.Customer.Name,
            req.CurrencyCode, req.ExchangeRateSnapshot, readyForReview, items));
    }

    [HttpPost("{requisitionId}/approve")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> Approve(int requisitionId, ApproveRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.SalesPerson)
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return Validation
                .Detail("Requisition is not in MdReview status")
                .Field("Status", "Requisition is not in MdReview status.")
                .Return();

        if (request.Items is null || request.Items.Count == 0)
            return Validation
                .Detail("No items provided for approval.")
                .Field("Items", "No items provided for approval.")
                .Return();

        if (request.Items.Any(i => i.SalesPricePerKgAed <= 0))
        {
            var builder = Validation.Detail("SalesPrice must be greater than 0.");
            for (int i = 0; i < request.Items.Count; i++)
                if (request.Items[i].SalesPricePerKgAed <= 0)
                    builder.Field($"Items[{i}].SalesPricePerKgAed", "Must be greater than 0.");
            return builder.Return();
        }

        var inputIds = request.Items.Select(i => i.RequisitionItemId).ToList();
        if (inputIds.Count != inputIds.Distinct().Count())
            return Validation
                .Detail("Duplicate items in approval request.")
                .Field("Items", "Duplicate items in request.")
                .Return();

        var requisitionItemIds = req.Items.Select(i => i.Id).ToList();
        var missingInputs = requisitionItemIds.Except(inputIds).ToList();
        if (missingInputs.Count > 0)
            return Validation
                .Detail($"Missing price for item(s): {string.Join(", ", missingInputs)}")
                .Field("Items", "Missing price for one or more items.")
                .Return();

        var orphanInputSet = inputIds.Except(requisitionItemIds).ToHashSet();
        if (orphanInputSet.Count > 0)
        {
            var builder = Validation.Detail($"Unknown item(s) in request: {string.Join(", ", orphanInputSet)}");
            for (int i = 0; i < request.Items.Count; i++)
                if (orphanInputSet.Contains(request.Items[i].RequisitionItemId))
                    builder.Field($"Items[{i}].RequisitionItemId", "Unknown item.");
            return builder.Return();
        }

        if (req.Items.Any(i => i.BomHeader?.Cost is null))
            return Validation
                .Detail("All items must have a costed BOM before approval.")
                .Field("Items", "One or more items have no costed BOM.")
                .Return();

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = true
        };

        foreach (var input in request.Items)
        {
            var ri = req.Items.First(i => i.Id == input.RequisitionItemId);

            var totalCost = ri.BomHeader!.TotalCostPerKg;
            var profitMargin = (input.SalesPricePerKgAed - totalCost) / input.SalesPricePerKgAed * 100;
            var matPct = totalCost == 0 ? 0 : ri.BomHeader.Cost!.RawMaterialCostTotal / totalCost * 100;
            var otherPct = 100 - matPct;

            decimal? foreignPrice = null;
            if (req.CurrencyCode != "AED" && req.ExchangeRateSnapshot.HasValue)
                foreignPrice = input.SalesPricePerKgAed / req.ExchangeRateSnapshot.Value;

            approval.Items.Add(new ApprovalItem
            {
                RequisitionItemId = ri.Id,
                SalesPricePerKgAed = input.SalesPricePerKgAed,
                SalesPricePerKgForeign = foreignPrice,
                ProfitMarginPct = profitMargin,
                MaterialCostPct = matPct,
                OtherCostPct = otherPct
            });
        }

        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Approved;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] Quotation approved {RequisitionId} {RefNo} ApprovedByUserId={ApprovedByUserId} ItemCount={ItemCount}",
            req.Id, req.RefNo, CurrentUserId, approval.Items.Count);

        try
        {
            await db.Entry(approval).Collection(a => a.Items).LoadAsync();
            var pdf = pdfSvc.GenerateQuotation(req, approval);

            await notificationSvc.SendAsync(req.SalesPersonId,
                $"Quotation approved! Download ready: {req.RefNo}", req.Id, "QuotationRequest");

            // V23c P2 D6 broad: app never emails customer; email goes to SP
            // with customer contact info in the body so SP can forward manually.
            if (!string.IsNullOrWhiteSpace(req.SalesPerson.Email))
            {
                var customerContact = string.Join(" / ",
                    new[] { req.Customer.Email, req.Customer.PhoneNumber }
                        .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (string.IsNullOrEmpty(customerContact)) customerContact = "(no contact on file)";

                await emailSvc.SendAsync(req.SalesPerson.Email, req.SalesPerson.Name,
                    $"Quotation Approved – {req.RefNo}",
                    $"<p>Dear {req.SalesPerson.Name},</p>" +
                    $"<p>Your quotation <strong>{req.RefNo}</strong> has been approved. " +
                    $"Please forward the attached PDF to the customer.</p>" +
                    $"<p><strong>Customer:</strong> {req.Customer.Name}<br/>" +
                    $"<strong>Customer contact:</strong> {customerContact}</p>" +
                    $"<p>Regards,<br/>Fujairah Plastic Factory</p>",
                    pdf, $"{req.RefNo}-Quotation.pdf");
            }
            else
            {
                logger.LogWarning(
                    "[Approve] SP {SalesPersonId} has no email on record; skipping email dispatch for {RefNo}",
                    req.SalesPersonId, req.RefNo);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { message = "Approved", req.RefNo });
    }

    [HttpPost("{requisitionId}/reject")]
    [Authorize(Roles = "ManagingDirector")]
    public async Task<IActionResult> Reject(int requisitionId, RejectRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.SalesPerson)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return Validation
                .Detail("Requisition is not in MdReview status")
                .Field("Status", "Requisition is not in MdReview status.")
                .Return();

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = false
        };
        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Rejected;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogWarning("[Audit] Quotation rejected {RequisitionId} {RefNo} RejectedByUserId={RejectedByUserId}",
            req.Id, req.RefNo, CurrentUserId);

        try
        {
            await notificationSvc.SendAsync(req.SalesPersonId,
                $"Quotation rejected: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

            var accountantIds = await db.Users
                .Where(u => u.Role == UserRole.Accountant && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();
            await notificationSvc.SendToUsersAsync(
                accountantIds,
                $"Quotation rejected by MD: {req.RefNo}. Reason: {request.Notes}",
                req.Id,
                "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful commit for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { message = "Rejected" });
    }

    [HttpGet("{requisitionId}/pdf")]
    [Authorize(Roles = "ManagingDirector,SalesPerson,Accountant,Admin")]
    public async Task<IActionResult> DownloadPdf(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Items).ThenInclude(ri => ri.Item)
            .Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.SalesPerson)
            .Include(q => q.Items).ThenInclude(ri => ri.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Approvals).ThenInclude(a => a.Items)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();

        // SalesPerson can only download PDFs for their own requisitions.
        // Accountant, MD, Admin have null BranchId and see all branches.
        if (CurrentRole == "SalesPerson" && req.SalesPersonId != CurrentUserId)
            return Forbid();

        var currentApproval = req.Approvals.FirstOrDefault(a => !a.IsSuperseded);
        if (currentApproval is null || !currentApproval.IsApproved) return NotFound();

        var pdf = pdfSvc.GenerateQuotation(req, currentApproval);
        return File(pdf, "application/pdf", $"{req.RefNo}-Quotation.pdf");
    }

    // ─── V3 split approval endpoints ─────────────────────────────────────────
    // Stage 1: SetMargin (MdPricing → CustomerConfirm)
    // Stage 2A: AcceptCustomer (CustomerConfirm → MdFinalSign)
    //           RejectCustomer (CustomerConfirm → MdPricing — re-margin loop)
    // Stage 2B: FinalSign (MdFinalSign → Signed)

    [HttpPost("{requisitionId}/set-margin")]
    [Authorize(Roles = "ManagingDirector,Admin")]
    public async Task<IActionResult> SetMargin(int requisitionId, [FromBody] SetMarginRequest body)
    {
        if (body is null || body.Items is null || body.Items.Count == 0)
            return Validation.Detail("Items are required.")
                .Field("Items", "At least one margin entry is required.").Return();

        var req = await db.QuotationRequests
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == requisitionId);
        if (req is null) return NotFound();

        if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.CustomerConfirm))
            return Validation.Detail($"Cannot set margin from {req.Status}.")
                .Field("Status", $"Cannot set margin from {req.Status}.").Return();

        // D17 — every FG must have exactly one margin entry; no extras.
        var fgIds = req.Items.Select(ri => ri.Id).ToHashSet();
        var bodyIds = body.Items.Select(i => i.RequisitionItemId).ToHashSet();
        if (!fgIds.SetEquals(bodyIds))
            return Validation.Detail("Margin must be supplied for every FG, no extras.")
                .Field("Items", "Margin entries must match FG set exactly.").Return();

        if (body.Items.Count != bodyIds.Count)
            return Validation.Detail("Duplicate items in margin payload.")
                .Field("Items", "Duplicate RequisitionItemId.").Return();

        for (int i = 0; i < body.Items.Count; i++)
        {
            if (body.Items[i].MarginPerKg < 0)
                return Validation.Detail($"Margin must be >= 0 (item {body.Items[i].RequisitionItemId}).")
                    .Field($"Items[{i}].MarginPerKg", "Must be >= 0.").Return();
        }

        // D21 — sale-side FX snapshot at margin entry. AED requisitions skip this.
        decimal? saleRateSnapshot = null;
        if (req.CurrencyCode != "AED")
        {
            var rate = await db.ExchangeRates
                .Where(er => er.IsActive && er.CurrencyCode == req.CurrencyCode)
                .OrderByDescending(er => er.EffectiveDate)
                .Select(er => (decimal?)er.RateToAed)
                .FirstOrDefaultAsync();
            if (rate is null)
                return Validation.Detail($"No active FX rate for {req.CurrencyCode}.")
                    .Field("CurrencyCode", "No active exchange rate.").Return();
            saleRateSnapshot = rate;
        }
        // Cost-side mirror — per-line foreign FX handled at PDF generation (Task 31).
        var costRateSnapshot = saleRateSnapshot;

        // Re-margin loop: supersede any prior non-superseded InitialPricing approvals.
        var priorApprovals = await db.QuotationApprovals
            .Where(qa => qa.QuotationRequestId == req.Id
                      && qa.Stage == ApprovalStage.InitialPricing
                      && !qa.IsSuperseded)
            .ToListAsync();
        var nowUtc = DateTime.UtcNow;
        foreach (var prior in priorApprovals)
        {
            prior.IsSuperseded = true;
            prior.SupersededAt = nowUtc;
        }

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            ApprovedByUserId = CurrentUserId,
            ApprovedAt = nowUtc,
            Notes = body.Notes,
            IsApproved = false,
            Stage = ApprovalStage.InitialPricing,
            RateSnapshot = saleRateSnapshot,
            CostFxSnapshot = costRateSnapshot,
        };
        foreach (var item in body.Items)
        {
            approval.Items.Add(new ApprovalItem
            {
                RequisitionItemId = item.RequisitionItemId,
                MarginPerKg = item.MarginPerKg,
            });
        }
        db.QuotationApprovals.Add(approval);

        req.Status = RequisitionStatus.CustomerConfirm;
        req.UpdatedAt = nowUtc;
        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] V3 margin set {RequisitionId} {RefNo} ApprovedByUserId={ApprovedByUserId} ItemCount={ItemCount}",
            req.Id, req.RefNo, CurrentUserId, approval.Items.Count);

        try
        {
            await notificationSvc.SendAsync(req.SalesPersonId,
                $"{req.RefNo} priced — confirm with customer", req.Id, "QuotationRequest");

            var accountantIds = await db.UserBranches
                .Where(ub => ub.BranchId == req.BranchId
                          && ub.User.Role == UserRole.Accountant
                          && ub.User.IsActive)
                .Select(ub => ub.UserId)
                .ToListAsync();
            await notificationSvc.SendToUsersAsync(
                accountantIds,
                $"{req.RefNo} pricing complete",
                req.Id,
                "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful set-margin for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { id = req.Id, status = req.Status.ToString(), approvalId = approval.Id });
    }

    [HttpPost("{requisitionId}/accept-customer")]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> AcceptCustomer(int requisitionId, [FromBody] AcceptCustomerRequest body)
    {
        var req = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == requisitionId);
        if (req is null) return NotFound();

        if (CurrentRole == "SalesPerson" && req.SalesPersonId != CurrentUserId)
            return Forbid();

        if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.MdFinalSign))
            return Validation.Detail($"Cannot accept-customer from {req.Status}.")
                .Field("Status", $"Cannot accept-customer from {req.Status}.").Return();

        var nowUtc = DateTime.UtcNow;
        req.Status = RequisitionStatus.MdFinalSign;
        req.UpdatedAt = nowUtc;
        if (body is not null && !string.IsNullOrWhiteSpace(body.CustomerFeedback))
            req.Notes = (req.Notes ?? "") +
                $"\n[CustomerAccepted {nowUtc:yyyy-MM-dd}] {body.CustomerFeedback}";

        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] V3 customer accepted {RequisitionId} {RefNo} ByUserId={UserId}",
            req.Id, req.RefNo, CurrentUserId);

        try
        {
            var mdIds = await db.Users
                .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();
            var accountantIds = await db.UserBranches
                .Where(ub => ub.BranchId == req.BranchId
                          && ub.User.Role == UserRole.Accountant
                          && ub.User.IsActive)
                .Select(ub => ub.UserId)
                .ToListAsync();

            await notificationSvc.SendToUsersAsync(mdIds,
                $"{req.RefNo} customer accepted — apply final sign",
                req.Id, "QuotationRequest");
            await notificationSvc.SendToUsersAsync(accountantIds,
                $"{req.RefNo} customer accepted",
                req.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful accept-customer for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { id = req.Id, status = req.Status.ToString() });
    }

    [HttpPost("{requisitionId}/reject-customer")]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> RejectCustomer(int requisitionId, [FromBody] RejectCustomerRequest body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Trim().Length < 5)
            return Validation.Detail("Reason >= 5 chars required.")
                .Field("Reason", "At least 5 characters required.").Return();

        var req = await db.QuotationRequests.FirstOrDefaultAsync(r => r.Id == requisitionId);
        if (req is null) return NotFound();

        if (CurrentRole == "SalesPerson" && req.SalesPersonId != CurrentUserId)
            return Forbid();

        if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.MdPricing))
            return Validation.Detail($"Cannot reject-customer from {req.Status}.")
                .Field("Status", $"Cannot reject-customer from {req.Status}.").Return();

        var nowUtc = DateTime.UtcNow;
        // Supersede the active InitialPricing approval — MD must re-margin.
        var current = await db.QuotationApprovals
            .Where(qa => qa.QuotationRequestId == req.Id
                      && qa.Stage == ApprovalStage.InitialPricing
                      && !qa.IsSuperseded)
            .FirstOrDefaultAsync();
        if (current is not null)
        {
            current.IsSuperseded = true;
            current.SupersededAt = nowUtc;
        }

        req.Status = RequisitionStatus.MdPricing;
        req.Notes = (req.Notes ?? "") +
            $"\n[CustomerRejected {nowUtc:yyyy-MM-dd}] {body.Reason.Trim()}";
        req.UpdatedAt = nowUtc;

        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] V3 customer rejected {RequisitionId} {RefNo} ByUserId={UserId}",
            req.Id, req.RefNo, CurrentUserId);

        try
        {
            var mdIds = await db.Users
                .Where(u => u.Role == UserRole.ManagingDirector && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();
            var accountantIds = await db.UserBranches
                .Where(ub => ub.BranchId == req.BranchId
                          && ub.User.Role == UserRole.Accountant
                          && ub.User.IsActive)
                .Select(ub => ub.UserId)
                .ToListAsync();

            await notificationSvc.SendToUsersAsync(mdIds,
                $"{req.RefNo} customer rejected — re-price needed",
                req.Id, "QuotationRequest");
            await notificationSvc.SendToUsersAsync(accountantIds,
                $"{req.RefNo} customer rejected",
                req.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful reject-customer for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new { id = req.Id, status = req.Status.ToString() });
    }

    [HttpPost("{requisitionId}/final-sign")]
    [Authorize(Roles = "ManagingDirector,Admin")]
    public async Task<IActionResult> FinalSign(int requisitionId, [FromBody] FinalSignRequest body)
    {
        // D22 — type-to-confirm token must be exactly "SIGN" (case-sensitive).
        if (body is null || body.ConfirmationToken != "SIGN")
            return Validation.Detail("Type-to-confirm token must be 'SIGN'.")
                .Field("ConfirmationToken", "Must be exactly 'SIGN'.").Return();

        var req = await db.QuotationRequests
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == requisitionId);
        if (req is null) return NotFound();

        if (!RequisitionStateMachine.CanTransition(req.Status, RequisitionStatus.Signed))
            return Validation.Detail($"Cannot final-sign from {req.Status}.")
                .Field("Status", $"Cannot final-sign from {req.Status}.").Return();

        var current = await db.QuotationApprovals
            .Include(qa => qa.Items)
            .Where(qa => qa.QuotationRequestId == req.Id
                      && qa.Stage == ApprovalStage.InitialPricing
                      && !qa.IsSuperseded)
            .OrderByDescending(qa => qa.ApprovedAt)
            .FirstOrDefaultAsync();
        if (current is null)
            return Validation.Detail("No initial-pricing approval to sign.")
                .Field("Approval", "No active InitialPricing approval found.").Return();

        var nowUtc = DateTime.UtcNow;
        // Promote in place — preserves price + RateSnapshot history on the same row.
        current.Stage = ApprovalStage.FinalSign;
        current.IsApproved = true;
        current.ApprovedByUserId = CurrentUserId;
        current.ApprovedAt = nowUtc;
        if (!string.IsNullOrWhiteSpace(body.Notes))
            current.Notes = (current.Notes ?? "") + $"\n[FinalSign] {body.Notes}";

        req.Status = RequisitionStatus.Signed;
        req.UpdatedAt = nowUtc;
        await db.SaveChangesAsync();

        logger.LogInformation("[Audit] V3 final-sign {RequisitionId} {RefNo} ApprovalId={ApprovalId} SignedByUserId={UserId}",
            req.Id, req.RefNo, current.Id, CurrentUserId);

        // Generate signed PDF (stub — Task 31). PDF generation failure must not
        // roll back the state change, but can fail loudly since the stub is
        // intentionally unimplemented.
        try
        {
            var signer = await db.Users.FindAsync(CurrentUserId);
            if (signer is not null)
            {
                _ = await pdfSvc.GenerateSignedQuotationAsync(req, current, signer);
            }
        }
        catch (NotImplementedException)
        {
            // Expected during Phase A until Task 31 lands.
            logger.LogWarning("[FinalSign] GenerateSignedQuotationAsync stubbed — Task 31 pending for {RefNo}", req.RefNo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Signed-PDF generation failed for {RefNo}", req.RefNo);
        }

        // D23 — notify SP + branch accountants only; never the customer.
        try
        {
            var accountantIds = await db.UserBranches
                .Where(ub => ub.BranchId == req.BranchId
                          && ub.User.Role == UserRole.Accountant
                          && ub.User.IsActive)
                .Select(ub => ub.UserId)
                .ToListAsync();

            await notificationSvc.SendAsync(req.SalesPersonId,
                $"{req.RefNo} signed — quotation locked",
                req.Id, "QuotationRequest");
            await notificationSvc.SendToUsersAsync(accountantIds,
                $"{req.RefNo} signed",
                req.Id, "QuotationRequest");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification dispatch failed after successful final-sign for {Entity} {Id}",
                "QuotationRequest", req.Id);
        }

        return Ok(new
        {
            id = req.Id,
            status = req.Status.ToString(),
            approvalId = current.Id,
            pdfDownloadUrl = $"/api/approvals/{req.Id}/pdf"
        });
    }
}
