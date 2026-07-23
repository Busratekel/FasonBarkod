using FasonBarkod.Core.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FasonBarkod.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();

    public DbSet<BarcodePrint> BarcodePrints => Set<BarcodePrint>();

    public DbSet<SapTransferLog> SapTransferLogs => Set<SapTransferLog>();

    public DbSet<AppPrintSettings> AppPrintSettings => Set<AppPrintSettings>();

    public DbSet<LabelTemplate> LabelTemplates => Set<LabelTemplate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.VendorCode).HasMaxLength(20);
        });

        builder.Entity<SalesOrderLine>(entity =>
        {
            entity.HasIndex(e => e.SalesOrderNo);
            entity.HasIndex(e => new { e.SalesOrderNo, e.MaterialCode });
            entity.Property(e => e.Quantity).HasPrecision(18, 3);
        });

        builder.Entity<BarcodePrint>(entity =>
        {
            entity.HasIndex(e => e.BarcodeNo);
            entity.HasIndex(e => e.SalesOrderNo);
            entity.HasIndex(e => e.VendorCode);
            entity.HasIndex(e => e.PrintDate);
            entity.Property(e => e.VendorCode).HasMaxLength(20);
            entity.Property(e => e.LineNo).HasMaxLength(20);
            entity.Property(e => e.SerialNumber).HasMaxLength(64);
            entity.Property(e => e.Quantity).HasPrecision(18, 3);
            entity.HasOne(e => e.PrintedByUser)
                .WithMany()
                .HasForeignKey(e => e.PrintedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<SapTransferLog>(entity =>
        {
            entity.HasOne(e => e.BarcodePrint)
                .WithMany(e => e.TransferLogs)
                .HasForeignKey(e => e.BarcodePrintId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AppPrintSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.PrinterName).HasMaxLength(512);
        });

        builder.Entity<LabelTemplate>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.BrandCode).HasMaxLength(100);
            entity.Property(e => e.Content).IsRequired();
            entity.HasIndex(e => new { e.LabelType, e.BrandCode });
        });
    }
}
