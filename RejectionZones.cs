using System;
using System.Collections.Generic;
using System.Linq;

namespace NqContextLevels;

/// <summary>
/// Un rejet détecté sur une bougie : le prix extrême atteint, et de quel côté il a été repoussé.
/// </summary>
internal readonly struct Rejection
{
    public Rejection(decimal price, bool isResistance, int bar)
    {
        Price = price;
        IsResistance = isResistance;
        Bar = bar;
    }

    /// <summary>Extrême de la mèche : le plus haut pour une résistance, le plus bas pour un support.</summary>
    public decimal Price { get; }

    /// <summary>Vrai si le prix a été repoussé par le haut.</summary>
    public bool IsResistance { get; }

    public int Bar { get; }
}

/// <summary>
/// Une bande de prix ayant repoussé le marché plusieurs fois.
/// </summary>
internal sealed class RejectionZone
{
    public decimal Low { get; set; }
    public decimal High { get; set; }
    public int ResistanceCount { get; set; }
    public int SupportCount { get; set; }

    /// <summary>Bougie du premier rejet du regroupement.</summary>
    public int FirstBar { get; set; }

    /// <summary>Bougie du dernier rejet : c'est le dernier contact connu de la zone.</summary>
    public int LastBar { get; set; }

    public int Total => ResistanceCount + SupportCount;

    /// <summary>Zone ayant servi dans les deux sens — un flip.</summary>
    public bool IsFlip => ResistanceCount > 0 && SupportCount > 0;

    public decimal Mid => (Low + High) / 2m;

    public string Label => IsFlip
        ? $"S/R×{Total}"
        : ResistanceCount > 0 ? $"R×{ResistanceCount}" : $"S×{SupportCount}";
}

internal static class RejectionZoneBuilder
{
    /// <summary>
    /// Détecte un rejet sur une bougie : une mèche représentant au moins <paramref name="minWickRatio"/>
    /// de l'amplitude totale. Une bougie peut produire un rejet de chaque côté.
    /// </summary>
    public static void Detect(
        decimal open, decimal high, decimal low, decimal close,
        int bar, decimal minWickRatio, ICollection<Rejection> output)
    {
        var range = high - low;

        if (range <= 0m)
            return;

        var bodyTop = Math.Max(open, close);
        var bodyBottom = Math.Min(open, close);

        if ((high - bodyTop) / range >= minWickRatio)
            output.Add(new Rejection(high, isResistance: true, bar));

        if ((bodyBottom - low) / range >= minWickRatio)
            output.Add(new Rejection(low, isResistance: false, bar));
    }

    /// <summary>
    /// Regroupe les rejets en bandes. Parcours glouton sur les prix triés : un rejet rejoint
    /// la bande courante tant qu'il reste à moins de <paramref name="tolerance"/> de sa base.
    /// Cette borne sur la base — et non sur le dernier élément — empêche une bande de dériver
    /// indéfiniment par effet de chaîne.
    /// </summary>
    public static IReadOnlyList<RejectionZone> Build(
        IReadOnlyList<Rejection> rejections, decimal tolerance, int minTouches)
    {
        if (tolerance <= 0m)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "La tolérance doit être strictement positive.");

        if (minTouches < 2)
            throw new ArgumentOutOfRangeException(nameof(minTouches), "Une zone demande au moins deux tests.");

        var zones = new List<RejectionZone>();

        if (rejections.Count == 0)
            return zones;

        RejectionZone current = null;

        foreach (var rejection in rejections.OrderBy(r => r.Price))
        {
            if (current == null || rejection.Price - current.Low > tolerance)
            {
                current = new RejectionZone
                {
                    Low = rejection.Price,
                    High = rejection.Price,
                    FirstBar = rejection.Bar,
                    LastBar = rejection.Bar
                };

                zones.Add(current);
            }
            else
            {
                if (rejection.Price > current.High)
                    current.High = rejection.Price;

                if (rejection.Bar < current.FirstBar)
                    current.FirstBar = rejection.Bar;

                if (rejection.Bar > current.LastBar)
                    current.LastBar = rejection.Bar;
            }

            if (rejection.IsResistance)
                current.ResistanceCount++;
            else
                current.SupportCount++;
        }

        return zones.Where(z => z.Total >= minTouches).ToList();
    }
}
