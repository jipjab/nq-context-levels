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

Le profil du jour (`vPOC` / `VAH` / `VAL`) n'est **volontairement pas tracé** : le Volume Profile & TPO
natif le fait mieux. Les deux indicateurs sont faits pour cohabiter.

Aucun signal, aucune alerte, aucun ordre. L'indicateur dessine du contexte, la décision reste manuelle.

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

La description affichée dans l'onglet *About* **n'est pas alimentable depuis le code** :
elle provient du catalogue serveur d'ATAS, associée à un module enregistré dans le Personal Area
(cf. [Indicators and strategies distribution](https://docs.atas.net/en/md_DataFeedsCore_2Docs_2en_20140__IndicatorsStrategiesDistribution.html)).
Ni `[Description]`, ni `AssemblyDescription` ne sont lus.

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
