using ConsumerApp.Data;
using ConsumerApp.Models;
using ConsumerApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConsumerApp.Controllers
{
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
            var approval = await _context.ProductApprovals.FindAsync(id);

            if (approval == null)
                return NotFound();

            if (approval.Status != "Pending")
            {
                TempData["Error"] = "Xatolik";
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

            if (feedbackSent)
            {
                TempData["Success"] = $"{approval.ProductName} Xatolik";
            }
            else
            {
                TempData["Warning"] = $"{approval.ProductName} Xatolik";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string rejectionReason, string comments)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                TempData["Error"] = "Xatolik";
                return RedirectToAction(nameof(Review), new { id });
            }

            var approval = await _context.ProductApprovals.FindAsync(id);

            if (approval == null)
                return NotFound();

            if (approval.Status != "Pending")
            {
                TempData["Error"] = "Xatolik!";
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
                ReviewedBy = approval.ReviewedBy
            };

            var feedbackSent = await _feedbackService.SendFeedbackAsync(feedback);

            if (feedbackSent)
            {
                TempData["Success"] = $"{approval.ProductName} ' Xatolik";
            }
            else
            {
                TempData["Warning"] = $"{approval.ProductName} ' Xatolik";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkApprove(List<int> selectedIds)
        {
            if (selectedIds == null || !selectedIds.Any())
            {
                TempData["Error"] = "Xatolik";
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

            TempData["Success"] = $"{successCount}  tasdiqlandi!";
            return RedirectToAction(nameof(Index));
        }

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