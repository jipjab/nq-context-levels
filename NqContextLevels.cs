using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using ATAS.Indicators;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Tools;
using CrossColor = System.Windows.Media.Color;
using CrossColors = System.Windows.Media.Colors;

namespace NqContextLevels;

/// <summary>
/// Grandeur accumulée par le profil. Reproduit les modes du Volume Profile &amp; TPO natif.
/// </summary>
public enum ProfileSource
{
    /// <summary>Volume échangé par niveau — mode par défaut du profil natif.</summary>
    Volume,

    /// <summary>Nombre de transactions par niveau.</summary>
    Ticks,

    /// <summary>Temps passé par niveau — c'est le mode TPO.</summary>
    Time
}

/// <summary>
/// Étendue temporelle du profil quotidien. Équivaut au réglage "External period"
/// du Volume Profile &amp; TPO natif.
/// </summary>
public enum ProfileScope
{
    /// <summary>Journée d'échange complète (Globex 18:00 -> 17:00). Équivaut à "Daily".</summary>
    FullSession,

    /// <summary>Session cash uniquement (09:30 -> 16:00).</summary>
    RegularHours
}

/// <summary>
/// Carte de contexte pré-market pour NQ : niveaux de session, profil de la veille,
/// naked POC et nœuds HVN/LVN d'un profil composite.
///
/// Toutes les fenêtres horaires sont exprimées dans l'heure du datafeed décalée
/// de <see cref="TimeOffsetHours"/>. Règle les valeurs par défaut sur l'heure de New York.
/// </summary>
// Préfixe plutôt que suffixe : dans une liste alphabétique de 278 indicateurs,
// tous les outils 6ITLab se regroupent en tête.
[DisplayName("6ITLab - NQ Context Levels")]
// "Perso" ne crée pas de section : la barre latérale d'ATAS a des catégories fixes.
// Custom est celle qui accueille les indicateurs utilisateur.
[Category("Custom")]
[Description(AboutText)]
[HelpLink(HelpUrl)]
[Logo(LogoUrl)]
public sealed class NqContextLevels : Indicator, IPropertiesEditorOwner
{
    /// <summary>
    /// Description courte affichée dans l'onglet About. Le panneau est étroit :
    /// deux phrases, comme les indicateurs natifs. Le détail est dans le README,
    /// atteignable via le lien HelpLink.
    /// </summary>
    private const string AboutText =
        "Complément du Volume Profile & TPO natif, pas un remplacement : ranges Asia / Londres / overnight, " +
        "extrêmes de la veille, naked POC multi-jours et nœuds HVN / LVN composites. " +
        "Régler le décalage horaire en premier : ONH / ONL doivent se figer à l'ouverture du cash.";

    /// <summary>
    /// Cible du lien « More details » de l'onglet About.
    /// ATAS n'accepte que https:// — une URL file:// produit un lien grisé.
    /// </summary>
    private const string HelpUrl = "https://jipjab.github.io/nq-context-levels/";

    /// <summary>
    /// Visuel de l'onglet About. LogoAttribute expose un GetLogoUri, donc il attend une URI —
    /// même hébergement que la page d'aide.
    /// </summary>
    private const string LogoUrl = "https://jipjab.github.io/nq-context-levels/logo.png";

    private const string GroupHelp = "① Lisez-moi";
    private const string GroupTimezone = "② Fuseau horaire";
    private const string GroupSessions = "Sessions";
    private const string GroupProfile = "Profil volume";
    private const string GroupNodes = "Nœuds HVN / LVN";
    private const string GroupNaked = "Naked POC";
    private const string GroupDisplay = "Affichage";
    private const string GroupLabels = "Textes des étiquettes";

    private readonly List<TradingDay> _days = new();
    private readonly List<NakedPoc> _nakedPocs = new();

    private IPropertiesEditor _propertiesEditor;

    private TradingDay _currentDay;
    private VolumeProfile _composite;
    private IReadOnlyList<decimal> _hvn = Array.Empty<decimal>();
    private IReadOnlyList<decimal> _lvn = Array.Empty<decimal>();
    private decimal _binSize;
    private int _nextBar;

    public NqContextLevels()
    {
        EnableCustomDrawing = true;
        SubscribeToDrawingEvents(DrawingLayouts.Final);
        DenyToChangePanel = true;
        DrawAbovePrice = false;

        ((ValueDataSeries)DataSeries[0]).VisualType = VisualMode.Hide;
        ((ValueDataSeries)DataSeries[0]).IsHidden = true;
    }

    #region Réglages — aide

    // L'onglet About n'est alimentable que par le catalogue serveur d'ATAS (FeatureId).
    // L'aide vit donc ici, où elle est visible sans quitter les réglages.

    [Display(Name = "À quoi ça sert", GroupName = GroupHelp, Order = 1)]
    [ReadOnly(true)]
    public string HelpPurpose { get; set; } =
        "Complément du Volume Profile & TPO natif : ranges Asia/Londres/overnight, extrêmes de la veille, naked POC multi-jours, nœuds HVN/LVN composites. Le profil du jour (vPOC/VAH/VAL) reste fourni par l'indicateur natif. Aucun signal ni ordre.";

    [Display(Name = "1. Décalage horaire", GroupName = GroupHelp, Order = 2)]
    [ReadOnly(true)]
    public string HelpTimezone { get; set; } =
        "À régler EN PREMIER, groupe suivant. Les fenêtres sont en heure de New York. Mauvais décalage = tous les niveaux faux, sans message d'erreur. Test : ONH/ONL doivent cesser d'évoluer pile à l'ouverture du cash, et LDN H/L se figer à 08:00.";

    [Display(Name = "2. Aligner sur Volume Profile & TPO", GroupName = GroupHelp, Order = 3)]
    [ReadOnly(true)]
    public string HelpAlignment { get; set; } =
        "Les naked POC et les nœuds HVN/LVN sont calculés en interne. Pour qu'ils raisonnent comme ton profil natif, recopier dans 'Profil volume' : External period → Étendue, Profile type → Base de calcul, Prices per row → Taille de bin.";

    [Display(Name = "3. Lisibilité", GroupName = GroupHelp, Order = 4)]
    [ReadOnly(true)]
    public string HelpReadability { get; set; } =
        "Groupe 'Affichage' : n'activer que la veille et l'overnight au départ. Groupe 'Textes' : vider un champ masque l'étiquette sans masquer la ligne.";

    [Display(Name = "Limites", GroupName = GroupHelp, Order = 5)]
    [ReadOnly(true)]
    public string HelpLimits { get; set; } =
        "La bougie en cours est exclue du calcul interne (retard d'une bougie). Sans données footprint, naked POC et nœuds n'apparaissent pas ; les niveaux de session, si. Jours fériés, demi-séances et heure d'été non gérés.";

    #endregion

    #region Réglages — fuseau

    [Display(Name = "Décalage horaire (heures)", GroupName = GroupTimezone, Order = 10,
        Description = "Décalage à appliquer à l'heure du datafeed pour obtenir l'heure de New York. 0 si ton datafeed est déjà en ET.")]
    public int TimeOffsetHours { get; set; }

    #endregion

    #region Réglages — sessions

    [Display(Name = "Ouverture Globex", GroupName = GroupSessions, Order = 20)]
    public TimeSpan GlobexOpen { get; set; } = new(18, 0, 0);

    [Display(Name = "Ouverture Londres", GroupName = GroupSessions, Order = 21)]
    public TimeSpan LondonOpen { get; set; } = new(3, 0, 0);

    [Display(Name = "Début pre-market NY", GroupName = GroupSessions, Order = 22)]
    public TimeSpan PremarketOpen { get; set; } = new(8, 0, 0);

    [Display(Name = "Ouverture cash NY", GroupName = GroupSessions, Order = 23)]
    public TimeSpan RthOpen { get; set; } = new(9, 30, 0);

    [Display(Name = "Clôture cash NY", GroupName = GroupSessions, Order = 24)]
    public TimeSpan RthClose { get; set; } = new(16, 0, 0);

    #endregion

    #region Réglages — profil

    [Display(Name = "Étendue du profil", GroupName = GroupProfile, Order = 28,
        Description = "Doit correspondre au réglage 'External period' du Volume Profile & TPO natif. Daily => Journée complète.")]
    public ProfileScope Scope { get; set; } = ProfileScope.FullSession;

    [Display(Name = "Base de calcul", GroupName = GroupProfile, Order = 29,
        Description = "Doit correspondre au réglage 'Calculation mode' du Volume Profile & TPO natif, sinon les POC divergeront.")]
    public ProfileSource Source { get; set; } = ProfileSource.Volume;

    [Display(Name = "Taille de bin (ticks)", GroupName = GroupProfile, Order = 31,
        Description = "Regroupement des prix. 4 ticks = 1 point sur NQ.")]
    [Range(1, 100)]
    public int BinTicks { get; set; } = 4;

    [Display(Name = "Jours du profil composite", GroupName = GroupProfile, Order = 32)]
    [Range(2, 60)]
    public int CompositeDays { get; set; } = 10;

    #endregion

    #region Réglages — nœuds

    [Display(Name = "Lissage (bins)", GroupName = GroupNodes, Order = 40)]
    [Range(0, 20)]
    public int Smoothing { get; set; } = 2;

    [Display(Name = "Seuil HVN (ratio du pic)", GroupName = GroupNodes, Order = 41)]
    [Range(0.1, 1.0)]
    public decimal HvnThreshold { get; set; } = 0.70m;

    [Display(Name = "Seuil LVN (ratio du pic)", GroupName = GroupNodes, Order = 42)]
    [Range(0.01, 0.9)]
    public decimal LvnThreshold { get; set; } = 0.25m;

    [Display(Name = "Séparation minimale (bins)", GroupName = GroupNodes, Order = 43,
        Description = "Évite d'empiler plusieurs nœuds côte à côte.")]
    [Range(1, 200)]
    public int MinSeparationBins { get; set; } = 5;

    #endregion

    #region Réglages — naked POC

    [Display(Name = "Nombre max de naked POC affichés", GroupName = GroupNaked, Order = 50,
        Description = "Les plus anciens sont abandonnés au-delà de cette limite.")]
    [Range(1, 120)]
    public int MaxNakedPocs { get; set; } = 20;

    #endregion

    #region Réglages — textes

    // Vider un champ masque l'étiquette correspondante sans masquer la ligne.

    [Display(Name = "Format du prix", GroupName = GroupLabels, Order = 100,
        Description = "Format .NET appliqué au prix. Exemples : 0.## | 0.00 | # ##0")]
    public string PriceFormat { get; set; } = "0.##";

    [Display(Name = "Afficher le prix", GroupName = GroupLabels, Order = 101)]
    public bool ShowPriceInLabel { get; set; } = true;

    [Display(Name = "Haut de la veille", GroupName = GroupLabels, Order = 110)]
    public string LabelPrevHigh { get; set; } = "PDH";

    [Display(Name = "Bas de la veille", GroupName = GroupLabels, Order = 111)]
    public string LabelPrevLow { get; set; } = "PDL";

    [Display(Name = "Clôture de la veille", GroupName = GroupLabels, Order = 112)]
    public string LabelPrevClose { get; set; } = "PDC";

    [Display(Name = "Haut overnight", GroupName = GroupLabels, Order = 116)]
    public string LabelOvernightHigh { get; set; } = "ONH";

    [Display(Name = "Bas overnight", GroupName = GroupLabels, Order = 117)]
    public string LabelOvernightLow { get; set; } = "ONL";

    [Display(Name = "Haut Asie", GroupName = GroupLabels, Order = 118)]
    public string LabelAsiaHigh { get; set; } = "ASIA H";

    [Display(Name = "Bas Asie", GroupName = GroupLabels, Order = 119)]
    public string LabelAsiaLow { get; set; } = "ASIA L";

    [Display(Name = "Haut Londres", GroupName = GroupLabels, Order = 120)]
    public string LabelLondonHigh { get; set; } = "LDN H";

    [Display(Name = "Bas Londres", GroupName = GroupLabels, Order = 121)]
    public string LabelLondonLow { get; set; } = "LDN L";

    [Display(Name = "Naked POC", GroupName = GroupLabels, Order = 124)]
    public string LabelNakedPoc { get; set; } = "nPOC";

    [Display(Name = "Nœud haut volume", GroupName = GroupLabels, Order = 125)]
    public string LabelHvn { get; set; } = "HVN";

    [Display(Name = "Nœud faible volume", GroupName = GroupLabels, Order = 126)]
    public string LabelLvn { get; set; } = "LVN";

    #endregion

    #region Réglages — affichage

    [Display(Name = "Veille (PDH/PDL/PDC)", GroupName = GroupDisplay, Order = 60)]
    public bool ShowPreviousDay { get; set; } = true;

    [Display(Name = "Overnight (ONH/ONL)", GroupName = GroupDisplay, Order = 61)]
    public bool ShowOvernight { get; set; } = true;

    [Display(Name = "Ranges Asie / Londres", GroupName = GroupDisplay, Order = 62)]
    public bool ShowSessionRanges { get; set; } = true;

    [Display(Name = "Naked POC", GroupName = GroupDisplay, Order = 64)]
    public bool ShowNakedPoc { get; set; } = true;

    [Display(Name = "Nœuds HVN / LVN", GroupName = GroupDisplay, Order = 65)]
    public bool ShowNodes { get; set; } = true;

    [Display(Name = "Étiquettes", GroupName = GroupDisplay, Order = 66)]
    public bool ShowLabels { get; set; } = true;

    [Display(Name = "Épaisseur des lignes", GroupName = GroupDisplay, Order = 67)]
    [Range(1, 5)]
    public int LineWidth { get; set; } = 1;

    [Display(Name = "Taille de police", GroupName = GroupDisplay, Order = 68)]
    [Range(6, 30)]
    public int FontSize { get; set; } = 10;

    [Display(Name = "Couleur veille", GroupName = GroupDisplay, Order = 70)]
    public CrossColor PreviousDayColor { get; set; } = CrossColors.SteelBlue;

    [Display(Name = "Couleur overnight", GroupName = GroupDisplay, Order = 72)]
    public CrossColor OvernightColor { get; set; } = CrossColors.MediumPurple;

    [Display(Name = "Couleur Asie", GroupName = GroupDisplay, Order = 73)]
    public CrossColor AsiaColor { get; set; } = CrossColors.DarkSeaGreen;

    [Display(Name = "Couleur Londres", GroupName = GroupDisplay, Order = 74)]
    public CrossColor LondonColor { get; set; } = CrossColors.IndianRed;

    [Display(Name = "Couleur naked POC", GroupName = GroupDisplay, Order = 76)]
    public CrossColor NakedPocColor { get; set; } = CrossColors.Orange;

    [Display(Name = "Couleur HVN", GroupName = GroupDisplay, Order = 77)]
    public CrossColor HvnColor { get; set; } = CrossColors.DarkCyan;

    [Display(Name = "Couleur LVN", GroupName = GroupDisplay, Order = 78)]
    public CrossColor LvnColor { get; set; } = CrossColors.Crimson;

    #endregion

    #region Calcul

    protected override void OnCalculate(int bar, decimal value)
    {
        if (bar == 0)
            Reset();

        // Le bar courant est recalculé à chaque tick : on ne fige un bar
        // qu'une fois le suivant apparu, sinon le profil compterait le volume plusieurs fois.
        while (_nextBar < bar)
        {
            ProcessBar(_nextBar);
            _nextBar++;
        }
    }

    private void Reset()
    {
        if (InstrumentInfo == null)
            throw new InvalidOperationException("InstrumentInfo indisponible : impossible de déterminer le TickSize.");

        if (InstrumentInfo.TickSize <= 0m)
            throw new InvalidOperationException($"TickSize invalide ({InstrumentInfo.TickSize}).");

        _binSize = InstrumentInfo.TickSize * BinTicks;
        _days.Clear();
        _nakedPocs.Clear();
        _currentDay = null;
        _composite = null;
        _hvn = Array.Empty<decimal>();
        _lvn = Array.Empty<decimal>();
        _nextBar = 0;
    }

    private void ProcessBar(int index)
    {
        var candle = GetCandle(index);
        var marketTime = candle.Time.AddHours(TimeOffsetHours);
        var sessionDate = SessionClock.SessionDate(marketTime, GlobexOpen);
        var minutes = SessionClock.MinutesSince(marketTime.TimeOfDay, GlobexOpen);

        if (_currentDay == null || _currentDay.SessionDate != sessionDate)
        {
            CloseCurrentDay();
            _currentDay = new TradingDay(sessionDate, index, _binSize);
            _days.Add(_currentDay);
            TrimHistory();
        }

        _currentDay.LastBar = index;

        TouchNakedPocs(candle.Low, candle.High);

        var londonStart = MinutesFromGlobex(LondonOpen);
        var premarketStart = MinutesFromGlobex(PremarketOpen);
        var rthStart = MinutesFromGlobex(RthOpen);
        var rthEnd = MinutesFromGlobex(RthClose);

        if (minutes < rthStart)
            _currentDay.Overnight.Add(candle.High, candle.Low);

        if (minutes < londonStart)
            _currentDay.Asia.Add(candle.High, candle.Low);

        if (SessionClock.IsInWindow(minutes, londonStart, premarketStart))
            _currentDay.London.Add(candle.High, candle.Low);

        var inRegularHours = SessionClock.IsInWindow(minutes, rthStart, rthEnd);

        // L'étendue du profil est indépendante des niveaux RTH : en mode "Journée complète"
        // on accumule sur tous les bars de la session, comme le profil natif en External period Daily.
        if (Scope == ProfileScope.FullSession || inRegularHours)
            AccumulateProfile(_currentDay.Distribution, candle);

        if (!inRegularHours)
            return;

        _currentDay.Rth.Add(candle.High, candle.Low);
        _currentDay.RthClose = candle.Close;
    }

    private int MinutesFromGlobex(TimeSpan time) => SessionClock.MinutesSince(time, GlobexOpen);

    private void AccumulateProfile(VolumeProfile profile, IndicatorCandle candle)
    {
        var levels = candle.GetAllPriceLevels();

        if (levels == null)
            return;

        foreach (var level in levels)
        {
            if (level == null)
                continue;

            profile.Add(level.Price, Weight(level));
        }
    }

    /// <summary>
    /// Poids attribué à un niveau de prix. Doit refléter le mode de calcul du profil natif :
    /// un POC "volume" et un POC "ticks" ne tombent pas au même prix.
    /// </summary>
    private decimal Weight(PriceVolumeInfo level) => Source switch
    {
        ProfileSource.Volume => level.Volume,
        ProfileSource.Ticks => level.Ticks,
        ProfileSource.Time => level.Time,
        _ => throw new ArgumentOutOfRangeException(nameof(Source), Source, "Base de calcul inconnue.")
    };

    private void CloseCurrentDay()
    {
        if (_currentDay == null)
            return;

        _currentDay.FinalizePoc();

        if (_currentDay.Poc.HasValue)
        {
            _nakedPocs.Add(new NakedPoc(_currentDay.Poc.Value, _currentDay.LastBar));
            RebuildComposite();
        }
    }

    private void TouchNakedPocs(decimal low, decimal high)
    {
        for (var i = _nakedPocs.Count - 1; i >= 0; i--)
        {
            if (_nakedPocs[i].Price >= low && _nakedPocs[i].Price <= high)
                _nakedPocs.RemoveAt(i);
        }

        if (_nakedPocs.Count > MaxNakedPocs)
            _nakedPocs.RemoveRange(0, _nakedPocs.Count - MaxNakedPocs);
    }

    private void RebuildComposite()
    {
        var completed = _days.Where(d => d.Poc.HasValue).ToList();

        if (completed.Count == 0)
            return;

        var window = completed.Skip(Math.Max(0, completed.Count - CompositeDays));
        var composite = new VolumeProfile(_binSize);

        foreach (var day in window)
            composite.Merge(day.Distribution);

        _composite = composite;

        if (composite.IsEmpty)
            return;

        _hvn = composite.FindHighVolumeNodes(Smoothing, HvnThreshold, MinSeparationBins);
        _lvn = composite.FindLowVolumeNodes(Smoothing, LvnThreshold, MinSeparationBins);
    }

    private void TrimHistory()
    {
        var keep = CompositeDays + 2;

        if (_days.Count > keep)
            _days.RemoveRange(0, _days.Count - keep);
    }

    #endregion

    #region Rendu

    protected override void OnRender(RenderContext context, DrawingLayouts layout)
    {
        if (ChartInfo == null || _days.Count == 0)
            return;

        var font = new RenderFont("Arial", FontSize);
        var current = _days[^1];
        var previous = _days.Count > 1 ? _days[^2] : null;

        if (ShowPreviousDay && previous != null && previous.HasRthData)
        {
            var from = previous.LastBar;
            DrawLevel(context, font, previous.Rth.High, from, LabelPrevHigh, PreviousDayColor);
            DrawLevel(context, font, previous.Rth.Low, from, LabelPrevLow, PreviousDayColor);
            DrawLevel(context, font, previous.RthClose, from, LabelPrevClose, PreviousDayColor);
        }

        if (ShowOvernight && current.Overnight.HasData)
        {
            DrawLevel(context, font, current.Overnight.High, current.FirstBar, LabelOvernightHigh, OvernightColor);
            DrawLevel(context, font, current.Overnight.Low, current.FirstBar, LabelOvernightLow, OvernightColor);
        }

        if (ShowSessionRanges)
        {
            if (current.Asia.HasData)
            {
                DrawLevel(context, font, current.Asia.High, current.FirstBar, LabelAsiaHigh, AsiaColor);
                DrawLevel(context, font, current.Asia.Low, current.FirstBar, LabelAsiaLow, AsiaColor);
            }

            if (current.London.HasData)
            {
                DrawLevel(context, font, current.London.High, current.FirstBar, LabelLondonHigh, LondonColor);
                DrawLevel(context, font, current.London.Low, current.FirstBar, LabelLondonLow, LondonColor);
            }
        }

        if (ShowNakedPoc)
        {
            foreach (var poc in _nakedPocs)
                DrawLevel(context, font, poc.Price, poc.FromBar, LabelNakedPoc, NakedPocColor);
        }

        if (!ShowNodes || _composite == null)
            return;

        var nodeStart = _days[0].FirstBar;

        foreach (var price in _hvn)
            DrawLevel(context, font, price, nodeStart, LabelHvn, HvnColor);

        foreach (var price in _lvn)
            DrawLevel(context, font, price, nodeStart, LabelLvn, LvnColor);
    }

    private void DrawLevel(RenderContext context, RenderFont font, decimal price, int fromBar, string label, CrossColor color)
    {
        var y = ChartInfo.GetYByPrice(price, false);

        if (y < 0 || y > ChartArea.Height)
            return;

        var x = ChartInfo.GetXByBar(fromBar, false);

        if (x >= ChartArea.Width)
            return;

        if (x < 0)
            x = 0;

        // L'éditeur de réglages d'ATAS travaille en System.Windows.Media.Color,
        // l'API de rendu en System.Drawing.Color : conversion ici, une seule fois.
        var renderColor = ToRenderColor(color);

        context.DrawLine(new RenderPen(renderColor, LineWidth), x, y, ChartArea.Width, y);

        if (!ShowLabels)
            return;

        var text = BuildLabel(label, price);

        if (text.Length == 0)
            return;

        var size = context.MeasureString(text, font);
        var textX = ChartArea.Width - (int)size.Width - 4;
        var textY = y - (int)size.Height - 1;

        context.DrawString(text, font, renderColor, textX, textY);
    }

    /// <summary>
    /// Compose l'étiquette. Un texte vide masque l'étiquette sans masquer la ligne ;
    /// désactiver le prix laisse le seul intitulé.
    /// </summary>
    private string BuildLabel(string label, decimal price)
    {
        var name = string.IsNullOrWhiteSpace(label) ? string.Empty : label.Trim();

        if (!ShowPriceInLabel)
            return name;

        var formatted = price.ToString(PriceFormat);

        return name.Length == 0 ? formatted : $"{name} {formatted}";
    }

    private static System.Drawing.Color ToRenderColor(CrossColor color)
        => System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    #endregion

    #region Éditeur de propriétés

    // Le groupe d'aide est replié à l'ouverture des réglages : consultable,
    // mais sans repousser les réglages utiles hors de l'écran.
    [Browsable(false)]
    IPropertiesEditor IPropertiesEditorOwner.PropertiesEditor
    {
        get => _propertiesEditor;
        set
        {
            if (_propertiesEditor == value)
                return;

            _propertiesEditor = value;
            CollapseHelpGroup(value);
        }
    }

    private static void CollapseHelpGroup(IPropertiesEditor editor)
    {
        if (editor == null)
            return;

        editor.BeginInit();
        editor.SetIsExpandedCategory(GroupHelp, false);
        editor.EndInit();
    }

    #endregion

    private readonly struct NakedPoc
    {
        public NakedPoc(decimal price, int fromBar)
        {
            Price = price;
            FromBar = fromBar;
        }

        public decimal Price { get; }
        public int FromBar { get; }
    }
}
