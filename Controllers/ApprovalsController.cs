using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ConsumerApp.Data;
using ConsumerApp.Models;
using ConsumerApp.Services;

namespace ConsumerApp.Controllers
{
    public class ApprovalsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly FeedbackProducerService _feedbackService;
        private readonly ILogger<ApprovalsController> _logger;

        public ApprovalsController(
            ApplicationDbContext context,
            FeedbackProducerService feedbackService,
            ILogger<ApprovalsController> logger)
        {
            _context = context;
            _feedbackService = feedbackService;
            _logger = logger;
        }

        // GET: Approvals
        public async Task<IActionResult> Index(string filter = "pending")
        {
            ViewData["CurrentFilter"] = filter;

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

        // GET: Approvals/Review/5
        public async Task<IActionResult> Review(int? id)
        {
            if (id == null)
                return NotFound();

            var approval = await _context.ProductApprovals.FindAsync(id);

            if (approval == null)
                return NotFound();

            return View(approval);
        }

        // POST: Approvals/Approve/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string comments)
        {
            var approval = await _context.ProductApprovals.FindAsync(id);

            if (approval == null)
                return NotFound();

            if (approval.Status != "Pending")
            {
                TempData["Error"] = "❌ Bu mahsulot allaqachon ko'rib chiqilgan!";
                return RedirectToAction(nameof(Index));
            }

            // Status yangilash
            approval.Status = "Approved";
            approval.ReviewedDate = DateTime.UtcNow;
            approval.ReviewedBy = "Admin"; // Keyin User.Identity.Name
            approval.Comments = comments;

            _context.Update(approval);
            await _context.SaveChangesAsync();

            // Feedback yuborish
            var feedback = new ApprovalFeedback
            {
                ProductId = approval.ProductId,
                Status = "Approved",
                ReviewedDate = approval.ReviewedDate.Value,
                ReviewedBy = approval.ReviewedBy
            };

            var feedbackSent = await _feedbackService.SendFeedbackAsync(feedback);

            if (feedbackSent)
            {
                TempData["Success"] = $"✅ '{approval.ProductName}' tasdiqlandi va Producer'ga xabar yuborildi!";
            }
            else
            {
                TempData["Warning"] = $"⚠️ '{approval.ProductName}' tasdiqlandi, lekin Producer'ga xabar yuborilmadi!";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Approvals/Reject/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string rejectionReason, string comments)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                TempData["Error"] = "❌ Rad etish sababini kiriting!";
                return RedirectToAction(nameof(Review), new { id });
            }

            var approval = await _context.ProductApprovals.FindAsync(id);

            if (approval == null)
                return NotFound();

            if (approval.Status != "Pending")
            {
                TempData["Error"] = "❌ Bu mahsulot allaqachon ko'rib chiqilgan!";
                return RedirectToAction(nameof(Index));
            }

            // Status yangilash
            approval.Status = "Rejected";
            approval.ReviewedDate = DateTime.UtcNow;
            approval.ReviewedBy = "Admin";
            approval.RejectionReason = rejectionReason;
            approval.Comments = comments;

            _context.Update(approval);
            await _context.SaveChangesAsync();

            // Feedback yuborish
            var feedback = new ApprovalFeedback
            {
                ProductId = approval.ProductId,
                Status = "Rejected",
                RejectionReason = rejectionReason,
                ReviewedDate = approval.ReviewedDate.Value,
                ReviewedBy = approval.ReviewedBy
            };

            var feedbackSent = await _feedbackService.SendFeedbackAsync(feedback);

            if (feedbackSent)
            {
                TempData["Success"] = $"✅ '{approval.ProductName}' rad etildi va Producer'ga xabar yuborildi!";
            }
            else
            {
                TempData["Warning"] = $"⚠️ '{approval.ProductName}' rad etildi, lekin Producer'ga xabar yuborilmadi!";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: Bulk Approve
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkApprove(List<int> selectedIds)
        {
            if (selectedIds == null || !selectedIds.Any())
            {
                TempData["Error"] = "❌ Hech qanday mahsulot tanlanmagan!";
                return RedirectToAction(nameof(Index));
            }

            var approvals = await _context.ProductApprovals
                .Where(p => selectedIds.Contains(p.Id) && p.Status == "Pending")
                .ToListAsync();

            int successCount = 0;

            foreach (var approval in approvals)
            {
                approval.Status = "Approved";
                approval.ReviewedDate = DateTime.UtcNow;
                approval.ReviewedBy = "Admin";

                var feedback = new ApprovalFeedback
                {
                    ProductId = approval.ProductId,
                    Status = "Approved",
                    ReviewedDate = approval.ReviewedDate.Value,
                    ReviewedBy = approval.ReviewedBy
                };

                await _feedbackService.SendFeedbackAsync(feedback);
                successCount++;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"✅ {successCount} ta mahsulot tasdiqlandi!";
            return RedirectToAction(nameof(Index));
        }

        // Statistics
        public async Task<IActionResult> Statistics()
        {
            var stats = new
            {
                Total = await _context.ProductApprovals.CountAsync(),
                Pending = await _context.ProductApprovals.CountAsync(p => p.Status == "Pending"),
                Approved = await _context.ProductApprovals.CountAsync(p => p.Status == "Approved"),
                Rejected = await _context.ProductApprovals.CountAsync(p => p.Status == "Rejected")
            };

            return View(stats);
        }
    }
}