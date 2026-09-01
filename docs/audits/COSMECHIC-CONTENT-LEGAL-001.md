# COSMECHIC-CONTENT-LEGAL-001 — Contenu institutionnel, légal et de confiance

## 1. Recertification de la baseline

- `EXPECTED_HEAD` fourni par le mandat : `fcdf48a08e571d193846ef4bf7d145a12a270090`.
- HEAD local au démarrage de ce lot : `fcdf48a08e571d193846ef4bf7d145a12a270090` — **identique**. `PREFLIGHT=OK`.
- `git status` au démarrage : worktree propre.
- Branche : `claude/cosmechic-full-audit-0up8zj`. `git rev-list --left-right --count origin/main...HEAD` : 11 commits d'avance, 0 de retard sur `origin/main` (`7738e72`) — confirme que la chaîne validée n'existe que localement, comme signalé par l'utilisateur. **Aucun push n'a été effectué dans ce lot.**
- Chaîne de lots recertifiée via `git log --oneline --decorate --graph --all` : la lignée linéaire `fa9be09 → a337d09 → 59e01f5 → 060ae66 → 9c2c037 → 12bae6a → 1e2f460 → 62fbf38 → 6276e27 → 0d68dfb → fcdf48a` est intacte, avec un `docs/audits/*.md` correspondant à chacun des lots SECURITY-001, DATA-001, ECOM-CORE-001, IDENTITY-COMMS-001, SECURITY-002, CATALOG-001, COMMERCE-OPERATIONS-001A, COMMERCE-OPERATIONS-001B, COMMERCE-OPERATIONS-001B-CLOSURE-1 (fusionné dans le commit `0d68dfb`), ACCOUNT-001. `HISTORY_CHAIN_COMPLETE=YES`.
- Gates baseline avant modification : `RESTORE=PASS`, `BUILD=PASS` (48 warnings, 0 erreur), `TESTS=282/282 PASS`, `NUGET_CRITICAL/HIGH/MODERATE/LOW=0`.

## 2. Inventaire avant modification (aucune page dupliquée)

Audit de `Controllers/HomeController.cs`, `Views/Home/`, `Views/Shared/_Layout.cshtml`, `Program.cs` (routage conventionnel uniquement pour `Home`) avant toute création :

| Route | Vue | Contrôleur | Public/Auth | Statut avant | Écart constaté |
|---|---|---|---|---|---|
| `/Home/About` | `About.cshtml` | `HomeController.About` | Public | EXISTS_INCOMPLETE | Fondateur/date/équipe/statistiques fabriqués, 8 images référencées absentes de `wwwroot` |
| `/Home/Contact` | `Contact.cshtml` | `HomeController.Contact` | Public | BROKEN | Téléphone et horaires fabriqués, formulaire non fonctionnel (template Start Bootstrap, jeton API placeholder, bouton désactivé) |
| `/Home/Privacy` | `Privacy.cshtml` | `HomeController.Privacy` | Public | EXISTS_INCOMPLETE | Boilerplate générique, fausse mention d'usage marketing (aucun système marketing n'existe), domaine email fabriqué |
| `/Home/Terms` | `Terms.cshtml` | `HomeController.Terms` | Public | EXISTS_INCOMPLETE | Juridiction canadienne fabriquée, domaine email fabriqué |
| `/Home/Faq` | — | — | — | MISSING | — |
| `/Home/Shipping` | — | — | — | MISSING | — |
| `/Home/Returns` | — | — | — | MISSING | — |
| Footer (`_Layout.cshtml`) | — | — | — | BROKEN | 8 liens statiques morts (`/notrehistoire`, `/blog`, `/localisez-nous`...) et 3 liens "Catégorie 1/2/3" vers `#` |

**Conclusion** : aucune des pages requises n'était absente au sens strict — toutes existaient déjà sous forme non conforme. Ce lot **corrige en place** plutôt que de dupliquer, et **crée** seulement Faq/Shipping/Returns, réellement absentes.

## 3. Architecture de configuration ajoutée

Deux `IOptions<T>` minimales, nullable par défaut, centralisant les décisions métier encore ouvertes plutôt que de les disperser dans plusieurs vues :

- `BusinessInformationOptions` (`LegalBusinessName`, `BusinessAddress`, `TaxRegistrationNumbers`, `SupportEmail`, `SupportPhone`) — section `BusinessInformation` d'`appsettings.json`. Seul `SupportEmail` est renseigné, avec la valeur **déjà configurée et réellement utilisée** pour les emails transactionnels (`Smtp:FromAddress = equipe.cosmechic@gmail.com`, IDENTITY-COMMS-001) — jamais une valeur inventée.
- `CommercePolicyOptions` (`ReturnWindowDays`, `RefundShippingPolicy`, `RefundTaxPolicy`) — section `CommercePolicy`, entièrement vide/`null` : ce sont exactement les trois décisions laissées ouvertes par COMMERCE-OPERATIONS-001B.

Aucun CMS, aucune abstraction supplémentaire : ces deux classes remplacent uniquement des chaînes qui auraient sinon été dupliquées dans 2 à 4 vues chacune.

## 4. Pages publiques

### 4.1 About (`/Home/About`)
Réécrite pour ne reprendre que ce qui est démontré par `README.md` (positionnement Cosmechic — cosmétiques pour peaux/cheveux afro, noirs et métissés). Fondateur, date de fondation, équipe et 8 images inexistantes supprimés. Titre H1 unique, `ViewData["MetaDescription"]` défini.

### 4.2 Contact (`/Home/Contact`)
Formulaire réel : `ContactMessageInput` (DTO étroit : `Name`, `Email`, `Message` — aucun champ administratif, jamais lié à une entité), `[HttpPost][ValidateAntiForgeryToken][EnableRateLimiting("ContactForm")]`. Réutilise l'`IEmailSender` existant (`SmtpEmailSender`) — aucun `SmtpClient`/MailKit réintroduit dans le contrôleur. Comportement déterministe si `SupportEmail` n'est pas configuré ou si l'envoi échoue : message générique, aucune fuite de détail d'exception, jamais de page 500 (`ContentLegalPagesTests.ContactPost_EmailSenderThrows_ReturnsGenericErrorWithoutCrashing`).

### 4.3 Privacy (`/Home/Privacy`)
Voir matrice de données personnelles (section 6). Retire la fausse mention d'usage marketing, retire le domaine email fabriqué, ajoute la section Cookies (section 7) et une section "Vos droits" référençant l'export/la suppression de compte réels.

### 4.4 Terms (`/Home/Terms`)
Bandeau explicite : *« Ce document décrit le fonctionnement réel du site. Il n'a pas fait l'objet d'une révision juridique et ne constitue pas un contrat validé par un professionnel du droit. »* Sections factuelles uniquement (utilisation du site, comptes, commandes/paiement Stripe, disponibilité, expédition, retours, propriété intellectuelle). La juridiction fabriquée est remplacée par : *« La juridiction et le droit applicable à ces conditions n'ont pas encore été déterminés par l'entreprise. »* `LEGAL_REVIEW_REQUIRED=YES`.

### 4.5 Faq (`/Home/Faq`, nouvelle)
Accordéon Bootstrap natif (`data-bs-toggle="collapse"`, `aria-expanded`/`aria-controls` gérés par le composant — jamais de JS maison). 8 questions construites uniquement à partir de capacités réelles (compte, commandes, paiement Stripe, livraison, suivi, retour, remboursement, catalogue) ; aucun délai/politique inventé — chaque réponse sensible renvoie vers la page de politique correspondante plutôt que d'affirmer un chiffre.

### 4.6 Shipping (`/Home/Shipping`, nouvelle)
Reflète l'architecture réelle COMMERCE-OPERATIONS-001A : tableau des `ShippingMethod` actives (`Name`, `Price`, `FreeShippingThreshold`, `EstimatedMinDays`/`EstimatedMaxDays`), avec repli explicite « À déterminer » quand les délais estimés ne sont pas renseignés — jamais un délai inventé. Explique que le coût est calculé et affiché avant paiement, expédition Canada uniquement (fait démontré, pas une politique déclarée).

### 4.7 Returns (`/Home/Returns`, nouvelle)
Reflète les capacités réelles COMMERCE-OPERATIONS-001B (demande de retour, retours partiels, inspection, remboursement complet/partiel, suivi de statut). `CommercePolicyOptions.ReturnWindowDays`/`RefundShippingPolicy`/`RefundTaxPolicy` ne sont **jamais** inventés : repli explicite (« n'a pas encore été défini par l'entreprise ») quand non configurés — testé (`ReturnsPage_WithoutConfiguredPolicy_ShowsExplicitUndefinedFallback_NotFabricatedValue`).

## 5. Footer, navigation et checkout

- `_Layout.cshtml` : footer entièrement réécrit avec des liens `asp-controller`/`asp-action` (donc résolus au moment de la requête, jamais de chemin statique arbitraire) vers About/Contact/Catégories/FAQ/Livraison/Retours/Confidentialité/Conditions/Espace client/Commandes. Les anciens liens morts (`/notrehistoire`, `/blog`, `/localisez-nous`, "Catégorie 1/2/3") sont supprimés.
- `Views/Cart/Summary.cshtml` (checkout) : ajout de liens vers Livraison/Conditions/Confidentialité/Retours avant le bouton de paiement. Aucune case « J'accepte » ajoutée (non justifiée par ce lot). **Corrige au passage** une estimation de livraison fixe fabriquée (« 7 à 14 jours » codée en dur, indépendante de la méthode réellement choisie) trouvée dans le même bloc — remplacée par un renvoi vers la méthode sélectionnée et la politique de livraison réelle.

## 6. Inventaire des données personnelles (Privacy)

| Catégorie | Source | Finalité technique | Stockage | Visible utilisateur | Exportable | Supprimable | Rétention définie |
|---|---|---|---|---|---|---|---|
| Identité (email, mot de passe haché) | ASP.NET Identity | Authentification | `ApplicationDbContext` (SQL Server) | Oui (profil) | Export Identity natif (`DownloadPersonalData`) | Bloqué si historique de commandes | TODO_REQUIRES_BUSINESS_CONFIGURATION |
| Téléphone, adresses (`CustomerAddress`) | Compte client | Livraison | `CosmechicsContext` | Oui (Account/Addresses) | Non (hors périmètre Identity natif) | Oui, sauf si référencée par une commande historique | TODO_REQUIRES_BUSINESS_CONFIGURATION |
| Historique de commandes, statuts | Checkout/lifecycle | Exécution du contrat de vente | `CosmechicsContext` | Oui (Account/Orders) | Non | Non (intégrité historique) | TODO_REQUIRES_BUSINESS_CONFIGURATION |
| Retours, remboursements | Return/Refund workflow | Exécution du contrat de vente | `CosmechicsContext` | Oui (Account/Returns) | Non | Non | TODO_REQUIRES_BUSINESS_CONFIGURATION |
| Identifiants Stripe (session/paiement) | Stripe Checkout | Traitement du paiement | `CosmechicsContext` (identifiants seulement, jamais le numéro de carte) | Non (technique) | Non | Non | TODO_REQUIRES_BUSINESS_CONFIGURATION |
| Message du formulaire Contact | `HomeController.Contact` | Réponse au client | Non persisté (envoyé par email, jamais écrit en base) | N/A | N/A | N/A | N/A (aucun stockage) |
| Logs techniques | ASP.NET Core / `ILogger` | Diagnostic | Fichiers de logs / stdout | Non | Non | Non | TODO_REQUIRES_BUSINESS_CONFIGURATION |

`PERSONAL_DATA_EXPORT_SCOPE` et `ACCOUNT_DELETION_ANONYMIZATION_POLICY` restent `TODO_REQUIRES_BUSINESS_CONFIGURATION`, comme laissés ouverts par ACCOUNT-001 — **aucune modification** de ces politiques dans ce lot (décision hors périmètre, cf. section 9).

## 7. Cookies et tracking

| Cookie | Source | Finalité | Essentiel | Durée connue | Consentement technique requis |
|---|---|---|---|---|---|
| `.AspNetCore.Identity.Application` | ASP.NET Identity | Session authentifiée | Oui | Session/persistant selon "se souvenir de moi" | Non (essentiel) |
| `.AspNetCore.Antiforgery.*` | ASP.NET Core | Protection CSRF | Oui | Session | Non (essentiel) |
| Cookie de session (panier) | `app.UseSession()` | Panier non authentifié | Oui | Session | Non (essentiel) |

Audit du CSP (`Program.cs`) et du code : aucun Google Analytics, Facebook Pixel, ou tout autre traceur tiers non essentiel n'est présent. `NON_ESSENTIAL_TRACKERS=NONE`. Conformément au mandat, aucun CMP n'est construit — seule la section Cookies de la page Privacy documente ces trois cookies essentiels.

## 8. Audit des allégations cosmétiques (produits)

Recherche de motifs médicaux/thérapeutiques (`guéri`, `cure`, `heal`, `medical`, `dermatolog`, `clinically proven`, `hypoallerg`, `chemical/toxin free`, etc.) dans `Views/**/*.cshtml` et dans les données de seed (`CommerceSeedService.cs`, `TestDataSeeder.cs`) : **aucune occurrence** (un seul faux positif — « traiter vos commandes » dans `Privacy.cshtml`, sens administratif, non cosmétique). Les fiches produit réelles sont du contenu saisi en base par un administrateur, hors du dépôt de code — non auditable statiquement. `CLAIM_REVIEW_REQUIRED=NOT_APPLICABLE_TO_REPO_CONTENT` ; recommandation : une revue de contenu périodique côté administration reste nécessaire, hors périmètre technique de ce lot.

## 9. Export de données personnelles et suppression de compte

Aucune modification apportée à l'export Identity natif (`DownloadPersonalData`) ni à la règle de blocage de suppression de compte en présence d'un historique de commandes (ACCOUNT-001). L'extension de l'export aux données applicatives (adresses, commandes, retours) nécessiterait une décision de rétention encore absente (`PERSONAL_DATA_EXPORT_SCOPE`) — implémenter maintenant risquerait d'exporter des données dont la durée de conservation légale n'est pas déterminée. **Décision : différé**, documenté ici plutôt qu'implémenté par défaut.

## 10. SEO et accessibilité

- Chaque page nouvelle/corrigée définit `ViewData["Title"]` et `ViewData["MetaDescription"]` ; `_Layout.cshtml` rend `<meta name="description">` conditionnellement. Aucun canonical/Open Graph ajouté (aucun support global existant trouvé).
- `wwwroot/robots.txt` ajouté : autorise le crawl public, exclut les routes `[Authorize]` (Account, Identity, Cart, OrderHeaders/Details/Operations, AspNetUsers, Returns, ShippingMethods, TaxRates, Brands). Aucun `sitemap.xml` ajouté : aucun domaine de production n'est configuré nulle part dans le dépôt (`appsettings*.json`, `Program.cs`) — en générer un fabriquerait un domaine. `PRODUCTION_DOMAIN=TODO_REQUIRES_BUSINESS_CONFIGURATION`.
- Accessibilité : accordéon FAQ natif Bootstrap (`aria-expanded`/`aria-controls`), landmarks `<section>`/`<h1>`/`<h2>` cohérents, libellés de formulaire explicites sur Contact, tableaux avec `<caption>`/`scope` sur Shipping.
- `RESPONSIVE`/`RUNTIME_VISUAL` : les nouvelles pages réutilisent les classes Bootstrap (`container`, `row`, `col-lg-*`) déjà validées ailleurs dans l'application ; aucune vérification visuelle multi-résolution réelle n'a été effectuée dans cet environnement (pas de navigateur piloté). `RUNTIME_VISUAL=PARTIALLY_VERIFIED`.

## 11. Sécurité

- Toute nouvelle mutation (`Contact` POST) : `[HttpPost]`, `[ValidateAntiForgeryToken]`, `[EnableRateLimiting("ContactForm")]` (5/min/IP, `FixedWindowRateLimiter`), DTO étroit, validation serveur (`ModelState`).
- Aucun `Html.Raw` sur donnée non fiable ; le corps HTML de l'email Contact est construit avec `WebUtility.HtmlEncode` explicite (pas de rendu Razor pour cet email).
- Aucune route d'administration exposée anonymement — revérifié pour `ShippingMethods`, `TaxRates`, `Brands` (`ContentLegalPagesTests.AdminOnlyRoutes_AreNotExposedAnonymously`).
- Aucun secret, identifiant SMTP/Stripe, PII au-delà du strict nécessaire dans les logs (`_logger.LogError` sur échec Contact ne journalise que l'exception technique, jamais le contenu du message).

## 12. Tests ajoutés (`Cosmechic.Tests/ContentLegalPagesTests.cs`, 32 tests)

- Pages publiques essentielles accessibles anonymement (200) — About/Contact/Faq/Shipping/Returns/Privacy/Terms/Index.
- Liens du footer résolvent tous en 200 ; liens de compte redirigent/rejettent un utilisateur anonyme (401 en environnement de test, cf. `TestAuthHandler`) sans jamais être un lien mort.
- Aucune route d'administration exposée anonymement.
- Checkout (`/Cart/Summary`) contient les liens vers Livraison/Retours/Conditions/Confidentialité.
- Contact : soumission valide envoie l'email et redirige ; soumission invalide ne redirige pas et n'envoie rien ; échec SMTP simulé reste déterministe (pas de 500, pas de fuite de détail) ; preuve structurelle (réflexion) que l'action est protégée par `[ValidateAntiForgeryToken]` et `[EnableRateLimiting]` ; dépassement du quota de rate limiting renvoie 429.
- Pages Retours/Conditions ne contiennent jamais de délai/juridiction fabriqués, seulement le repli explicite documenté.
- Titre + meta description présents sur un échantillon représentatif des nouvelles pages.

## 13. Gates finaux

- `RESTORE=PASS`
- `BUILD=PASS` — 0 erreur, **48 warnings** (identique à la baseline pré-lot ; `NEW_CODE_WARNINGS=0`)
- `TESTS_BEFORE=282`, `TESTS_AFTER=314`, `TESTS_PASS=314`, `TESTS_FAIL=0` (dont les 32 nouveaux tests de ce lot, tous verts ; suite SQL-Server-backed revalidée contre un conteneur SQL Server jetable, nettoyé automatiquement après exécution)
- `NUGET_CRITICAL=0`, `NUGET_HIGH=0`, `NUGET_MODERATE=0`, `NUGET_LOW=0`
- `TEST_ARTIFACTS=0` (aucun fichier hors périmètre laissé dans `wwwroot` ou ailleurs — `git status` ne montre que les 17 fichiers attendus)
- `SECRET_SCAN=CLEAN` (diff complet inspecté — aucun secret, seule une adresse email déjà réellement configurée)
- `MODEL_MIGRATION_DRIFT=NONE` (aucune migration créée dans ce lot — aucun changement de modèle)
- `PRODUCTION_TOUCHED=NO`, `REAL_STRIPE_USED=NO`

## 14. Revue du diff (hors périmètre = 0)

| Fichier | Changement | Raison | Dans le périmètre | Risque |
|---|---|---|---|---|
| `Cosmechic/Services/BusinessInformationOptions.cs` | Créé | Centraliser les données d'entreprise non fabriquées | Oui | Faible |
| `Cosmechic/Services/CommercePolicyOptions.cs` | Créé | Centraliser les 3 décisions retour/remboursement ouvertes | Oui | Faible |
| `Cosmechic/Models/ViewModels/ContactMessageInput.cs` | Créé | DTO étroit du formulaire Contact | Oui | Faible |
| `Cosmechic/Controllers/HomeController.cs` | Modifié | Titre/meta, action Contact POST, actions Faq/Shipping/Returns | Oui | Faible |
| `Cosmechic/Program.cs` | Modifié | Binding des options, policy de rate limiting `ContactForm` | Oui | Faible |
| `Cosmechic/appsettings.json` | Modifié | Sections `BusinessInformation`/`CommercePolicy` | Oui | Faible |
| `Cosmechic/Views/Home/About.cshtml` | Réécrit | Suppression du contenu fabriqué | Oui | Faible |
| `Cosmechic/Views/Home/Contact.cshtml` | Réécrit | Formulaire réel | Oui | Faible |
| `Cosmechic/Views/Home/Privacy.cshtml` | Réécrit | Inventaire de données réel | Oui | Faible |
| `Cosmechic/Views/Home/Terms.cshtml` | Réécrit | Suppression de la juridiction fabriquée | Oui | Faible |
| `Cosmechic/Views/Home/Faq.cshtml` | Créé | Page manquante requise | Oui | Faible |
| `Cosmechic/Views/Home/Shipping.cshtml` | Créé | Page manquante requise | Oui | Faible |
| `Cosmechic/Views/Home/Returns.cshtml` | Créé | Page manquante requise | Oui | Faible |
| `Cosmechic/Views/Shared/_Layout.cshtml` | Modifié | Footer réel + meta description | Oui | Faible |
| `Cosmechic/Views/Cart/Summary.cshtml` | Modifié | Liens de politiques avant paiement + suppression d'un délai fabriqué | Oui | Faible |
| `Cosmechic/wwwroot/robots.txt` | Créé | Guidance de crawl best-effort | Oui | Faible |
| `Cosmechic.Tests/ContentLegalPagesTests.cs` | Créé | Couverture de test du lot | Oui | Faible |

`OUT_OF_SCOPE_CHANGES=0`.

## 15. Décisions métier non résolues (`TODO_REQUIRES_BUSINESS_CONFIGURATION`)

| Clé | Statut actuel | Requis avant production | Propriétaire |
|---|---|---|---|
| `RETURN_WINDOW_DAYS` | Non défini (`CommercePolicy:ReturnWindowDays = null`) | Oui | Métier |
| `REFUND_SHIPPING_POLICY` | Non défini | Oui | Métier |
| `REFUND_TAX_POLICY` | Non défini | Oui | Métier |
| `INVOICE_LEGAL_TAX_INFO` | Non défini (hérité de 001B) | Oui | Métier/comptabilité |
| `ACCOUNT_DELETION_ANONYMIZATION_POLICY` | Non défini (hérité d'ACCOUNT-001) | Oui | Métier/juridique |
| `PERSONAL_DATA_EXPORT_SCOPE` | Non défini (hérité d'ACCOUNT-001) | Oui | Métier/juridique |
| `LEGAL_BUSINESS_NAME` / `BUSINESS_ADDRESS` / `TAX_REGISTRATION_NUMBERS` | Non définis (`BusinessInformationOptions`) | Oui | Métier/juridique |
| `CONTRACTUAL_JURISDICTION` | Non défini (Terms l'indique explicitement) | Oui | Juridique |
| `PRODUCTION_DOMAIN` | Non défini nulle part dans le dépôt | Oui (bloque `sitemap.xml`) | DevOps/métier |
| Révision juridique des CGU/CGV | Non effectuée (bandeau explicite sur la page Terms) | Oui | Juridique |

## 16. Hors périmètre (confirmé non traité)

- Aucune migration EF créée (aucun changement de modèle de données requis pour ce lot).
- Aucune modification de la politique de rétention ou de la règle de suppression de compte.
- Aucune extension de l'export de données personnelles au-delà de l'export Identity natif (différé, section 9).
- Aucun CMP/bandeau de consentement (aucun cookie non essentiel constaté).
- Aucune case « J'accepte » contractuelle ajoutée au checkout.
- UX-001, SEO-001, OBSERVABILITY-001, DEVOPS-001, RELEASE-001 : non commencés.
