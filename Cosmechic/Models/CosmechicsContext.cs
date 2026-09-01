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

    public virtual DbSet<ShippingMethod> ShippingMethods { get; set; }

    public virtual DbSet<TaxRate> TaxRates { get; set; }

    public virtual DbSet<ShoppingCart> ShoppingCarts { get; set; }

    // COSMECHIC-COMMERCE-OPERATIONS-001B : cœur post-achat.
    public virtual DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }

    public virtual DbSet<ReturnRequest> ReturnRequests { get; set; }

    public virtual DbSet<ReturnItem> ReturnItems { get; set; }

    public virtual DbSet<Refund> Refunds { get; set; }

    public virtual DbSet<StockMovement> StockMovements { get; set; }

    // COSMECHIC-ACCOUNT-001 : adresses de livraison client, plusieurs par utilisateur.
    public virtual DbSet<CustomerAddress> CustomerAddresses { get; set; }

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

            // COSMECHIC-COMMERCE-OPERATIONS-001A (section 6/9) : mêmes conventions que
            // OrderTotal — type SQL "money", jamais float/double.
            entity.Property(e => e.Subtotal).HasColumnType("money");
            entity.Property(e => e.ShippingAmount).HasColumnType("money");
            entity.Property(e => e.TaxAmount).HasColumnType("money");
            entity.Property(e => e.DiscountAmount).HasColumnType("money");
            entity.Property(e => e.ShippingMethodName).HasMaxLength(200);

            // COSMECHIC-COMMERCE-OPERATIONS-001B (section 2/25/34) : dimensions distinctes
            // du cycle de vie post-achat, jamais mélangées avec OrderStatus/PaymentStatus.
            entity.Property(e => e.FulfillmentStatus).HasMaxLength(50);
            entity.Property(e => e.RefundedAmount).HasColumnType("money").HasDefaultValue(0m);
            entity.Property(e => e.RowVersion).IsRowVersion();

            entity.ToTable(t => t.HasCheckConstraint("CK_OrderHeaders_OrderTotal_NonNegative", "[OrderTotal] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderHeaders_RefundedAmount_WithinTotal", "[RefundedAmount] >= 0 AND [RefundedAmount] <= [OrderTotal]"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderHeaders_Subtotal_NonNegative", "[Subtotal] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderHeaders_ShippingAmount_NonNegative", "[ShippingAmount] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderHeaders_TaxAmount_NonNegative", "[TaxAmount] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderHeaders_DiscountAmount_NonNegative", "[DiscountAmount] >= 0"));
            // Invariant obligatoire (section 6) : appliqué par le moteur, pas seulement en
            // C# — aucune ligne ne peut jamais diverger de la somme de son détail.
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_OrderHeaders_Total_Equals_Components",
                "[OrderTotal] = [Subtotal] + [ShippingAmount] + [TaxAmount] - [DiscountAmount]"));

            entity.HasOne(d => d.ApplicationUser).WithMany(p => p.OrderHeaders)
                .HasForeignKey(d => d.ApplicationUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OrderHeaders_AspNetUsers");

            // Restrict (section 33) : une méthode de livraison ne doit jamais être
            // supprimée physiquement tant qu'une commande la référence — l'admin
            // n'expose de toute façon que la désactivation (comme Brand, COSMECHIC-CATALOG-001).
            entity.HasOne(d => d.ShippingMethod).WithMany(p => p.OrderHeaders)
                .HasForeignKey(d => d.ShippingMethodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OrderHeaders_ShippingMethods");
        });

        modelBuilder.Entity<ShippingMethod>(entity =>
        {
            entity.HasKey(e => e.ShippingMethodId);

            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Price).HasColumnType("money");
            entity.Property(e => e.FreeShippingThreshold).HasColumnType("money");

            entity.ToTable(t => t.HasCheckConstraint("CK_ShippingMethods_Price_NonNegative", "[Price] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_ShippingMethods_FreeShippingThreshold_NonNegative",
                "[FreeShippingThreshold] IS NULL OR [FreeShippingThreshold] >= 0"));
        });

        modelBuilder.Entity<TaxRate>(entity =>
        {
            entity.HasKey(e => e.TaxRateId);

            entity.Property(e => e.Jurisdiction).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(e => e.RegionCode).HasMaxLength(10);
            // Un taux (ex. 0.09975 pour 9.975 %) n'est pas une somme d'argent — precision
            // décimale explicite plutôt que "money" (COSMECHIC-DATA-001, même logique que
            // pour toute grandeur non monétaire).
            entity.Property(e => e.Rate).HasColumnType("decimal(9, 6)");

            entity.ToTable(t => t.HasCheckConstraint("CK_TaxRates_Rate_NonNegative", "[Rate] >= 0"));

            entity.HasIndex(e => new { e.CountryCode, e.RegionCode, e.IsActive })
                .HasDatabaseName("IX_TaxRates_Jurisdiction");
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

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PreviousStatus).HasMaxLength(50);
            entity.Property(e => e.NewStatus).HasMaxLength(50);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.ActorUserId).HasMaxLength(450);
            entity.Property(e => e.ActorType).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_OrderStatusHistories_OrderId");

            // Restrict, pas Cascade : l'historique est la preuve d'audit elle-même, elle ne
            // doit jamais disparaître silencieusement (CosmechicsContext n'expose de toute
            // façon aucune suppression physique de commande côté admin après ce lot — voir
            // §9 de la doc d'audit).
            entity.HasOne(d => d.Order).WithMany(p => p.StatusHistory)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_OrderStatusHistories_OrderHeaders");
        });

        modelBuilder.Entity<ReturnRequest>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ApplicationUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.CustomerComment).HasMaxLength(1000);
            entity.Property(e => e.AdminComment).HasMaxLength(1000);

            entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_ReturnRequests_OrderId");
            entity.HasIndex(e => e.ApplicationUserId).HasDatabaseName("IX_ReturnRequests_ApplicationUserId");

            entity.HasOne(d => d.Order).WithMany(p => p.ReturnRequests)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ReturnRequests_OrderHeaders");
        });

        modelBuilder.Entity<ReturnItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Reason).HasMaxLength(500);

            entity.ToTable(t => t.HasCheckConstraint("CK_ReturnItems_Quantity_Positive", "[Quantity] > 0"));

            entity.HasIndex(e => e.ReturnRequestId).HasDatabaseName("IX_ReturnItems_ReturnRequestId");
            entity.HasIndex(e => e.OrderDetailId).HasDatabaseName("IX_ReturnItems_OrderDetailId");

            entity.HasOne(d => d.ReturnRequest).WithMany(p => p.Items)
                .HasForeignKey(d => d.ReturnRequestId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ReturnItems_ReturnRequests");

            // Restrict : une ligne de commande (preuve d'achat) ne doit jamais disparaître
            // tant qu'un ReturnItem la référence.
            entity.HasOne(d => d.OrderDetail).WithMany()
                .HasForeignKey(d => d.OrderDetailId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_ReturnItems_OrderDetails");
        });

        modelBuilder.Entity<Refund>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.StripeRefundId).HasMaxLength(255);
            entity.Property(e => e.Amount).HasColumnType("money");
            entity.Property(e => e.MerchandiseAmount).HasColumnType("money").HasDefaultValue(0m);
            entity.Property(e => e.ShippingAmount).HasColumnType("money").HasDefaultValue(0m);
            entity.Property(e => e.TaxAmount).HasColumnType("money").HasDefaultValue(0m);
            entity.Property(e => e.Cause).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RequestedByUserId).HasMaxLength(450);
            entity.Property(e => e.ActorType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.FailureCode).HasMaxLength(100);

            entity.ToTable(t => t.HasCheckConstraint("CK_Refunds_Amount_Positive", "[Amount] > 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_Refunds_MerchandiseAmount_NonNegative", "[MerchandiseAmount] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_Refunds_ShippingAmount_NonNegative", "[ShippingAmount] >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_Refunds_TaxAmount_NonNegative", "[TaxAmount] >= 0"));
            // COSMECHIC-BUSINESS-POLICY-001 (section 4/5) : appliqué par le moteur, pas
            // seulement en C# — mêmes garanties que CK_OrderHeaders_Total_Equals_Components.
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_Refunds_Breakdown_Equals_Amount",
                "[MerchandiseAmount] + [ShippingAmount] + [TaxAmount] = [Amount]"));

            // Ancre d'idempotence (section 28/29) : appliquée par le moteur, pas seulement
            // par le code applicatif — deux tentatives concurrentes de la même opération
            // logique (même clé) ne peuvent physiquement pas produire deux lignes.
            entity.HasIndex(e => e.IdempotencyKey).IsUnique().HasDatabaseName("IX_Refunds_IdempotencyKey");
            entity.HasIndex(e => e.StripeRefundId)
                .IsUnique()
                .HasFilter("[StripeRefundId] IS NOT NULL")
                .HasDatabaseName("IX_Refunds_StripeRefundId");
            entity.HasIndex(e => e.OrderId).HasDatabaseName("IX_Refunds_OrderId");

            entity.HasOne(d => d.Order).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Refunds_OrderHeaders");

            entity.HasOne(d => d.ReturnRequest).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.ReturnRequestId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Refunds_ReturnRequests");
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.QuantityDelta).HasColumnType("decimal(18, 0)");
            entity.Property(e => e.Reason).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ActorUserId).HasMaxLength(450);
            entity.Property(e => e.ActorType).HasMaxLength(50).IsRequired();

            entity.ToTable(t => t.HasCheckConstraint("CK_StockMovements_QuantityDelta_NotZero", "[QuantityDelta] <> 0"));

            entity.HasIndex(e => e.ProduitId).HasDatabaseName("IX_StockMovements_ProduitId");

            entity.HasOne(d => d.Produit).WithMany()
                .HasForeignKey(d => d.ProduitId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_StockMovements_Produits");
        });

        modelBuilder.Entity<CustomerAddress>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ApplicationUserId).HasMaxLength(450).IsRequired();
            entity.Property(e => e.Label).HasMaxLength(100).IsRequired();
            entity.Property(e => e.RecipientName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PhoneNumber).HasMaxLength(30).IsRequired();
            entity.Property(e => e.StreetAddress).HasMaxLength(300).IsRequired();
            entity.Property(e => e.City).HasMaxLength(100).IsRequired();
            entity.Property(e => e.State).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PostalCode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CountryCode).HasMaxLength(2).IsRequired().HasDefaultValue("CA");

            // Deux index distincts sur la même colonne (surnommés explicitement via la
            // surcharge à deux arguments) : EF Core fusionnerait sinon deux
            // HasIndex(e => e.ApplicationUserId) successifs en un seul index reconfiguré.
            entity.HasIndex(e => e.ApplicationUserId, "IX_CustomerAddresses_ApplicationUserId");

            // Invariant moteur (section 13) : au plus une adresse par défaut par
            // utilisateur — index unique filtré, pas seulement une règle applicative
            // (même stratégie que IX_Refunds_StripeRefundId, COSMECHIC-COMMERCE-
            // OPERATIONS-001B).
            entity.HasIndex(e => e.ApplicationUserId, "IX_CustomerAddresses_ApplicationUserId_DefaultShipping")
                .IsUnique()
                .HasFilter("[IsDefaultShipping] = 1");

            // Cascade (contrairement à OrderHeader/ReturnRequest/Refund) : une adresse
            // enregistrée n'est jamais un enregistrement commercial historique — elle
            // n'est référencée par aucune commande (snapshot plat uniquement, section
            // 15/42) — donc supprimer un compte peut légitimement supprimer ses adresses.
            entity.HasOne(d => d.ApplicationUser).WithMany(p => p.CustomerAddresses)
                .HasForeignKey(d => d.ApplicationUserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_CustomerAddresses_AspNetUsers");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
