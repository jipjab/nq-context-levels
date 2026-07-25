using System;

namespace NqContextLevels;

/// <summary>
/// Vide de cotation entre deux bougies consécutives : une plage de prix où rien ne s'est échangé.
/// Détecté sur les extrêmes et non sur ouverture/clôture — c'est ce qui garantit un vide
/// réellement visible à l'écran, sur un contrat qui cote presque 24h.
/// </summary>
internal sealed class PriceGap
{
    public PriceGap(decimal low, decimal high, bool isUp, int bar, DateTime time)
    {
        if (high <= low)
            throw new ArgumentException("Un gap suppose une plage de largeur strictement positive.", nameof(high));

        Low = low;
        High = high;
        IsUp = isUp;
        Bar = bar;
        Time = time;
    }

    public decimal Low { get; }

    public decimal High { get; }

    /// <summary>Vrai si le marché a sauté vers le haut.</summary>
    public bool IsUp { get; }

    /// <summary>Bougie qui a ouvert le vide : origine du tracé.</summary>
    public int Bar { get; }

    /// <summary>Horodatage de cette bougie, pour l'étiquette.</summary>
    public DateTime Time { get; }

    public decimal Size => High - Low;

    public decimal Mid => (Low + High) / 2m;

    /// <summary>
    /// Bord opposé au sens du saut. C'est lui qu'il faut atteindre pour parler de comblement :
    /// entrer dans la plage ne suffit pas.
    /// </summary>
    public decimal FillLevel => IsUp ? Low : High;

    public bool IsFilledBy(decimal low, decimal high)
        => IsUp ? low <= FillLevel : high >= FillLevel;

    /// <summary>
    /// Détecte un vide entre deux bougies consécutives. Retourne null si les amplitudes
    /// se chevauchent, ou si l'écart n'atteint pas la taille minimale.
    /// </summary>
    public static PriceGap Detect(
        decimal previousHigh, decimal previousLow,
        decimal currentHigh, decimal currentLow,
        int bar, DateTime time, decimal minSize)
    {
        if (currentLow - previousHigh >= minSize)
            return new PriceGap(previousHigh, currentLow, isUp: true, bar, time);

        if (previousLow - currentHigh >= minSize)
            return new PriceGap(currentHigh, previousLow, isUp: false, bar, time);

        return null;
    }
}
