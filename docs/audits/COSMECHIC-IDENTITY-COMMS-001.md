# COSMECHIC-IDENTITY-COMMS-001 — Réconciliation du schéma Identity, livraison et récupération par email

- **Lot** : COSMECHIC-IDENTITY-COMMS-001
- **Base de départ** : `060ae66f2d9610a0fe34354ffe3ba26a1da68e4e` (COSMECHIC-ECOM-CORE-001, PASS_WITH_RESERVATION)
- **Portée** : réconciliation du schéma utilisateur Identity (P0 confirmé), inscription/confirmation email, mot de passe oublié/réinitialisation, source unique d'envoi email.
- **Hors scope, volontairement non touché** : SEARCH-001, SEC-007 (upload), vulnérabilités NuGet MailKit/MimeKit (SECURITY-002), doublon `StripeSettings` (ARCH-001, warning CS0436 toujours ouvert).

## 0. Recertification COSMECHIC-ECOM-CORE-001

Avant toute modification Identity, le point ambigu du rapport précédent (transaction explicite retirée vs conservée dans `StripeFulfillmentService`) a été recertifié par lecture directe du code committé (`fa9be09`→`060ae66`) :

```
FULFILLMENT_EXPLICIT_TRANSACTION=YES
FULFILLMENT_SAVECHANGES_COUNT=1 par tentative dans le chemin critique de fulfillment
ATOMICITY_MECHANISM=Atomicité implicite EF Core (un seul SaveChangesAsync) + transaction explicite Database.BeginTransactionAsync/CommitAsync/RollbackAsync réellement présente (StripeFulfillmentService.cs:208, OrderCheckoutService.cs:107). Le retrait envisagé pendant le développement (EF Core InMemory rejette les transactions explicites par défaut) a été abandonné au profit d'une alternative ciblée : suppression de l'avertissement InMemoryEventId.TransactionIgnoredWarning dans la configuration des DbContext InMemory de test uniquement.
SQL_SERVER_ATOMICITY_TEST=PASS (TestC_MultiLineOrder_OneLineInsufficientStock_NeitherLineIsMutated, vérifié contre SQL Server réel)
ECOM_CORE_REOPEN_REQUIRED=NO
```

Aucune régression réelle trouvée : le rapport précédent était exact. ECOM-CORE-001 n'a pas été rouvert.

## 1. Gap avant (confirmé, reproduit)

Reconstruction depuis zéro sur SQL Server 2022 jetable (migrations `ApplicationDbContext` puis `CosmechicsContext`) :

| Colonne SQL réelle `AspNetUsers` (avant ce lot) | `ApplicationUser` (IdentityUser, canonique) | `AspNetUser` (CosmechicsContext) | Utilisée en production |
|---|---|---|---|
| 15 colonnes Identity standard (Id, UserName, Email, PasswordHash, ...) | Oui | Oui | Oui |
| StreetAddress | **Non** | Oui | Oui — CRUD admin `AspNetUsersController`, préremplissage `CartController.Summary()` GET |
| City | **Non** | Oui | Oui — idem |
| State | **Non** | Oui | Oui — idem |
| PostalCode | **Non** | Oui | Oui — idem |

Spot-check `AspNetRoles`/`AspNetRoleClaims`/`AspNetUserClaims`/`AspNetUserLogins`/`AspNetUserTokens` : aucune autre divergence trouvée. Le gap est isolé à ces 4 colonnes.

**Root cause** : `Cosmechic.Models.AspNetUser` (scaffold database-first de `CosmechicsContext`) a été généré à un moment où la table physique `AspNetUsers` contenait déjà ces 4 colonnes (probablement ajoutées à la main ou via une migration antérieure perdue). La migration Identity actuellement en vigueur (`00000000000000_CreateIdentitySchema`, `ApplicationDbContext`) a été générée indépendamment depuis les conventions Identity pures (`IdentityUser` nu, sans personnalisation) et n'a jamais capturé ces colonnes. Aucun type `ApplicationUser` personnalisé n'existe dans le code : le type canonique est directement `Microsoft.AspNetCore.Identity.IdentityUser`.

## 2. Modèle canonique choisi (Option B)

`IdentityUser`/`ApplicationDbContext` reste l'unique modèle canonique d'authentification (login, rôles, claims, cookies) — inchangé par ce lot. `AspNetUser`/`CosmechicsContext` n'est pas un modèle d'identité concurrent : c'est une deuxième surface de lecture/écriture légitime sur la même table physique, pour des données de profil métier (adresse), qui n'avait simplement pas un schéma synchronisé.

**Option A rejetée** (faire de `IdentityUser` l'unique modèle, supprimer `AspNetUser`) : aurait exigé de réécrire tous les `Include(o => o.ApplicationUser)`/`Include(a => a.AspNetUser)` de `OrderHeadersController`, `AvisController`, `CartController` en jointures manuelles entre deux `DbContext` distincts (EF Core ne supporte pas `Include()` à travers deux contextes) — refactor disproportionné et à haut risque de régression pour ce lot.

**Option C rejetée** (table de profil séparée 1:1) : architecturalement plus pure mais exige une nouvelle table, une nouvelle migration, et la réécriture de `AspNetUsersController` et de ses 5 vues — sans bénéfice supplémentaire par rapport à l'option B une fois le schéma synchronisé.

**Option B retenue** : `ApplicationDbContext` reste seul propriétaire du schéma `AspNetUsers` (cohérent avec ARCH-002/DATA-001), mais son modèle EF Core doit désormais connaître ces 4 colonnes pour pouvoir les migrer. Mécanisme retenu : **propriétés fantômes** (`modelBuilder.Entity<IdentityUser>().Property<string>("StreetAddress")`, etc.) dans `ApplicationDbContext.OnModelCreating`, plutôt qu'un type `ApplicationUser : IdentityUser` dédié — qui aurait exigé de changer `AddDefaultIdentity<IdentityUser>` et `UserManager<IdentityUser>`/`SignInManager<IdentityUser>` dans absolument toutes les pages Identity scaffoldées, pour un bénéfice nul (rien dans `ApplicationDbContext` n'a besoin d'un accès typé à ces champs ; seul `CosmechicsContext.AspNetUser`, qui les a déjà, les consomme).

## 3. Migration

`Cosmechic/Data/Migrations/20260901005757_AddCustomerAddressFields.cs` (contexte `ApplicationDbContext`) : purement additive.

```
Up()   : ALTER TABLE AspNetUsers ADD City/PostalCode/State/StreetAddress nvarchar(max) NULL (×4)
Down() : DROP COLUMN (×4), symétrique
```

Aucun `DROP TABLE`, aucune recréation, aucune donnée existante affectée. Type/nullabilité alignés exactement sur ce que `CosmechicsContext.AspNetUser` attendait déjà (`nvarchar(max)`, nullable — `CosmechicsContext` ne déclare aucun `HasMaxLength` pour ces colonnes).

## 4. Validation SQL Server

Base vide → migrations `ApplicationDbContext` (incluant `AddCustomerAddressFields`) → migrations `CosmechicsContext` → vérification directe des colonnes :

```
AspNetUsers.StreetAddress = PRESENT (nvarchar, nullable)
AspNetUsers.City          = PRESENT
AspNetUsers.State         = PRESENT
AspNetUsers.PostalCode    = PRESENT
```

`MODEL_MIGRATION_DRIFT=NONE` pour les deux contextes (`dotnet ef migrations has-pending-model-changes`).

Chemins runtime précédemment cassés, revalidés contre cette base reconstruite (voir `IdentitySqlServerTests.cs`, 5 tests, tous PASS) :
- `AspNetUsersController` (matérialisation complète du DbSet, CRUD adresse).
- `OrderHeadersController` (`new SelectList(_context.AspNetUsers, "Id", "Id")`).
- `AvisController` (projection `.Select(u => u.UserName)` — n'était jamais cassée, non-régression confirmée).
- `CartController.Summary()` GET (`_context.AspNetUsers.Where(...).FirstOrDefault()`).

Un utilisateur créé via `ApplicationDbContext` (comme le fait `UserManager` en production) avec adresse renseignée via propriété fantôme est relu avec succès, valeurs intactes, via `CosmechicsContext.AspNetUsers`.

## 5. Architecture email — avant / après

### Avant
```
Register.cshtml.cs      → new SmtpClient() + MimeMessage construits inline,
                           lit Smtp:Host/Port/Username/Password (IEmailSender injecté
                           mais jamais utilisé)
ForgotPassword.cshtml.cs → new SmtpClient() + MimeMessage construits inline,
                           hôte "smtp.gmail.com" et identifiants
                           "your_email@gmail.com"/"your_password" EN DUR, utilisés
                           comme logique d'exécution réelle (jamais fonctionnel)
ResendEmailConfirmation, Manage/Email, ExternalLogin → utilisaient déjà correctement
                           IEmailSender (jamais cassés), mais résolvaient vers le
                           EmailSender par défaut de Microsoft.AspNetCore.Identity.UI
                           (no-op, aucun envoi réel)
```

### Après
```
Register / ForgotPassword / ResendEmailConfirmation / Manage/Email / ExternalLogin
      ↓ (tous, uniformément)
IEmailSender (Microsoft.AspNetCore.Identity.UI.Services)
      ↓
SmtpEmailSender (Cosmechic/Services/SmtpEmailSender.cs) — seule implémentation réelle,
      enregistrée après AddDefaultIdentity pour remplacer le EmailSender par défaut
      ↓
MailKit / SMTP configuré (Smtp:Host/Port/Username/Password/FromAddress/FromName/UseSsl)
```

`git grep` exhaustif (`SmtpClient`, `MailKit`, `MimeMessage`, `your_email`, `your_password`) confirme : `SmtpClient`/`MailKit` n'existent plus que dans `SmtpEmailSender.cs` — un seul fichier, un seul point d'envoi. Aucun credential placeholder utilisé comme logique d'exécution.

## 6. Configuration

`appsettings.json`, section `Smtp` étendue (valeurs déjà présentes inchangées, seulement 3 clés ajoutées) :

```json
"Smtp": {
  "Host": "sandbox.smtp.mailtrap.io",
  "Port": 2525,
  "Username": "",
  "Password": "",
  "FromAddress": "equipe.cosmechic@gmail.com",
  "FromName": "COSMECHIC",
  "UseSsl": false
}
```

Aucune valeur réelle ajoutée (Username/Password restent vides, comme avant). `Smtp:Host` vide en configuration → `SmtpEmailSender` lève une `InvalidOperationException` maîtrisée ("Smtp:Host n'est pas configuré") plutôt que de tenter une connexion vers un hôte vide.

## 7. Comportement en cas d'échec d'envoi

Décision retenue (mandat section 15) : le compte est **toujours créé** (jamais supprimé sur un simple échec SMTP), reste **non confirmé**, l'utilisateur est redirigé normalement (aucun 500 non géré), l'erreur est **loguée** (sans exposer de détail à l'utilisateur, sans jamais loguer de secret/contenu de message), et `ResendEmailConfirmation` reste disponible sans changement pour retenter une fois le problème SMTP résolu.

Pour `ForgotPassword`, le comportement est **identique** en cas de succès ou d'échec d'envoi (toujours redirection vers `ForgotPasswordConfirmation`) — cohérent avec la protection anti-énumération de comptes déjà en place (ne jamais révéler si un compte existe), désormais étendue pour ne pas non plus révéler un problème SMTP.

Vérifié par `IdentityEmailFailureTests.cs` (3 tests) : compte créé non confirmé sans 500, resend fonctionnel après rétablissement, ForgotPassword toujours silencieux sur l'échec.

## 8. Bugs annexes corrigés (découverts en testant ce lot)

1. **`ResetPassword.cshtml.cs`** : `[Compare("Mot de passe", ...)]` référençait le libellé d'affichage français au lieu du nom réel de la propriété C# (`Password`) — cassait à la fois la validation du formulaire (comparaison impossible à résoudre) et, pire, le rendu de la page en cas de resoumission invalide (`ArgumentException` non gérée → 500). Bloquait entièrement le mandat lui-même ("reset password fonctionne", critère de sortie #12). Corrigé (`[Compare("Password", ...)]`).
2. **`Register.cshtml.cs`** : URL de confirmation construite par concaténation manuelle de chaîne (`$"{Request.Scheme}://{Request.Host}/Identity/Account/ConfirmEmail?..."`) plutôt que via `Url.Page(...)` — remplacée pour cohérence avec le reste du code (`ForgotPassword`, `ResendEmailConfirmation`, `Manage/Email` l'utilisaient déjà correctement) et robustesse (génération d'URL par le framework, jamais de domaine en dur).
3. **`EventUtility.ConstructEvent` (`throwOnApiVersionMismatch`)** : déjà corrigé dans COSMECHIC-ECOM-CORE-001, sans lien avec ce lot — mentionné ici pour mémoire uniquement, aucune ré-ouverture nécessaire (recertifié section 0).

## 9. Tests

**Total après ce lot : 92 tests, tous PASS** (80 avant + 12 nouveaux) :

- `IdentityRegistrationTests.cs` (2) : inscription crée un compte non confirmé et envoie l'email via `IEmailSender` ; flux complet inscription → confirmation → connexion (connexion refusée avant confirmation, autorisée après).
- `IdentityPasswordResetTests.cs` (2) : mot de passe oublié → réinitialisation → ancien mot de passe invalide, nouveau valide ; email inconnu ne révèle pas l'existence du compte.
- `IdentityEmailFailureTests.cs` (3) : `IEmailSender` qui échoue à l'inscription (compte créé non confirmé, pas de 500), renvoi fonctionnel après rétablissement, `ForgotPassword` toujours silencieux en cas d'échec.
- `IdentitySqlServerTests.cs` (5, **contre SQL Server réel jetable**) : persistance des champs d'adresse à travers `ApplicationDbContext`/`CosmechicsContext`, et les 4 chemins runtime précédemment cassés (section 4).

`SECURITY_001_REGRESSION=NONE`, `ECOM_CORE_001_REGRESSION=NONE`, `DATA_001_REGRESSION=NONE` — les 80 tests des lots précédents passent tous sans modification de leur logique.

## 10. Considérations de sécurité

- Aucun credential SMTP réel n'a été introduit (les placeholders restent vides en configuration, jamais remplacés par de vraies valeurs).
- Protection anti-énumération de comptes préexistante (`ForgotPassword`) conservée et étendue au cas d'échec SMTP (comportement identique succès/échec/compte inexistant).
- Toutes les URLs d'action email (confirmation, réinitialisation, changement d'email) sont générées via `Url.Page(...)` (scheme/host/route corrects, aucun domaine en dur) et encodées via `HtmlEncoder.Default.Encode(...)` avant insertion dans le HTML de l'email — vérifié exhaustivement sur les 6 points d'envoi existants.
- Le logging n'expose jamais de secret SMTP ni de contenu de message (seuls destinataire/sujet/statut sont logués).

## 11. Limites restantes

- `StripeSettings` (ARCH-001, doublon entre `Cosmechic.Utility` et `Cosmechic/Cosmechic.Utility`) : warning CS0436 toujours ouvert, volontairement non traité (hors scope explicite de ce lot).
- Vulnérabilités connues MailKit/MimeKit (NU1902) : toujours ouvertes, à traiter dans COSMECHIC-SECURITY-002 ; aucune mise à jour de package effectuée dans ce lot (non requise pour que le code fonctionne).
- `AspNetUsersController`/`CartController.Summary()` GET continuent d'utiliser `CosmechicsContext.AspNetUser` (matérialisation complète de l'entité) plutôt qu'une API Identity typée — fonctionnellement correct désormais (schéma synchronisé), mais reste un couplage direct à la table Identity physique en dehors du chemin `UserManager`/`SignInManager` habituel. Non retouché (hors scope, fonctionne).
- SEARCH-001, SEC-007 (upload) : ouverts, non touchés, confirmés hors périmètre.
