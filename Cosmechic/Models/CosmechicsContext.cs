using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Models;

public partial class CosmechicsContext : DbContext
{
    public CosmechicsContext()
    {
    }

    public CosmechicsContext(DbContextOptions<CosmechicsContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AspNetRole> AspNetRoles { get; set; }

    public virtual DbSet<AspNetRoleClaim> AspNetRoleClaims { get; set; }

    public virtual DbSet<AspNetUser> AspNetUsers { get; set; }

    public virtual DbSet<AspNetUserClaim> AspNetUserClaims { get; set; }

    public virtual DbSet<AspNetUserLogin> AspNetUserLogins { get; set; }

    public virtual DbSet<AspNetUserToken> AspNetUserTokens { get; set; }

    public virtual DbSet<Avi> Avis { get; set; }

    public virtual DbSet<BlogPost> BlogPosts { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<OrderDetail> OrderDetails { get; set; }

    public virtual DbSet<OrderHeader> OrderHeaders { get; set; }

    public virtual DbSet<Produit> Produits { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<ShoppingCart> ShoppingCarts { get; set; }

    public virtual DbSet<TemoignagesClient> TemoignagesClients { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");

            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<AspNetRoleClaim>(entity =>
        {
            entity.HasIndex(e => e.RoleId, "IX_AspNetRoleClaims_RoleId");

            entity.HasOne(d => d.Role).WithMany(p => p.AspNetRoleClaims).HasForeignKey(d => d.RoleId);
        });

        modelBuilder.Entity<AspNetUser>(entity =>
        {
            entity.HasIndex(e => e.NormalizedEmail, "EmailIndex");

            entity.HasIndex(e => e.NormalizedUserName, "UserNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedUserName] IS NOT NULL)");

            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.NormalizedEmail).HasMaxLength(256);
            entity.Property(e => e.NormalizedUserName).HasMaxLength(256);
            entity.Property(e => e.UserName).HasMaxLength(256);

            entity.HasMany(d => d.Roles).WithMany(p => p.Users)
                .UsingEntity<Dictionary<string, object>>(
                    "AspNetUserRole",
                    r => r.HasOne<AspNetRole>().WithMany().HasForeignKey("RoleId"),
                    l => l.HasOne<AspNetUser>().WithMany().HasForeignKey("UserId"),
                    j =>
                    {
                        j.HasKey("UserId", "RoleId");
                        j.ToTable("AspNetUserRoles");
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<AspNetUserClaim>(entity =>
        {
            entity.HasIndex(e => e.UserId, "IX_AspNetUserClaims_UserId");

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserClaims).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserLogin>(entity =>
        {
            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });

            entity.HasIndex(e => e.UserId, "IX_AspNetUserLogins_UserId");

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.ProviderKey).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserLogins).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserToken>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.LoginProvider, e.Name });

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.Name).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserTokens).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<Avi>(entity =>
        {
            entity.HasKey(e => e.ReviewId);

            entity.Property(e => e.ReviewId)
                .ValueGeneratedNever()
                .HasColumnName("ReviewID");
            entity.Property(e => e.AspNetUserId)
                .HasMaxLength(450)
                .HasColumnName("AspNetUserID");
            entity.Property(e => e.DateReview).HasColumnType("datetime");
            entity.Property(e => e.PaiementId)
                .ValueGeneratedOnAdd()
                .HasColumnName("PaiementID");
            entity.Property(e => e.ProduitId).HasColumnName("ProduitID");

            entity.HasOne(d => d.AspNetUser).WithMany(p => p.Avis)
                .HasForeignKey(d => d.AspNetUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Avis_AspNetUsers");

            entity.HasOne(d => d.Produit).WithMany(p => p.Avis)
                .HasForeignKey(d => d.ProduitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Avis_Produits");
        });

        modelBuilder.Entity<BlogPost>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__BlogPost__3214EC07806A658A");

            entity.Property(e => e.DatePublication)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Image).HasMaxLength(500);
            entity.Property(e => e.Titre).HasMaxLength(250);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategorieId);

            entity.Property(e => e.CategorieId).HasColumnName("CategorieID");
            entity.Property(e => e.Image)
                .HasMaxLength(450)
                .IsFixedLength();
            entity.Property(e => e.Nom)
                .HasMaxLength(450)
                .IsFixedLength();
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            entity.HasOne(d => d.OrderHeader).WithMany(p => p.OrderDetails)
                .HasForeignKey(d => d.OrderHeaderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderDetails_OrderHeaders");

            entity.HasOne(d => d.Produit).WithMany(p => p.OrderDetails)
                .HasForeignKey(d => d.ProduitId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderDetails_Produits");
        });

        modelBuilder.Entity<OrderHeader>(entity =>
        {
            entity.Property(e => e.ApplicationUserId).HasMaxLength(450);

            entity.HasOne(d => d.ApplicationUser).WithMany(p => p.OrderHeaders)
                .HasForeignKey(d => d.ApplicationUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderHeaders_AspNetUsers");
        });

        modelBuilder.Entity<Produit>(entity =>
        {
            entity.HasKey(e => e.ProduitId).HasName("PK_Table_1");

            entity.Property(e => e.ProduitId).HasColumnName("ProduitID");
            entity.Property(e => e.CategorieId).HasColumnName("CategorieID");
            entity.Property(e => e.Image).HasMaxLength(450);
            entity.Property(e => e.Nom).HasMaxLength(450);
            entity.Property(e => e.Prix).HasColumnType("money");
            entity.Property(e => e.Stock).HasColumnType("decimal(18, 0)");

            entity.HasOne(d => d.Categorie).WithMany(p => p.Produits)
                .HasForeignKey(d => d.CategorieId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Produits_Categories");
        });

        modelBuilder.Entity<Promotion>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Promotio__3214EC071F180FF8");

            entity.Property(e => e.DateDebut).HasColumnType("datetime");
            entity.Property(e => e.DateFin).HasColumnType("datetime");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Remise).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.Titre).HasMaxLength(250);
        });

        modelBuilder.Entity<ShoppingCart>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(d => d.Produit) 
                .WithMany()
                .HasForeignKey(d => d.ProduitId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TemoignagesClient>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PK__Temoigna__3214EC07F49AF2DD");

            entity.Property(e => e.Commentaire).HasMaxLength(1000);
            entity.Property(e => e.Date)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Nom).HasMaxLength(250);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
