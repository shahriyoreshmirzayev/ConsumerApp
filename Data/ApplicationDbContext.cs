using Microsoft.EntityFrameworkCore;

namespace ConsumerApp;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProductApproval> ProductApprovals { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProductApproval>(entity =>
        {
            entity.ToTable("ProductApprovals");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ProductName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Category)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.Price)
                .HasColumnType("decimal(18,2)");

            entity.Property(e => e.Status)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.RejectionReason)
                .HasMaxLength(500);

            entity.Property(e => e.ReceivedDate)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.Status);
        });
    }
}