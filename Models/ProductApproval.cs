using System.ComponentModel.DataAnnotations;

namespace ConsumerApp.Models
{
    // Kafka'dan kelgan mahsulot
    public class ReceivedProduct
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public string? Manufacturer { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    // Tasdiqlash jarayoni uchun
    public class ProductApproval
    {
        public int Id { get; set; }

        public int ProductId { get; set; } // Producer'dagi mahsulot ID

        [Required]
        public string ProductName { get; set; }

        [Required]
        public string Category { get; set; }

        public decimal Price { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public string? Manufacturer { get; set; }

        // Kafka ma'lumotlari
        public string KafkaMessage { get; set; } // To'liq JSON
        public DateTime ReceivedDate { get; set; }

        // Tasdiqlash holati
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected

        public DateTime? ReviewedDate { get; set; }
        public string? ReviewedBy { get; set; }

        [StringLength(500)]
        public string? RejectionReason { get; set; }

        public string? Comments { get; set; }
    }

    // Feedback (Producer'ga yuboriladi)
    public class ApprovalFeedback
    {
        public int ProductId { get; set; }
        public string Status { get; set; } // Approved / Rejected
        public string? RejectionReason { get; set; }
        public DateTime ReviewedDate { get; set; }
        public string? ReviewedBy { get; set; }
    }
}