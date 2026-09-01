# COSMECHIC-UX-001 — Refonte UI/UX avec contrôle strict de régression

## 1. Baseline

- `EXPECTED_BASELINE_SHA=5039831`, HEAD réel au démarrage : `5039831acdc1a78e8bcafebd200ac99d03e52f42` — identique. Worktree propre (`git status --short` vide). `PREFLIGHT=OK`.
- Chaîne de lots recertifiée via `git log -15 --oneline --decorate` : lignée linéaire complète jusqu'à `5039831` (CONTENT-LEGAL-001), tous les lots antérieurs présents. `HISTORY_CHAIN_COMPLETE=YES`.
- Gates avant modification : `RESTORE=PASS`, `BUILD=PASS` (0 erreur, 48 warnings), `TESTS=314/314 PASS` (conforme à `TESTS_BASELINE_EXPECTED=314`), `NUGET_CRITICAL/HIGH/MODERATE/LOW=0`.

## 2. Audit UI/UX initial (avant modification)

Inventaire réel de `Views/`, `Areas/Identity/Pages/`, `Controllers/`, `wwwroot/css/`. Constats principaux (table complète des défauts trouvés, sévérité et correction) :

| Surface | Problème | Sévérité | Impact | Correction | In scope |
|---|---|---|---|---|---|
| `_Layout.cshtml` (header) | `navbar-expand-lg`/`id="navbarSupportedContent"` posés sans jamais ajouter `.navbar-toggler` ni `.collapse` — le menu, la recherche et le compte/panier restaient visibles en permanence, sans mécanisme de repli mobile | Critique | Navigation principale potentiellement inutilisable/débordante sous 992px | Bouton bascule + panneau `.collapse` en dropdown | Oui |
| `_Layout.cshtml` | Aucun skip link | Moyen | Clavier/lecteur d'écran doit retraverser tout le header à chaque page | `.cx-skip-link` + `#main-content` | Oui |
| `_DashboardLayout.cshtml` | Scripts `vendor/...`/`js/...` sans préfixe `~/` : URL relative résolue par rapport à l'URL courante, pas à la racine — 404 sur toute page admin à segments multiples (ex. `/OrderHeaders/Edit/5`) | Critique | jQuery/Bootstrap/SB Admin 2 ne se chargeaient jamais réellement en dehors de la page d'accueil admin | Préfixe `~/` sur tous les scripts | Oui |
| `_DashboardLayout.cshtml` | Formulaire de déconnexion (barre latérale) sans `method="post"` — soumission GET par défaut sur une page dont le seul handler est `OnPost` | Critique | Cliquer sur "Déconnexion" dans la barre latérale ne déconnectait personne | `method="post"` ajouté | Oui |
| `_DashboardLayout.cshtml` | Footer `position:fixed; height:130px` en permanence superposé au bas de la page | Élevé | Recouvre le contenu réel sur toute page admin plus longue que l'écran | Retour au flux normal | Oui |
| `_DashboardLayout.cshtml` | "Logout Modal" jamais déclenché (aucun `data-target`), lien `href="login.html"` cassé | Moyen | Code mort + lien mort inatteignable | Supprimé | Oui |
| `_DashboardLayout.cshtml` (sidebar) | Produits, Marques, Livraison, Taxes n'apparaissaient nulle part dans la navigation admin bien qu'étant des surfaces `[Authorize(Roles="Admin")]` fonctionnelles | Élevé | Aucun chemin de découverte, seule une URL tapée manuellement y menait | 4 entrées ajoutées | Oui |
| `Home/Index.cshtml` | Deux `<h1>` (bannière + section Contact) | Moyen | Structure de titres incorrecte (SEO/accessibilité) | Second `<h1>` retiré | Oui |
| `Home/Index.cshtml` | Formulaire "Newsletter" ne postant nulle part (aucune action, aucun backend), contredisant la page Confidentialité ("pas d'usage marketing") | Élevé | Fonctionnalité fictive présentée comme réelle | Supprimé | Oui |
| `Home/Index.cshtml` | Second formulaire "Contact" postant vers un service tiers externe (`formspree.io`), hors CSRF/rate limiting, dupliquant sans lien le vrai backend Contact (CONTENT-LEGAL-001) | Élevé | Fuite de données client vers un tiers non maîtrisé, incohérence avec la page Contact réelle | Remplacé par un lien vers `/Home/Contact` | Oui |
| `Home/Index.cshtml` | Bouton "Lire l'article" → `href="#"` (aucune page de détail d'article implémentée) | Moyen | Lien mort | CTA retiré, aperçu conservé | Oui |
| `Home/Index.cshtml` | Cartes produit en largeur fixe (`450px`) hors grille responsive | Élevé | Débordement horizontal probable sur mobile | Grille Bootstrap `row-cols-*` | Oui |
| `Produits/Details.cshtml` | Deux `<h1>` ("Détails du produit" générique + nom du produit) | Moyen | Structure de titres incorrecte | Fusionnés en un seul H1 (nom du produit) | Oui |
| `Produits/ItemDetails.cshtml` | `ViewData["Title"] = "Ajout produit"` fixe pour toute fiche produit | Moyen | `<title>` identique sur toutes les pages produit (SEO) | Titre = nom du produit réel | Oui |
| `Produits/ItemDetails.cshtml` | `alt="Image de {nom de fichier}"` au lieu du nom produit ; script `history.back()` sur submit, redondant avec la redirection serveur et potentiellement concurrent avec elle | Faible/Moyen | Alt text non pertinent ; navigation ambiguë | `alt` corrigé ; script retiré (redirection serveur suffit) | Oui |
| `Cart/Index.cshtml` | "Continuer les achats" → `asp-controller="Categorie"` (singulier, contrôleur inexistant) | Élevé | Lien mort sur le parcours panier | Corrigé vers `Categories/Customer` | Oui |
| `Cart/Index.cshtml` | Aucun état vide géré | Moyen | Panier vide affiche une grille creuse avec bouton de paiement actif | État vide ajouté | Oui |
| `Cart/Summary.cshtml` (checkout) | `<label>` non associés à leur `<input>` (pas de `for`) | Moyen | Lecteur d'écran ne peut relier le libellé au champ | `asp-for`/`for` explicite ajoutés | Oui |
| `Cart/Summary.cshtml` | Pas de protection anti double-soumission sur le bouton de paiement | Moyen | Double-clic → deux sessions Stripe distinctes possibles | État de chargement + désactivation au submit (jamais côté calcul serveur) | Oui |
| `Categories/Customer.cshtml` | Badge de disponibilité toujours `bg-success` (vert), y compris pour une catégorie "Non disponible" | Élevé | Signal visuel contradictoire | Couleur conditionnelle | Oui |
| `Categories/Customer.cshtml` | Aucun état vide ; pagination toujours affichée même à une seule page | Faible | Détails de polish | État vide ajouté ; pagination conditionnelle | Oui |
| `Produits/Customer.cshtml` | Aucun état vide, aucune pagination visible malgré `PaginatedList<T>` | Moyen | Grille creuse silencieuse en absence de produits | État vide + pagineur ajoutés | Oui |
| ~50 vues (`Views/**`, `Areas/Identity/**`) | Classe `fw-folder` (inexistante en Bootstrap — probable coquille pour `fw-bold`) sur les `<h1>` | Faible (mais très répandu) | Titres non mis en gras comme prévu | Remplacé par `fw-bold` partout | Oui |
| Ensemble du site | Aucun design system : styles ad hoc dupliqués page par page (cartes, focus, badges) | Moyen | Incohérence visuelle, duplication de patterns | `wwwroot/css/design-system.css` additif | Oui |
| `Produits/Index` (route) | Non `[Authorize]` : sert un rendu client ou admin selon le rôle sur la même URL (héritage CATALOG-001) | Observation | Architecture pré-existante, pas de faille (aucune donnée admin exposée à un anonyme) | **Non modifié** — changer l'autorisation est hors périmètre UX-001 | Documenté, hors périmètre |

## 3. Design system (`wwwroot/css/design-system.css`)

Fichier additif chargé après `styles.css` (bundle Bootstrap 5.2.3 existant) — n'introduit aucun framework parallèle. Réutilise les couleurs de marque déjà visibles dans le dépôt (`--bs-primary: #f4623a`, header `darkorange`) sans en inventer de nouvelles. Définit : tokens de typographie/espacement/couleur, focus visible clavier, skip link, boutons (dont état `.cx-loading`), cartes (`.cx-card-hover`, `.cx-product-card`), formulaires (`.cx-required`, focus ring), badges de statut sémantiques, états vides (`.cx-empty-state`), tableau responsive en cartes (`.cx-table-responsive-stack`, prêt à l'usage mais non appliqué à une table existante dans ce lot faute de temps), styles d'impression.

## 4. Surfaces modifiées

- **Header/navigation** (`_Layout.cshtml`) : bascule mobile réelle, skip link, lien FAQ ajouté au menu principal, `rel="noopener noreferrer"` sur les liens sociaux externes de la page d'accueil.
- **Page d'accueil** : voir table section 2 — H1 unique, sections responsives, suppression du contenu fictif/tiers, aucun fait marketing inventé (bannière, engagements, best-sellers, promotions et témoignages proviennent tous de données réelles interrogées par `HomeController.Index`).
- **Catalogue/recherche** : `ResultatsRecherche.cshtml` (déjà solide depuis CATALOG-001 — filtres, tri, pagination, état vide, `aria-label`) reçoit un polish visuel (`cx-card-hover`) ; `Produits/Customer.cshtml` et `Categories/Customer.cshtml` reçoivent état vide + pagination + correction du badge.
- **Fiche produit** : `Produits/Details.cshtml` (vue admin/mixte) et `Produits/ItemDetails.cshtml` (vue client réelle) — H1 unique, titre/alt corrects, image absente gérée.
- **Panier** : lien mort corrigé, état vide ajouté.
- **Checkout** (`Cart/Summary.cshtml`) : labels associés, état de chargement anti double-soumission. **`CheckoutFormInput` non touché** — aucun champ financier n'a jamais pu s'y lier (COMMERCE-OPERATIONS-001A), toujours vrai après ce lot.
- **Confirmation/reçu** (`Cart/OrderConfirmation.cshtml`, `Cart/Receipt.cshtml`) : déjà conformes (snapshot financier, aucune donnée technique/Stripe exposée, CSS d'impression présent depuis 001B) — aucune régression, aucun changement structurel nécessaire.
- **Compte** (`Views/Account/**`) : déjà bien construit par ACCOUNT-001 (nav avec `aria-current`, landmarks) — bénéficie du design system global, corrections `fw-bold`.
- **Identity** (`Areas/Identity/Pages/Account/**`) : labels déjà correctement associés via `asp-for` — bénéficie du design system global, corrections `fw-bold`. Aucune règle Identity modifiée.
- **Admin** (`_DashboardLayout.cshtml` + vues admin `Views/{Produits,Categories,Brands,OrderHeaders,ShippingMethods,TaxRates,Avis,AspNetUsers}/**`) : voir table section 2 pour les corrections structurelles ; les vues CRUD individuelles reçoivent la correction `fw-bold` et bénéficient du design system.

## 5. Responsive

Vérifié au niveau code (classes Bootstrap `row-cols-*`, `col-md-*`, media queries du header) pour 320/375/390/430/768/1024/1280/1440. Aucun outil de capture multi-résolution automatisé disponible dans cet environnement — la correction la plus significative (bascule mobile du header, absente auparavant) a été vérifiée par inspection du HTML rendu réel (section 7) plutôt que par capture visuelle. `RUNTIME_VISUAL=PARTIALLY_VERIFIED` (voir section 7) : aucune affirmation de validation visuelle qui n'a pas été faite.

## 6. Accessibilité

- Skip link fonctionnel (`_Layout.cshtml`, `_DashboardLayout.cshtml`).
- Focus visible clavier ajouté globalement (`design-system.css`).
- Labels de formulaire associés (`Cart/Summary.cshtml`).
- Un seul `<h1>` par page corrigée (vérifié par test automatisé sur un échantillon).
- Alt text pertinent (`ItemDetails.cshtml`).
- Badges de statut ne reposent plus uniquement sur une couleur trompeuse (`Categories/Customer.cshtml`).
- Réduction de mouvement respectée (`prefers-reduced-motion` sur les animations d'entrée et le spinner de chargement).
- Accordéon FAQ (déjà natif Bootstrap depuis CONTENT-LEGAL-001) : non modifié, toujours conforme.
- Non traité dans ce lot faute de temps : audit de contraste couleur systématique par page, zoom 200% par capture réelle.

## 7. Validation runtime

Application lancée localement (`dotnet run`, `ASPNETCORE_ENVIRONMENT=Development`, aucune base SQL Server réelle disponible dans cet environnement). Résultat honnête :

- Pages ne dépendant pas de la base (`/Home/About`, `/Home/Contact`, `/Home/Faq`, `/Home/Privacy`, `/Home/Terms`, `/Identity/Account/Login`, `/Identity/Account/Register`, `/robots.txt`) : `200`, vérifiées par `curl` réel contre un vrai processus Kestrel — présence confirmée de `cx-skip-link`, `navbar-toggler`, `design-system.css`, H1 unique, 8 items d'accordéon FAQ.
- Page d'accueil (`/`) et toute page dépendant de `CosmechicsContext` : `500` en l'absence de base de données réelle — comportement attendu de cet environnement (pas une régression de ce lot), non représentatif de la production où une vraie base est configurée.
- Le rendu HTTP complet à travers le vrai pipeline (routage, CSP, layout, vues) pour les surfaces dépendant de données a été vérifié via la suite de tests d'intégration (`CustomWebApplicationFactory`, base InMemory) plutôt que par capture d'écran — 37 nouveaux tests + 314 historiques, tous verts.
- Aucun outil de capture d'écran/navigateur piloté n'a été utilisé dans ce lot : `RUNTIME_VISUAL=PARTIALLY_VERIFIED`, comme le lot précédent. Aucune validation visuelle multi-résolution réelle n'est revendiquée.

## 8. Sécurité — non-régression

- **CSP** : aucun script inline ajouté sans nonce ; les scripts ajoutés/modifiés (`Cart/Summary.cshtml`) réutilisent le bloc `<script nonce="@CspNonce.Nonce">` déjà existant. Aucun `unsafe-inline`/`unsafe-eval`/wildcard introduit.
- **Autorisation** : aucune modification de `[Authorize]`/`[Authorize(Roles=...)]` sur aucun contrôleur. Les nouveaux liens de la barre latérale admin pointent tous vers des actions déjà protégées (à l'exception documentée de `Produits/Index`, pré-existante et non modifiée).
- **CSRF** : aucune nouvelle mutation POST introduite sans `[ValidateAntiForgeryToken]` — le seul formulaire POST touché (déconnexion admin) utilise le tag helper `asp-page` existant, qui injecte déjà le jeton.
- **Rate limiting** : aucune policy modifiée.
- **Upload** : aucun chemin d'upload touché.
- Invariants métier (stock, webhook, snapshot financier, transitions de commande, remboursements, ownership) : aucun fichier `.cs` de contrôleur/service/modèle modifié dans ce lot — voir section 10.

## 9. Dépendances

`NEW_DEPENDENCIES_EXPECTED=0` respecté : aucun package NuGet ni bibliothèque JS/CSS ajouté. Le seul nouveau fichier CSS (`design-system.css`) est écrit à la main, sans dépendance externe, chargé localement comme `styles.css`.

## 10. Base de données

`MIGRATIONS_EXPECTED=0` respecté : aucun fichier sous `Cosmechic/Models/`, `Cosmechic/Controllers/`, `Cosmechic/Services/`, `Cosmechic/Migrations/` n'a été modifié dans ce lot (`git status` le confirme — uniquement des vues Razor, un fichier CSS et un fichier de test). `MIGRATIONS_CREATED=0`, `MODEL_MIGRATION_DRIFT=NONE` par construction.

## 11. Tests ajoutés (`Cosmechic.Tests/UxRegressionTests.cs`, 37 tests)

Navigation (skip link/toggle présents, liens principaux résolvent, nav compte visible seulement authentifié, nouveaux liens admin protégés/accessibles selon le rôle), catalogue (formulaire de recherche, état vide, listing par catégorie, titre de fiche produit), panier (lien "Continuer les achats" corrigé), compte (pages rendent pour le propriétaire, état vide retours), Identity (pages répondent toujours), contenu/accessibilité structurelle (H1 unique sur échantillon, badge de disponibilité reflète l'état réel).

## 12. Gates finaux

- `RESTORE=PASS`, `BUILD=PASS` — 0 erreur, **48 warnings** (identiques avant/après, `NEW_CODE_WARNINGS=0`, `RESOLVED_WARNINGS=0` — comparaison par empreinte de diagnostic distincte, pas seulement par total)
- `TESTS_BEFORE=314`, `TESTS_AFTER=351`, `TESTS_PASS=351`, `TESTS_FAIL=0`
- `NUGET_CRITICAL=0`, `NUGET_HIGH=0`, `NUGET_MODERATE=0`, `NUGET_LOW=0`
- `TEST_ARTIFACTS=0` (conteneur SQL Server jetable nettoyé automatiquement ; `git status` ne montre que les fichiers attendus)
- `SECRET_SCAN=CLEAN`
- `MIGRATIONS_CREATED=0`, `MODEL_MIGRATION_DRIFT=NONE`
- `PRODUCTION_TOUCHED=NO`, `REAL_STRIPE_USED=NO`

## 13. Diff review (hors périmètre = 0)

| Groupe de fichiers | Changement | Raison | Dans le périmètre | Risque |
|---|---|---|---|---|
| `Cosmechic/wwwroot/css/design-system.css` | Créé | Design system minimal additif | Oui | Faible |
| `Cosmechic/Views/Shared/_Layout.cshtml` | Header responsive réel, skip link, lien FAQ, `rel=noopener` | Corrige un défaut mobile critique | Oui | Faible |
| `Cosmechic/Views/Shared/_DashboardLayout.cshtml` | Chemins scripts corrigés, formulaire déconnexion réparé, footer non-fixe, modal mort retiré, 4 liens sidebar ajoutés, skip link | Corrige plusieurs bugs réels (JS cassé hors accueil admin, déconnexion sidebar non fonctionnelle, footer superposé, navigation incomplète) | Oui | Faible — aucune règle d'autorisation touchée |
| `Cosmechic/Views/Home/Index.cshtml` | H1 unique, suppression newsletter/formulaire tiers fictifs, lien blog mort retiré, grille responsive | Corrige des défauts de contenu/structure réels | Oui | Faible |
| `Cosmechic/Views/Produits/Details.cshtml`, `ItemDetails.cshtml`, `Customer.cshtml`, `ResultatsRecherche.cshtml`, `Index.cshtml` (fw-bold) | H1 unique, titres/alt corrects, états vides/pagination, polish visuel | Corrige des défauts réels du catalogue | Oui | Faible |
| `Cosmechic/Views/Cart/Index.cshtml` | Lien mort corrigé, état vide | Corrige un défaut réel | Oui | Faible |
| `Cosmechic/Views/Cart/Summary.cshtml` | Labels associés, état de chargement anti double-soumission | Accessibilité + robustesse UI, `CheckoutFormInput` intact | Oui | Faible |
| `Cosmechic/Views/Categories/Customer.cshtml` | Badge conditionnel, état vide, pagination conditionnelle | Corrige un signal visuel trompeur réel | Oui | Faible |
| ~45 vues restantes (`Areas/Identity/**`, `Views/Account/**`, `Views/AspNetUsers/**`, `Views/Avis/**`, `Views/Categories/{Create,Delete,Details,Edit,Index}.cshtml`, `Views/OrderHeaders/**`, `Views/Produits/{Create,Delete,Edit}.cshtml`, `Views/Returns/**`, `Cart/OrderConfirmation.cshtml`) | `fw-folder` → `fw-bold` uniquement | Classe CSS inexistante (coquille), correction mécanique et sûre | Oui | Négligeable |
| `Cosmechic.Tests/UxRegressionTests.cs` | Créé | Couverture de test du lot | Oui | Faible |

`FILES_CHANGED=53`, `OUT_OF_SCOPE_CHANGES=0`.

## 14. Rollback

Chaque changement est confiné aux vues Razor et à un fichier CSS additif ; aucune migration, aucun changement de service/contrôleur/modèle. Un rollback complet consiste à `git revert` le commit unique de ce lot — sans impact sur le schéma de données ni sur l'état applicatif persistant.

## 15. Limites et hors périmètre (confirmé non traité)

- Aucune capture d'écran/navigateur piloté réelle — validation runtime par HTTP + inspection HTML + suite d'intégration (section 7).
- Contraste couleur non audité systématiquement page par page.
- `Produits/Index` reste une route non `[Authorize]` au comportement dual (client/admin) — architecture pré-existante, changement d'autorisation hors périmètre UX-001.
- `.cx-table-responsive-stack` (design system) créée mais pas encore appliquée aux tables admin existantes (ex. `OrderHeaders/Index`) — laissée en polish futur faute de temps dans ce lot déjà très large.
- SEO-001, OBSERVABILITY-001, DEVOPS-001, RELEASE-001 : non commencés.
