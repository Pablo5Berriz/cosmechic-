# COSMECHIC-BASELINE-2026

**Type de lot :** READ_ONLY (aucune modification métier)
**Date :** 2026-08-31
**Repository :** `Pablo5Berriz/cosmechic-`
**Auteur :** audit indépendant, exécuté sous DIRECTIVE MAÎTRE — COSMECHIC 2026

---

## 0. Vérification d'état Git obligatoire (section 0.1 du mandat)

```
$ git branch --show-current
claude/cosmechic-full-audit-0up8zj

$ git rev-parse HEAD
fa9be099f52fb66dbcf0c4edfb651f556dc227ed

$ git status --short
(vide — working tree propre)

$ git remote -v
origin  https://github.com/Pablo5Berriz/cosmechic- (fetch)
origin  https://github.com/Pablo5Berriz/cosmechic- (push)
```

| Champ | Valeur |
|---|---|
| `CURRENT_HEAD` | `fa9be099f52fb66dbcf0c4edfb651f556dc227ed` |
| `ORIGIN_MAIN` | `7738e72851202903962947159fe56c28d13a01bf` (recontrôlé via `git fetch --all --prune` — inchangé) |
| `WORKTREE_CLEAN` | `YES` |
| `AHEAD_BEHIND` | 1 commit ahead / 0 behind (le commit local `fa9be09` n'est pas encore sur `origin/main`) |

---

## 1. Vérification indépendante du commit `fa9be099f52fb66dbcf0c4edfb651f556dc227ed`

Conformément au mandat ("ne le considère pas comme valide simplement parce qu'un rapport précédent annonce PASS"), ce commit a été réexaminé sans se fier au rapport COSMECHIC-SECURITY-001 :

1. **Existence et contenu** — `git show --stat` confirme les 22 fichiers annoncés (5 controllers + `Program.cs` + `Cosmechic.sln` + 1 vue + 2 fichiers `Services/` + 12 fichiers `Cosmechic.Tests/`). Le diff complet de `OrderDetailsController.cs` et `CartController.cs` a été relu ligne à ligne : les annotations `[Authorize(Roles = "Admin")]`, le contrôle d'ownership dans `OrderConfirmation` (chargement → `NotFound` si absent → vérification `isOwner || Admin` → `Forbid` sinon → **alors seulement** l'appel Stripe), et la conversion GET→POST de `Plus/Minus/Remove` correspondent exactement à ce que le rapport décrivait.
2. **`git grep`** sur les 5 controllers confirme la présence de 21 annotations `[Authorize]`/`[Authorize(Roles="Admin")]` réparties comme annoncé (voir détail dans le corps du rapport SECURITY-001, revérifié ici et inchangé).
3. **Build reproduit indépendamment** (worktree propre, `rm -rf bin obj`, `dotnet restore` puis `dotnet build --configuration Release --no-restore`) : **0 erreur, 76 warnings** — identique au chiffre rapporté.
4. **Baseline de 80 warnings recontrôlée indépendamment** sur le commit non modifié `7738e72` (worktree séparé, même méthodologie) : **80 warnings, 0 erreur** — confirme qu'aucun nouveau warning non expliqué n'a été introduit.
5. **Tests reproduits indépendamment** : `dotnet test Cosmechic.sln --configuration Release --no-build` → **35/35 PASS, 0 FAIL**, deuxième exécution dans une session totalement différente de celle qui les a écrits.

| Champ | Valeur |
|---|---|
| `SECURITY_001_COMMIT_EXISTS` | `YES` |
| `SECURITY_001_CONTENT_MATCHES_REPORT` | `YES` |
| `SECURITY_001_TESTS_REPRODUCIBLE` | `YES` (35/35 PASS, reproduit dans une session indépendante) |
| `CURRENT_REPO_BASELINE` | `fa9be099f52fb66dbcf0c4edfb651f556dc227ed`, en avance de 1 commit sur `origin/main` |

---

## 2. Ce qui a changé depuis COSMECHIC-AUDIT-002

Les vulnérabilités SEC-001 à SEC-005 (contrôle d'accès/IDOR sur OrderDetails, Avis, OrderHeaders, AspNetUsers, CartController.OrderConfirmation/Plus/Minus/Remove) sont **CLOSED**, avec preuve automatisée (35 tests d'intégration `WebApplicationFactory`) et preuve manuelle (relecture complète des fichiers). Voir la matrice de clôture détaillée dans le rapport COSMECHIC-SECURITY-001.

Tout le reste de l'état constaté par AUDIT-002 (architecture, modèle de données, stock, paiement, dépendances, etc.) est **inchangé** — ce lot ne l'a pas touché, conformément à son périmètre strict.

---

## 3. Nouveaux constats de cette session (non couverts par les audits précédents)

Deux défauts fonctionnels réels ont été découverts pendant cette relecture, avec preuve de code (et, pour le premier, preuve d'exécution) :

### 3.1 EMAIL-001 — Inscription et réinitialisation de mot de passe cassées par construction

**Preuve CODE :** `Areas/Identity/Pages/Account/Register.cshtml.cs` (lignes 132-152) et `Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs` (lignes 60-75).

* Les deux pages **n'utilisent pas** l'abstraction `IEmailSender` injectée (`_emailSender` est un champ mort dans les deux fichiers) : elles envoient l'email via du code MailKit brut, sans `try/catch`.
* `Register.cshtml.cs` lit `Smtp:Username`/`Smtp:Password` depuis la configuration — **vides** dans `appsettings.json` (confirmé dès AUDIT-002) → `client.AuthenticateAsync("", "")` échouera très probablement contre l'hôte Mailtrap configuré, provoquant une exception non interceptée **après** que le compte ait déjà été créé en base (`_userManager.CreateAsync` a réussi avant l'envoi). Le visiteur reçoit une page d'erreur non gérée alors que son compte existe déjà, sans jamais recevoir de lien de confirmation.
* `ForgotPassword.cshtml.cs` **ignore complètement la configuration** et contient des identifiants **codés en dur et manifestement factices** : `client.ConnectAsync("smtp.gmail.com", 465, true); client.AuthenticateAsync("your_email@gmail.com", "your_password");` — échouera **à 100 % des tentatives**, sans exception interceptée.
* Combiné à `options.SignIn.RequireConfirmedAccount = true` (`Program.cs`), un compte créé via `Register` ne peut **jamais** recevoir son lien de confirmation par ce chemin et reste donc bloqué en pratique.

**Preuve RUNTIME (partielle) :** les pages `GET /Identity/Account/Register`, `/ForgotPassword`, `/ResendEmailConfirmation` répondent `200` (confirmant que `IEmailSender` se résout bien en DI via un fallback interne du framework — aucune implémentation concrète n'est enregistrée dans `Program.cs` ni ailleurs dans le dépôt). Le comportement exact du `POST` (déclenchement réel de l'échec SMTP) n'a **pas** été exécuté, conformément à l'interdiction d'envoyer un email réel ou de dépendre d'un service externe non maîtrisé pendant un lot `READ_ONLY`.
* Les trois pages qui utilisent correctement `_emailSender.SendEmailAsync(...)` (`ExternalLogin`, `ResendEmailConfirmation`, `Manage/Email`) ne plantent pas, mais **n'envoient jamais réellement d'email non plus**, faute d'implémentation concrète de `IEmailSender` enregistrée — échec silencieux plutôt qu'exception.

**Impact :** l'intégralité du funnel self-service (inscription avec confirmation obligatoire, réinitialisation de mot de passe, renvoi de confirmation, changement d'email) est non fonctionnelle en l'état. C'est un défaut **BLOCKER** pour toute mise en exploitation commerciale — plus sévère que ce qu'AUDIT-002 avait qualifié de "sous-système le plus sain" (l'infrastructure Identity elle-même est saine ; le code d'envoi d'email qui y a été greffé ne l'est pas).

### 3.2 CATALOG-001 — Recherche multi-résultats cassée (vue manquante)

**Preuve CODE :** `Controllers/ProduitsController.cs`, action `Rechercher(string query)` (lignes ~228-248) retourne `View("ResultatsRecherche", produits)` lorsque plusieurs produits correspondent partiellement à la requête. **Aucun fichier `Views/Produits/ResultatsRecherche.cshtml` n'existe** (confirmé par inventaire complet de `Views/Produits/` : `Create, Index, Details, ItemDetails, ParCategorie, Delete, Customer, Edit` — pas de `ResultatsRecherche`). Toute recherche qui ne correspond pas exactement à un seul nom de produit provoque une `InvalidOperationException` (vue introuvable) non interceptée.

**Impact :** la recherche ne "fonctionne" que par accident, dans le seul cas d'une correspondance exacte unique redirigeant vers `Details`. C'est cohérent avec le périmètre déjà prévu pour COSMECHIC-CATALOG-001 ("réparer complètement la recherche") — ce constat en précise la cause racine exacte.

---

## 4. Matrice complète des fonctionnalités

Statuts : `IMPLEMENTED` / `PARTIAL` / `BROKEN` / `ABSENT`. `SECURITY_RISK` : `CRITICAL` / `HIGH` / `MEDIUM` / `LOW` / `NONE`. `TEST_COVERAGE` : `AUTOMATED` / `NONE`.

| FEATURE | IMPLEMENTED/PARTIAL/BROKEN/ABSENT | SECURITY_RISK | TEST_COVERAGE | TARGET_LOT |
|---|---|---|---|---|
| Architecture MVC/Razor/Identity/EF Core | IMPLEMENTED (base saine, cf. AUDIT-002 §H/I) | NONE | NONE | — |
| Solution `.sln` / projets | PARTIAL — fichier/projet orphelin `Cosmechic/Cosmechic.Utility.csproj` toujours présent (ARCH-001), casse l'auto-détection `dotnet ef`/`dotnet run` mais pas le build | LOW | NONE | LOT hygiène (non prioritaire dans l'ordre imposé — à glisser dans DATA-001 ou DEVOPS-001) |
| Controllers — accès (5 contrôlés) | IMPLEMENTED — SEC-001 à SEC-005 CLOSED | NONE (était CRITICAL) | AUTOMATED (35 tests) | clos |
| Controllers — accès (Produits/Categories) | IMPLEMENTED (déjà corrects avant SECURITY-001) | NONE | NONE | — |
| Modèles de données (Produit/Category/OrderHeader/OrderDetail/ShoppingCart/Avi) | IMPLEMENTED | NONE | NONE (couvert indirectement par les tests d'autorisation, pas par des tests métier) | ECOM-CORE-001 / CATALOG-001 |
| Modèles morts (Panier/PanierItem/Commade/Paiement) | ABSENT de fait (non câblés, ARCH-004) | NONE | NONE | DATA-001 |
| Services (`Cosmechic/Services/*`) | IMPLEMENTED — nouveau, minimal (`IPaymentSessionService`, testabilité uniquement) | NONE | AUTOMATED | — |
| DbContexts (`ApplicationDbContext` / `CosmechicsContext`) | PARTIAL — deux contextes se recouvrant sur les tables Identity (ARCH-002), non résolu | HIGH | NONE | DATA-001 |
| Migrations | PARTIAL — Identity migré ; schéma métier entier sans migration (ARCH-003) | MEDIUM (reproductibilité) | NONE | DATA-001 |
| ASP.NET Core Identity (auth) | IMPLEMENTED (composant standard, non modifié) | NONE | NONE | — |
| Email (confirmation compte / reset password) | **BROKEN** — EMAIL-001, nouveau constat cette session | HIGH (fonctionnel, bloque l'inscription) | NONE | COMMUNICATIONS-001 (à envisager de remonter en priorité réelle vers ECOM-CORE-001 ou tout début de LOT — voir §6) |
| Stripe — Checkout Session (création) | IMPLEMENTED, montant recalculé serveur (point positif confirmé) | NONE sur ce point précis | NONE | ECOM-CORE-001 |
| Stripe — confirmation paiement | PARTIAL — pas de webhook, dépend de la redirection navigateur (SEC-006) | HIGH | AUTOMATED partiel (le contrôle d'ownership est testé ; la fiabilité de la confirmation elle-même ne l'est pas) | ECOM-CORE-001 |
| Stripe — idempotence | ABSENT | HIGH | NONE | ECOM-CORE-001 |
| Panier | PARTIAL — calcul de prix fiable et jamais fait confiance côté client (point positif) ; couplage stock cassé | HIGH (via BUG-001) | AUTOMATED (ownership uniquement) | ECOM-CORE-001 |
| Stock | **BROKEN** — décrémenté à l'ajout au panier, jamais restauré, pas de gestion de concurrence (BUG-001/002) | HIGH | NONE | ECOM-CORE-001 |
| Commandes (OrderHeader/OrderDetail) | PARTIAL — création fonctionnelle, statuts modélisés en chaînes libres, pas de machine à états | MEDIUM | AUTOMATED (accès uniquement) | ECOM-CORE-001 |
| Avis (Avis) | IMPLEMENTED (fonctionnellement) — accès désormais correct | NONE (était CRITICAL) | AUTOMATED | — |
| Promotions | PARTIAL — modèle lu en page d'accueil, **aucun CRUD, aucun workflow d'application au panier/checkout** | NONE | NONE | ADMIN-001 (workflow) |
| BlogPost | PARTIAL — même constat que Promotions, lecture seule en page d'accueil | NONE | NONE | CONTENT-001 (décision : intégrer ou supprimer) |
| Recherche produit | **BROKEN** — EMAIL... pardon, CATALOG-001 §3.2, vue manquante pour résultats multiples | LOW (pas de sécurité, disponibilité fonctionnelle) | NONE | CATALOG-001 |
| Filtres/tri catalogue | ABSENT | NONE | NONE | CATALOG-001 |
| Upload fichiers (Produits/Categories) | PARTIAL — fonctionne mais sans validation extension/MIME/signature (SEC-007) | HIGH | NONE | SECURITY-002 |
| Dépendances NuGet | PARTIAL — 2 vulnérabilités directes Moderate (MailKit/MimeKit) + 3 transitives High (Microsoft.Build, SQLitePCLRaw, System.Text.Json), aucune corrigée | HIGH | NONE | SECURITY-002 |
| Secrets | IMPLEMENTED (sain) — aucun secret réel dans HEAD ni l'historique complet, recontrôlé cette session | NONE | NONE | — |
| Headers sécurité web (CSP, HSTS, cookies) | PARTIAL — HSTS par défaut en Production uniquement, aucun CSP/X-Frame-Options/nosniff explicite (SEC-009) | MEDIUM | NONE | SECURITY-002 |
| Rate limiting | ABSENT (login, forgot-password, resend-confirmation, endpoints publics) | MEDIUM | NONE | SECURITY-002 |
| Base de données — reconstructibilité | **BROKEN** — confirmé outillé (`dotnet ef migrations list` → "No migrations were found" pour `CosmechicsContext`) | MEDIUM | NONE | DATA-001 |
| Précision monétaire (decimal) | PARTIAL — EF Core signale lui-même l'absence de précision explicite sur `OrderDetail.Price`/`OrderHeader.OrderTotal` | MEDIUM (troncature silencieuse possible) | NONE | DATA-001 |
| Contraintes DB (FK, unique, check) | PARTIAL — FK présentes via EF, aucune contrainte `CHECK` (stock ≥ 0, quantité > 0) | MEDIUM | NONE | DATA-001 |
| Comptes / espace client | PARTIAL — profil/commandes visibles avec ownership désormais correct, pas d'adresses multiples, pas de wishlist | NONE (sécurité) | AUTOMATED (accès) | ACCOUNT-001 |
| Administration (back-office) | PARTIAL — pas d'Area dédiée, controllers standards `[Authorize(Roles="Admin")]` + layout `_DashboardLayout` ; Dashboard/Diagrammes fonctionnels (Diagrammes = données factices, BUG-006) ; pas de gestion de stock tracée, pas de modération avis, pas de gestion promotions | LOW | NONE | ADMIN-001 |
| Livraison | ABSENT (aucun modèle, aucun coût, aucun transporteur) | NONE | NONE | COMMERCE-OPERATIONS-001 |
| Taxes | ABSENT (aucune règle fiscale, `OrderTotal` global sans détail) | NONE | NONE | COMMERCE-OPERATIONS-001 |
| Retours / remboursements | ABSENT | NONE | NONE | COMMERCE-OPERATIONS-001 |
| Facture/reçu | ABSENT | NONE | NONE | COMMERCE-OPERATIONS-001 |
| Pages légales (CGV, confidentialité, etc.) | ABSENT (seule une vue `Home/Privacy` existe, contenu non audité en détail) | NONE (juridique, hors périmètre technique) | NONE | LEGAL-PRIVACY-001 |
| Consentements (marketing/cookies) | ABSENT | NONE | NONE | LEGAL-PRIVACY-001 |
| SEO technique | **BROKEN/ABSENT** — pas de meta description, pas de canonical, pas de sitemap.xml, pas de robots.txt, pas de données structurées | NONE | NONE | SEO-001 |
| Accessibilité | NON VÉRIFIÉ EN RUNTIME — présomption de base via Bootstrap, aucun audit WCAG réalisé | NONE (à valider) | NONE | A11Y-001 |
| Responsive/UX | NON VÉRIFIÉ EN RUNTIME | NONE | NONE | UX-001 |
| CI/CD | ABSENT (aucun `.github/workflows`) | NONE | NONE | DEVOPS-001 |
| Health checks | ABSENT | NONE | NONE | DEVOPS-001 |
| Logs structurés | ABSENT (logging par défaut minimal) | LOW | NONE | DEVOPS-001 |
| Tests (hors périmètre SECURITY-001) | ABSENT pour Produits/Categories/Home/panier-métier/stock/paiement | HIGH (dette) | NONE | chaque LOT concerné |
| Performance (N+1, cache, images) | NON MESURÉ — quelques requêtes N+1 identifiées par lecture (AUDIT-002 §AG) | LOW | NONE | PERFORMANCE-001 |

---

## 5. Vérifications manuelles effectuées cette session (au-delà de la relecture de rapports)

1. Reproduction indépendante complète du cycle `restore`/`build`/`test` sur `HEAD` (`fa9be09`) et sur `origin/main` non modifié (`7738e72`) dans des worktrees isolés.
2. Lecture ligne à ligne du diff complet de `OrderDetailsController.cs` et `CartController.cs` dans le commit `fa9be09`.
3. `git grep` sur les annotations `[Authorize]` des 5 controllers concernés, résultat comparé au rapport SECURITY-001.
4. Nouvelle recherche de secrets sur l'arbre courant et l'historique Git complet (y compris le commit SECURITY-001) — aucun trouvé.
5. Inventaire exhaustif des actions de chaque controller (routes) par lecture directe du code source.
6. Inventaire exhaustif des vues de `Produits/`, `Categories/`, `Home/`, `Avis/`, `Cart/`, `OrderHeaders/`, `OrderDetails/`, `AspNetUsers/`.
7. Recherche de toute implémentation concrète de `IEmailSender` dans le dépôt (aucune trouvée) et de tout enregistrement DI correspondant dans `Program.cs` (aucun trouvé).
8. Démarrage réel de l'application (sans base de données ni secret réel) pour confirmer que les pages `Register`/`ForgotPassword`/`ResendEmailConfirmation` répondent `200` en GET (DI résolue), sans déclencher de `POST` ni d'envoi d'email réel.
9. Confirmation qu'aucun `Area` autre que `Identity` n'existe (pas d'espace admin dédié).
10. Confirmation qu'aucun controller Promotions/BlogPost n'existe (lecture seule confirmée dans `HomeController`).

---

## 6. Recommandation PM (à titre d'information — n'engage pas d'exécution)

Ce lot est strictement `READ_ONLY` et ne préjuge pas de la décision d'exécution du PM. Une observation factuelle est toutefois soumise à son arbitrage : **EMAIL-001** (inscription/réinitialisation cassées) bloque de fait tout le cœur commercial visé par **COSMECHIC-ECOM-CORE-001** — un client qui ne peut pas confirmer son compte ne peut pas non plus, en pratique, aller jusqu'au paiement dans un scénario réaliste de bout en bout. Ce constat est remonté ici tel quel ; il appartient au PM de décider s'il doit être traité en préalable, en parallèle, ou en fin de LOT 1, dans la directive d'exécution qu'il transmettra.

---

## 7. Rapport de lot (format section 20 du mandat)

```
LOT=COSMECHIC-BASELINE-2026
STATUS=PASS
BASELINE_SHA=fa9be099f52fb66dbcf0c4edfb651f556dc227ed
FINAL_SHA=(voir commit documentaire de ce lot, ci-dessous)
FILES_CHANGED=0
FILES_ADDED=1 (docs/audits/COSMECHIC-BASELINE-2026.md)
FILES_DELETED=0
IMPLEMENTED=vérification indépendante du commit SECURITY-001 ; matrice complète des fonctionnalités ; 2 nouveaux défauts documentés (EMAIL-001, CATALOG-001 recherche)
NOT_IMPLEMENTED=aucune modification métier (lot READ_ONLY par mandat)
SECURITY_IMPACT=NONE (lecture seule) — mais 2 constats HIGH nouvellement documentés (EMAIL-001) et confirmation de tous les risques déjà connus (SEC-006/007/009, ARCH-002/003, BUG-001/002, vulnérabilités NuGet)
DATABASE_IMPACT=NONE
MIGRATION_IMPACT=NONE
TESTS_RUN=dotnet test Cosmechic.sln --configuration Release --no-build (reproduction, pas de nouveau test ajouté)
TEST_RESULTS=35/35 PASS
BUILD=PASS (0 erreur, 76 warnings, reproduit deux fois : HEAD et baseline 7738e72 à 80 warnings)
WARNINGS_BASELINE=80 (7738e72, recompté indépendamment)
WARNINGS_FINAL=76 (fa9be09, recompté indépendamment)
DEPENDENCY_AUDIT=inchangé depuis BASELINE-001/SECURITY-001 : 2 Moderate directes (MailKit/MimeKit) + 3 High transitives (Microsoft.Build, SQLitePCLRaw.lib.e_sqlite3, System.Text.Json), aucune corrigée (hors périmètre de ce lot)
MANUAL_VERIFICATIONS=voir section 5 ci-dessus (10 vérifications listées)
KNOWN_LIMITATIONS=aucune instance SQL Server disponible dans ce bac à sable — comportement runtime contre une vraie base non observable ; comportement exact du POST Register/ForgotPassword non exécuté (interdiction d'envoi d'email réel)
RISKS_REMAINING=voir matrice complète section 4 ; risques HIGH non traités : ARCH-002 (double DbContext), SEC-006 (pas de webhook Stripe), SEC-007 (upload non validé), BUG-001 (stock), EMAIL-001 (nouveau), 3 CVE NuGet High
ROLLBACK=trivial — fichier markdown neuf uniquement, aucun code applicatif touché
COMMIT=(voir SHA ci-dessus, message "docs(audit): COSMECHIC-BASELINE-2026 read-only findings")
PUSHED=NO
SAFE_TO_START_NEXT_LOT=YES — sous réserve de l'arbitrage PM sur EMAIL-001 (section 6)
```

**STOP — fin du lot COSMECHIC-BASELINE-2026. En attente de la directive d'exécution du PM pour COSMECHIC-ECOM-CORE-001.**
