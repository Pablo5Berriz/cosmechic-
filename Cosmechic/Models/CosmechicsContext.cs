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

    public virtual DbSet<Brand> Brands { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<ProduitImage> ProduitImages { get; set; }

    public virtual DbSet<OrderDetail> OrderDetails { get; set; }

    public virtual DbSet<OrderHeader> OrderHeaders { get; set; }

    public virtual DbSet<Produit> Produits { get; set; }

    public virtual DbSet<Promotion> Promotions { get; set; }

    public virtual DbSet<ShoppingCart> ShoppingCarts { get; set; }

    public virtual DbSet<TemoignagesClient> TemoignagesClients { get; set; }

    // Préparation COSMECHIC-DATA-001 pour l'idempotence Stripe (COSMECHIC-ECOM-CORE-001).
    public virtual DbSet<ProcessedStripeEvent> ProcessedStripeEvents { get; set; }

    // COSMECHIC-DATA-001 : garde standard manquante dans le scaffold d'origine. Sans elle,
    // OnConfiguring réappliquait inconditionnellement UseSqlServer même quand le contexte
    // est déjà construit avec des DbContextOptions pleinement configurées ailleurs (tests,
    // outillage) — sans effet visible dans l'hôte ASP.NET Core réel (Program.cs résout la
    // même valeur de connexion), mais empêchant toute construction autonome de
    // CosmechicsContext hors du pipeline applicatif, ce qui bloquait les tests
    // d'intégration SQL Server réels exigés par ce lot.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Name=ConnectionStrings:DefaultConnection");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ARCH-002 (COSMECHIC-AUDIT-002) : ces 6 tables Identity (+ la table de jointure
        // AspNetUserRoles ci-dessous) sont déjà créées et versionnées par les migrations
        // de ApplicationDbContext (IdentityDbContext). CosmechicsContext continue de les
        // interroger/écrire normalement au runtime (ExcludeFromMigrations n'affecte QUE la
        // génération de migrations, jamais les requêtes) mais ne doit plus jamais générer
        // de CREATE/ALTER/DROP pour elles — éviter que deux jeux de migrations distincts
        // ne se disputent la même table physique. Voir docs/audits/COSMECHIC-DATA-001.md
        // pour l'analyse complète et le gap connu (colonnes StreetAddress/City/State/
        // PostalCode absentes du modèle Identity de base, non résolu dans ce lot).
        modelBuilder.Entity<AspNetRole>(entity =>
        {
            entity.ToTable("AspNetRoles", t => t.ExcludeFromMigrations());

            entity.HasIndex(e => e.NormalizedName, "RoleNameIndex")
                .IsUnique()
                .HasFilter("([NormalizedName] IS NOT NULL)");

            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.NormalizedName).HasMaxLength(256);
        });

        modelBuilder.Entity<AspNetRoleClaim>(entity =>
        {
            entity.ToTable("AspNetRoleClaims", t => t.ExcludeFromMigrations());

            entity.HasIndex(e => e.RoleId, "IX_AspNetRoleClaims_RoleId");

            entity.HasOne(d => d.Role).WithMany(p => p.AspNetRoleClaims).HasForeignKey(d => d.RoleId);
        });

        modelBuilder.Entity<AspNetUser>(entity =>
        {
            entity.ToTable("AspNetUsers", t => t.ExcludeFromMigrations());

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
                        j.ToTable("AspNetUserRoles", t => t.ExcludeFromMigrations());
                        j.HasIndex(new[] { "RoleId" }, "IX_AspNetUserRoles_RoleId");
                    });
        });

        modelBuilder.Entity<AspNetUserClaim>(entity =>
        {
            entity.ToTable("AspNetUserClaims", t => t.ExcludeFromMigrations());

            entity.HasIndex(e => e.UserId, "IX_AspNetUserClaims_UserId");

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserClaims).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserLogin>(entity =>
        {
            entity.ToTable("AspNetUserLogins", t => t.ExcludeFromMigrations());

            entity.HasKey(e => new { e.LoginProvider, e.ProviderKey });

            entity.HasIndex(e => e.UserId, "IX_AspNetUserLogins_UserId");

            entity.Property(e => e.LoginProvider).HasMaxLength(128);
            entity.Property(e => e.ProviderKey).HasMaxLength(128);

            entity.HasOne(d => d.User).WithMany(p => p.AspNetUserLogins).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<AspNetUserToken>(entity =>
        {
            entity.ToTable("AspNetUserTokens", t => t.ExcludeFromMigrations());

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
            entity.Property(e => e.Slug).HasMaxLength(450);

            // Index unique filtré (WHERE NOT NULL) : permet un rétro-remplissage progressif
            // sans exiger une valeur immédiate sur les lignes historiques (COSMECHIC-CATALOG-001,
            // section 49).
            entity.HasIndex(e => e.Slug)
                .IsUnique()
                .HasFilter("[Slug] IS NOT NULL")
                .HasDatabaseName("IX_Categories_Slug");
        });

        modelBuilder.Entity<OrderDetail>(entity =>
        {
            // Précision monétaire explicite (EF signalait ces deux propriétés comme non
            // configurées, COSMECHIC-BASELINE-001). Convention retenue : aligner sur le
            // type déjà utilisé pour Produit.Prix (SQL "money"), dont OrderDetail.Price
            // est directement une capture — cohérence plutôt qu'une nouvelle convention.
            entity.Property(e => e.Price).HasColumnType("money");
            entity.Property(e => e.ProduitNom).HasMaxLength(450);

            entity.ToTable(t => t.HasCheckConstraint("CK_OrderDetails_Count_Positive", "[Count] > 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderDetails_Price_NonNegative", "[Price] >= 0"));

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
            entity.Property(e => e.OrderTotal).HasColumnType("money");

            entity.ToTable(t => t.HasCheckConstraint("CK_OrderHeaders_OrderTotal_NonNegative", "[OrderTotal] >= 0"));

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
            // Jeton de concurrence optimiste (préparation ECOM-CORE-001, section 12 du
            // mandat) : SQL Server "rowversion", géré automatiquement par le moteur, ne
            // requiert aucune donnée existante et n'affecte aucune requête actuelle.
            entity.Property(e => e.RowVersion).IsRowVersion();

            entity.ToTable(t => t.HasCheckConstraint("CK_Produits_Stock_NonNegative", "[Stock] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_Produits_Prix_NonNegative", "[Prix] >= 0"));

            entity.HasOne(d => d.Categorie).WithMany(p => p.Produits)
                .HasForeignKey(d => d.CategorieId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Produits_Categories");

            // COSMECHIC-CATALOG-001 : champs catalogue. Sku/Slug nullable en base (rétro-
            // remplissage progressif via CatalogBackfillService, section 49/50) mais
            // uniques dès qu'une valeur est présente ; requis par validation applicative
            // pour tout produit créé après ce lot (section 16/17).
            entity.Property(e => e.Sku).HasMaxLength(64);
            entity.Property(e => e.Slug).HasMaxLength(450);
            entity.Property(e => e.IngredientsInci).HasColumnType("nvarchar(max)");
            entity.Property(e => e.UsageInstructions).HasColumnType("nvarchar(max)");
            entity.Property(e => e.Warnings).HasColumnType("nvarchar(max)");
            entity.Property(e => e.NetQuantity).HasMaxLength(100);
            entity.Property(e => e.SeoTitle).HasMaxLength(200);
            entity.Property(e => e.SeoDescription).HasMaxLength(500);
            entity.Property(e => e.DateCreation).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(e => e.Sku)
                .IsUnique()
                .HasFilter("[Sku] IS NOT NULL")
                .HasDatabaseName("IX_Produits_Sku");

            entity.HasIndex(e => e.Slug)
                .IsUnique()
                .HasFilter("[Slug] IS NOT NULL")
                .HasDatabaseName("IX_Produits_Slug");

            entity.HasIndex(e => e.BrandId).HasDatabaseName("IX_Produits_BrandId");
            entity.HasIndex(e => e.Disponible).HasDatabaseName("IX_Produits_Disponible");

            // Restrict plutôt que SetNull : une marque référencée par au moins un produit
            // ne doit jamais être supprimée physiquement — l'admin Brand n'expose que la
            // désactivation (section 39), ceci est le filet de sécurité au niveau DB.
            entity.HasOne(d => d.Brand).WithMany(b => b.Produits)
                .HasForeignKey(d => d.BrandId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Produits_Brands");
        });

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.HasKey(e => e.BrandId);

            entity.Property(e => e.Nom).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => e.Slug).IsUnique().HasDatabaseName("IX_Brands_Slug");
            entity.HasIndex(e => e.Nom).IsUnique().HasDatabaseName("IX_Brands_Nom");
        });

        modelBuilder.Entity<ProduitImage>(entity =>
        {
            entity.HasKey(e => e.ProduitImageId);

            entity.Property(e => e.FileName).HasMaxLength(450).IsRequired();
            entity.Property(e => e.AltText).HasMaxLength(300);

            entity.HasIndex(e => e.ProduitId).HasDatabaseName("IX_ProduitImages_ProduitId");

            // Cascade : les images n'ont aucune existence hors de leur produit (contrairement
            // à Produit lui-même, qui reste protégé par OrderDetails/Avis).
            entity.HasOne(d => d.Produit).WithMany(p => p.Images)
                .HasForeignKey(d => d.ProduitId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProduitImages_Produits");
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

            // ApplicationUserId n'est pas une FK EF (voir OrderHeader.ApplicationUser pour
            // la seule relation formalisée vers AspNetUser) mais c'est la colonne filtrée
            // par CartController sur pratiquement chaque action (Index/Summary/Plus/Minus/
            // Remove) : index explicitement justifié par l'usage réel, pas ajouté par principe.
            entity.HasIndex(e => e.ApplicationUserId, "IX_ShoppingCarts_ApplicationUserId");

            entity.ToTable(t => t.HasCheckConstraint("CK_ShoppingCarts_Count_Positive", "[Count] > 0"));

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

            // Aucune FK explicite n'existait vers Produit dans le modèle scaffoldé
            // d'origine ; EF la découvrait par convention (ProduitId + navigation Produit),
            // avec un comportement de suppression par défaut (Cascade, car FK requise) non
            // documenté et incohérent avec Avis/OrderDetails (ClientSetNull). Rendu explicite
            // ici sans changer le comportement réel : voir docs/audits/COSMECHIC-DATA-001.md.
            entity.HasOne(d => d.Produit).WithMany()
                .HasForeignKey(d => d.ProduitId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TemoignagesClients_Produits");
        });

        modelBuilder.Entity<ProcessedStripeEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.StripeEventId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProcessingStatus).HasMaxLength(50).IsRequired();

            // Clé de l'idempotence : un même événement Stripe ne doit jamais être traité
            // deux fois (mandat section 13/16 — "StripeEventId UNIQUE").
            entity.HasIndex(e => e.StripeEventId, "IX_ProcessedStripeEvents_StripeEventId").IsUnique();

            entity.HasOne(d => d.Order).WithMany()
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ProcessedStripeEvents_OrderHeaders");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
