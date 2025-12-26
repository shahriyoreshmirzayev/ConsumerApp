using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConsumerApp;

public class ApprovalsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly FeedbackProducerService _feedbackService;
    private readonly ILogger<ApprovalsController> _logger;

    public ApprovalsController(ApplicationDbContext context, FeedbackProducerService feedbackService, ILogger<ApprovalsController> logger)
    {
        _context = context;
        _feedbackService = feedbackService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string filter = "pending")
    {
        ViewData["CurrentFilter"] = filter;

        var totalCount = await _context.ProductApprovals.CountAsync();
        var pendingCount = await _context.ProductApprovals.CountAsync(p => p.Status == "Pending");
        var approvedCount = await _context.ProductApprovals.CountAsync(p => p.Status == "Approved");
        var rejectedCount = await _context.ProductApprovals.CountAsync(p => p.Status == "Rejected");

        ViewBag.TotalCount = totalCount;
        ViewBag.PendingCount = pendingCount;
        ViewBag.ApprovedCount = approvedCount;
        ViewBag.RejectedCount = rejectedCount;

        IQueryable<ProductApproval> query = _context.ProductApprovals;

        query = filter.ToLower() switch
        {
            "approved" => query.Where(p => p.Status == "Approved"),
            "rejected" => query.Where(p => p.Status == "Rejected"),
            "all" => query,
            _ => query.Where(p => p.Status == "Pending")
        };

        var approvals = await query
            .OrderByDescending(p => p.ReceivedDate)
            .ToListAsync();

        return View(approvals);
    }

    public async Task<IActionResult> Review(int? id)
    {
        if (id == null)
            return NotFound();

        var approval = await _context.ProductApprovals.FindAsync(id);

        if (approval == null)
            return NotFound();

        return View(approval);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string comments)
    {
        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            _logger.LogInformation("Approve transaction started - ApprovalId: {ApprovalId}", id);

            var approval = await _context.ProductApprovals.FindAsync(id);

            if (approval == null)
            {
                await dbTransaction.RollbackAsync();
                return NotFound();
            }

            if (approval.Status != "Pending")
            {
                await dbTransaction.RollbackAsync();
                TempData["Error"] = "Bu mahsulot allaqachon ko'rib chiqilgan!";
                return RedirectToAction(nameof(Index));
            }

            approval.Status = "Approved";
            approval.ReviewedDate = DateTime.UtcNow;
            approval.ReviewedBy = "Admin";
            approval.Comments = comments;

            _context.Update(approval);
            await _context.SaveChangesAsync();

            var feedback = new ApprovalFeedback
            {
                ProductId = approval.ProductId,
                Status = "Approved",
                ReviewedDate = approval.ReviewedDate.Value,
                ReviewedBy = approval.ReviewedBy,
                Comments = comments
            };

            var feedbackSent = await _feedbackService.SendFeedbackAsync(feedback);

            if (!feedbackSent)
            {
                await dbTransaction.RollbackAsync();
                _logger.LogError("Feedback send failed, rolling back database");
                TempData["Error"] = "Kafka'ga yuborishda xatolik, amaliyot bekor qilindi!";
                return RedirectToAction(nameof(Index));
            }

            await dbTransaction.CommitAsync();

            _logger.LogInformation("Approve transaction committed - ProductId: {ProductId}", approval.ProductId);
            TempData["Success"] = $"'{approval.ProductName}' tasdiqlandi va Producer'ga xabar yuborildi!";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            _logger.LogError(ex, "Approve transaction failed");
            TempData["Error"] = "Tasdiqlashda xatolik yuz berdi!";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string rejectionReason, string comments)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
        {
            TempData["Error"] = "Rad etish sababini kiriting!";
            return RedirectToAction(nameof(Review), new { id });
        }

        using var dbTransaction = await _context.Database.BeginTransactionAsync();

        try
        {
            _logger.LogInformation("Reject transaction started - ApprovalId: {ApprovalId}", id);

            var approval = await _context.ProductApprovals.FindAsync(id);

            if (approval == null)
            {
                await dbTransaction.RollbackAsync();
                return NotFound();
            }

            if (approval.Status != "Pending")
            {
                await dbTransaction.RollbackAsync();
                TempData["Error"] = "Bu mahsulot allaqachon ko'rib chiqilgan!";
                return RedirectToAction(nameof(Index));
            }

            approval.Status = "Rejected";
            approval.ReviewedDate = DateTime.UtcNow;
            approval.ReviewedBy = "Admin";
            approval.RejectionReason = rejectionReason;
            approval.Comments = comments;

            _context.Update(approval);
            await _context.SaveChangesAsync();

            var feedback = new ApprovalFeedback
            {
                ProductId = approval.ProductId,
                Status = "Rejected",
                RejectionReason = rejectionReason,
                ReviewedDate = approval.ReviewedDate.Value,
                ReviewedBy = approval.ReviewedBy,
                Comments = comments
            };

            var feedbackSent = await _feedbackService.SendFeedbackAsync(feedback);

            if (!feedbackSent)
            {
                await dbTransaction.RollbackAsync();
                _logger.LogError("Feedback send failed, rolling back database");
                TempData["Error"] = "Kafka'ga yuborishda xatolik, amaliyot bekor qilindi!";
                return RedirectToAction(nameof(Index));
            }

            await dbTransaction.CommitAsync();

            _logger.LogInformation("Reject transaction committed - ProductId: {ProductId}, Reason: {Reason}",
                approval.ProductId, rejectionReason);
            TempData["Success"] = $"'{approval.ProductName}' rad etildi va Producer'ga xabar yuborildi!";

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            _logger.LogError(ex, "Reject transaction failed");
            TempData["Error"] = "Rad etishda xatolik yuz berdi!";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkApprove(List<int> selectedIds)
    {
        if (selectedIds == null || !selectedIds.Any())
        {
            TempData["Error"] = "Hech qanday mahsulot tanlanmagan!";
            return RedirectToAction(nameof(Index));
        }

        var approvals = await _context.ProductApprovals
            .Where(p => selectedIds.Contains(p.Id) && p.Status == "Pending")
            .ToListAsync();

        if (!approvals.Any())
        {
            TempData["Error"] = "Tasdiqlash uchun mahsulotlar yo'q!";
            return RedirectToAction(nameof(Index));
        }

        int successCount = 0;
        int errorCount = 0;
        var errorProducts = new List<string>();

        foreach (var approval in approvals)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync();

            try
            {
                _logger.LogInformation("Bulk approve - Processing: {ProductName}", approval.ProductName);

                approval.Status = "Approved";
                approval.ReviewedDate = DateTime.UtcNow;
                approval.ReviewedBy = "Admin";

                _context.Update(approval);
                await _context.SaveChangesAsync();

                var feedback = new ApprovalFeedback
                {
                    ProductId = approval.ProductId,
                    Status = "Approved",
                    ReviewedDate = approval.ReviewedDate.Value,
                    ReviewedBy = approval.ReviewedBy
                };

                var feedbackSent = await _feedbackService.SendFeedbackAsync(feedback);

                if (!feedbackSent)
                {
                    throw new Exception("Feedback send failed");
                }

                await dbTransaction.CommitAsync();
                successCount++;

                _logger.LogInformation("Bulk approve success: {ProductName}", approval.ProductName);
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync();
                errorCount++;
                errorProducts.Add(approval.ProductName);
                _logger.LogError(ex, "Bulk approve failed: {ProductName}", approval.ProductName);
            }

            await Task.Delay(50);
        }

        // Natija
        if (successCount > 0 && errorCount == 0)
        {
            TempData["Success"] = $"{successCount} ta mahsulot tasdiqlandi!";
        }
        else if (successCount > 0 && errorCount > 0)
        {
            TempData["Warning"] = $"{successCount} ta tasdiqlandi, {errorCount} ta xatolik: {string.Join(", ", errorProducts)}";
        }
        else
        {
            TempData["Error"] = $"Hech qanday mahsulot tasdiqlanmadi!";
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Statistics()
    {
        var stats = new
        {
            Total = await _context.ProductApprovals.CountAsync(),
            Pending = await _context.ProductApprovals.CountAsync(p => p.Status == "Pending"),
            Approved = await _context.ProductApprovals.CountAsync(p => p.Status == "Approved"),
            Rejected = await _context.ProductApprovals.CountAsync(p => p.Status == "Rejected"),

            TodayReceived = await _context.ProductApprovals
                .CountAsync(p => p.ReceivedDate.Date == DateTime.UtcNow.Date),

            TodayReviewed = await _context.ProductApprovals
                .CountAsync(p => p.ReviewedDate.HasValue &&
                               p.ReviewedDate.Value.Date == DateTime.UtcNow.Date),

            TopRejectionReasons = await _context.ProductApprovals
                .Where(p => p.Status == "Rejected" && !string.IsNullOrEmpty(p.RejectionReason))
                .GroupBy(p => p.RejectionReason)
                .Select(g => new { Reason = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync(),

            Last7Days = Enumerable.Range(0, 7).Select(i =>
            {
                var date = DateTime.UtcNow.Date.AddDays(-i);
                return new
                {
                    Date = date.ToString("dd.MM"),
                    Received = _context.ProductApprovals.Count(p => p.ReceivedDate.Date == date),
                    Approved = _context.ProductApprovals.Count(p => p.ReviewedDate.HasValue && p.ReviewedDate.Value.Date == date && p.Status == "Approved"),
                    Rejected = _context.ProductApprovals.Count(p => p.ReviewedDate.HasValue && p.ReviewedDate.Value.Date == date && p.Status == "Rejected")
                };
            }).Reverse().ToList()
        };

        return View(stats);
    }
}