using System.ComponentModel.DataAnnotations;

namespace ConsumerApp.Models
{
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

    public class ProductApproval
    {
        public int Id { get; set; }

        public int ProductId { get; set; } 

        [Required]
        public string ProductName { get; set; }

        [Required]
        public string Category { get; set; }

        public decimal Price { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public string? Manufacturer { get; set; }

        public string KafkaMessage { get; set; } 
        public DateTime ReceivedDate { get; set; }

        public string Status { get; set; } = "Pending"; 

        public DateTime? ReviewedDate { get; set; }
        public string? ReviewedBy { get; set; }

        [StringLength(500)]
        public string? RejectionReason { get; set; }

        public string? Comments { get; set; }
    }

    public class ApprovalFeedback
    {
        public int ProductId { get; set; }
        public string Status { get; set; } 
        public string? RejectionReason { get; set; }
        public DateTime ReviewedDate { get; set; }
        public string? ReviewedBy { get; set; }
    }
}