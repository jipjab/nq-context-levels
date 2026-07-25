# NQ Context Levels

Indicateur [ATAS X](https://atas.net/atas-x/) pour futures NQ — niveaux de contexte pré-market.
Conçu comme **complément** du Volume Profile & TPO natif, pas comme remplacement.

📖 **[Instructions complètes](https://jipjab.github.io/nq-context-levels/)**

---

## Ce qu'il trace

| Étiquette | Signification | Fenêtre (ET) |
|---|---|---|
| `PDH` / `PDL` | Haut et bas de la session cash de la veille | 09:30 – 16:00 |
| `PDC` | Clôture de la veille | 16:00 |
| `ONH` / `ONL` | Extrêmes de la session overnight | 18:00 – 09:30 |
| `ASIA H/L` | Range asiatique | 18:00 – 03:00 |
| `LDN H/L` | Range de Londres | 03:00 – 08:00 |
| `nPOC` | POC de journées antérieures jamais retouchés | multi-jours |
| `HVN` / `LVN` | Nœuds de haut et faible volume du profil composite | N jours |
| `R×n` / `S×n` / `S/R×n` | Zones ayant repoussé le prix n fois | N bougies |
| `GAP+` / `GAP-` | Vides de cotation non comblés | N gaps |

Le profil du jour (`vPOC` / `VAH` / `VAL`) n'est **volontairement pas tracé** : le Volume Profile & TPO
natif le fait mieux. Les deux indicateurs sont faits pour cohabiter.

Aucun signal, aucune alerte, aucun ordre. L'indicateur dessine du contexte, la décision reste manuelle.

### vPOC ou nPOC ?

Ils ne s'opposent pas : **un nPOC est un vPOC**, avec un statut particulier.

Le **vPOC** (*volume Point of Control*) est le prix où le plus gros volume s'est échangé sur une
période donnée. Chaque profil en a un, par définition. Le « v » le distingue du POC en mode TPO,
qui mesure le temps passé plutôt que le volume — les deux ne tombent pas au même prix.

Le **nPOC** (*naked POC*) est un vPOC d'une séance passée que le prix **n'est jamais revenu toucher
depuis**. C'est un statut, pas un calcul différent : dès que le prix le traverse, il disparaît.

| | vPOC | nPOC |
|---|---|---|
| Nature | Résultat d'un calcul | Statut d'un vPOC passé |
| Combien | Un par profil | Zéro à plusieurs, selon l'historique |
| Durée de vie | Figé à la clôture de sa période | Jusqu'au premier contact du prix |
| Qui le trace | Volume Profile & TPO natif | Cet indicateur |

Un POC marque une zone où beaucoup de contrats ont changé de mains — de la valeur acceptée. L'idée
est que le marché tend à revenir tester ces zones non revisitées, ce qui en ferait des aimants.

> **Nuance.** Cette propriété d'aimant est une croyance largement partagée dans la communauté Market
> Profile, **pas un fait établi statistiquement**. Mesure-la sur tes propres données — combien de
> nPOC ont été touchés, en combien de séances — avant d'en faire une cible de sortie.

Piège de vocabulaire : certaines plateformes appellent le naked POC « vPOC » pour *virgin POC*.
Même abréviation, sens opposé.

### Zones S/R — un compteur relatif

Une zone est un regroupement de **rejets par mèche** : une bougie dont la mèche représente au moins
la moitié de l'amplitude. Les prix extrêmes sont regroupés en bandes par parcours glouton — un rejet
rejoint la bande courante tant qu'il reste à moins de N ticks de sa **base**, et non du dernier
élément ajouté, ce qui empêche une bande de dériver par effet de chaîne.

L'étiquette porte le compteur : `R×3` (trois rejets par le haut), `S×2` (deux rebonds par le bas),
`S/R×5` (zone ayant servi des deux côtés — un flip).

> **Le compteur n'est pas une mesure absolue.** Il dépend directement de la tolérance de regroupement
> et du seuil de mèche. Élargis la tolérance et tes `R×3` deviennent des `R×8` — mêmes données, autre
> chiffre. C'est un indice de contexte, à lire comme tel.

### Gaps — vides de cotation réels

Sur un contrat qui cote presque 24h, un gap clôture-cash → ouverture-cash **n'est pas un vide
visuel** : les bougies overnight occupent l'espace. La détection porte donc sur les extrêmes de deux
bougies consécutives — un gap n'existe que si `bas[i] > haut[i-1]` ou `haut[i] < bas[i-1]`.

Conséquence : peu de gaps, mais tous réellement visibles à l'écran. Essentiellement l'ouverture du
dimanche, les reprises après le break de maintenance, et les réactions violentes aux publications.

Un gap est comblé par **traversée complète** — atteindre le bord opposé, pas simplement entrer dans
la plage. Les gaps comblés disparaissent.

## Prérequis

- ATAS X (Windows ou macOS)
- .NET SDK 10 — `brew install --cask dotnet-sdk`

## Compiler

```bash
dotnet build -c Release
cp bin/Release/net10.0-windows/NqContextLevels.dll ~/Library/Application\ Support/ATAS/Indicators/
```

Dans ATAS : fenêtre des indicateurs → catégorie **Custom** → `6ITLab - NQ Context Levels`.

> Après un rebuild, supprime l'instance posée sur le chart et rajoute-la : elle conserve
> sinon ses anciennes propriétés sérialisées et les nouveaux réglages n'apparaîtront pas.

### Adapter les chemins

Les `HintPath` du `.csproj` pointent vers `/Applications/ATAS X.app/Contents/MonoBundle/`.
Sous Windows, remplace par le dossier d'installation de la plateforme.

Vérifie aussi que le `TargetFramework` correspond au runtime de ta plateforme :

```bash
strings -a "/Applications/ATAS X.app/Contents/MonoBundle/ATAS.Indicators.dll" \
  | grep -o "\.NETCoreApp,Version=v[0-9.]*" | sort -u
```

## À faire en premier : le décalage horaire

Toutes les fenêtres de session sont en heure de New York. Si les bougies de ton datafeed sont
dans un autre fuseau, **chaque niveau sera faux, sans aucun message d'erreur.**

Test de validation : `PDH` et `PDL` doivent tomber exactement sur les extrêmes de la session cash
de la veille. Tant que ce n'est pas le cas, ne touche à aucun autre réglage.

---

## Notes de développement ATAS X

Quatre écarts entre la documentation officielle et la réalité du bundle macOS, constatés
au développement. Ils font gagner du temps si tu écris tes propres indicateurs.

**1. `OFT.Rendering.dll` est une référence obligatoire.**
Les exemples de la doc utilisent `RenderContext`, `RenderPen` et `RenderFont` sous un simple
`using ATAS.Indicators;`. Ces types vivent en réalité dans `OFT.Rendering.dll`, namespaces
`OFT.Rendering.Context` et `OFT.Rendering.Tools`.

**2. Deux types `Color` cohabitent.**
Les propriétés de réglage doivent être en `System.Windows.Media.Color` (alias `CrossColor`) —
seul type que l'éditeur de réglages sait afficher en sélecteur de couleur. Mais l'API de rendu
attend `System.Drawing.Color`. Une conversion explicite est nécessaire.

**3. `OFT.PlatformX.runtimeconfig.json` n'existe pas.**
La doc dit d'y lire le `tfm`. Le fichier est absent du bundle. Lire le TFM dans la DLL de
référence est plus fiable (commande ci-dessus).

**4. `Indicator` expose déjà `ValueAreaPercent`.**
Déclarer une propriété du même nom la masque silencieusement (`CS0108`).

### Compiler pour Windows depuis macOS

`CrossColor` est un alias de `System.Windows.Media.Color`, un type WPF requis **à la compilation**
même pour ATAS X. D'où `<UseWPF>true</UseWPF>` combiné à `<EnableWindowsTargeting>true</EnableWindowsTargeting>`,
qui autorise le SDK .NET à compiler une cible Windows depuis macOS. On ne fait que compiler —
ATAS X convertit les types Windows au chargement du DLL.

Contrainte : l'assembly ne doit contenir **aucun type UI WPF** (`Window`, `UserControl`, XAML),
sinon ATAS X rejette le DLL. Tout le rendu passe par `RenderContext`, le chemin cross-platform.

### Onglet About

**Ni la description ni l'image de l'onglet *About* ne sont alimentables depuis le code.** Les deux
proviennent du catalogue serveur d'ATAS, associées à un module enregistré dans le Personal Area
(cf. [Indicators and strategies distribution](https://docs.atas.net/en/md_DataFeedsCore_2Docs_2en_20140__IndicatorsStrategiesDistribution.html)).
Testés sans effet : `[Description]`, `AssemblyDescription`, et `[Logo]` d'`OFT.Attributes`.
Indice cohérent : aucun indicateur natif n'utilise `LogoAttribute` dans les assemblies livrées.

En revanche `[HelpLink]` d'`OFT.Attributes` fonctionne localement — c'est lui qui alimente le lien
« More details ». **Il n'accepte que des URL `https://`** ; une URL `file://` produit un lien grisé.

`[Category("Custom")]` fonctionne également et place l'indicateur dans la catégorie *Custom*
de la barre latérale.

## Limites connues

- La bougie en formation est exclue du calcul interne : son volume serait sinon compté à chaque tick. Les niveaux dérivés du profil accusent un retard d'une bougie.
- Les naked POC et les nœuds dépendent des données footprint. Sans historique tick suffisant, ils n'apparaissent pas — les niveaux de session, si.
- Jours fériés et demi-séances non gérés.
- Heure d'été ignorée : le décalage horaire est un entier fixe.
- Détection HVN/LVN heuristique (maxima et minima locaux sur profil lissé), pas statistique. Les seuils demandent un calage visuel.

## Licence

MIT — voir [LICENSE](LICENSE).

Cet outil ne constitue pas un conseil en investissement.
