using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // COSMECHIC-IDENTITY-COMMS-001 : réconciliation du schéma Identity (P0 confirmé
        // par COSMECHIC-ECOM-CORE-001). Cosmechic.Models.AspNetUser (CosmechicsContext,
        // scaffold database-first) mappe depuis toujours ces 4 colonnes de profil client,
        // activement utilisées (CRUD adresse admin AspNetUsersController,
        // préremplissage CartController.Summary) — mais elles n'ont jamais existé dans le
        // schéma Identity réellement migré par ce contexte, qui en reste l'unique
        // propriétaire légitime (ARCH-002/DATA-001 : CosmechicsContext exclut
        // AspNetUsers de ses propres migrations).
        //
        // Choix : propriétés fantômes (shadow properties) plutôt qu'un type ApplicationUser
        // : IdentityUser dédié. Un sous-type aurait exigé de changer la signature
        // AddDefaultIdentity<IdentityUser> et le UserManager/SignInManager<IdentityUser>
        // injecté dans TOUTES les pages Identity scaffoldées (Login, Register,
        // ForgotPassword, Manage/*...), pour un bénéfice nul ici : rien dans
        // ApplicationDbContext n'a besoin d'un accès typé à ces champs, seul
        // CosmechicsContext.AspNetUser (qui les a déjà) les consomme. Les propriétés
        // fantômes permettent à ApplicationDbContext de posséder et migrer ces colonnes
        // sans toucher un seul fichier Identity existant.
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<IdentityUser>(entity =>
            {
                entity.Property<string>("StreetAddress");
                entity.Property<string>("City");
                entity.Property<string>("State");
                entity.Property<string>("PostalCode");
            });
        }
    }
}
