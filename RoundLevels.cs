using System;
using System.Collections.Generic;
using System.Linq;

namespace NqContextLevels;

/// <summary>
/// Un nombre rond ayant repoussé le prix suffisamment souvent pour mériter d'être tracé.
/// </summary>
internal sealed class RoundLevel
{
    public RoundLevel(decimal price, int firstBar)
    {
        Price = price;
        FirstBar = firstBar;
        LastBar = firstBar;
    }

    /// <summary>Le nombre rond exact — pas la moyenne des rejets qui l'ont validé.</summary>
    public decimal Price { get; }

    public int Count { get; private set; }

    public int FirstBar { get; }

    public int LastBar { get; private set; }

    public void Register(int bar)
    {
        Count++;

        if (bar > LastBar)
            LastBar = bar;
    }

    public string Label => $"{Price:0} ×{Count}";
}

internal static class RoundLevelBuilder
{
    /// <summary>
    /// Rattache chaque rejet au multiple de <paramref name="step"/> le plus proche, à condition
    /// qu'il en soit distant de moins de <paramref name="tolerance"/>. Les rejets isolés loin
    /// de tout nombre rond sont ignorés.
    ///
    /// Le sens du rejet n'est volontairement pas distingué : un nombre rond agit couramment
    /// comme support puis comme résistance, et l'inverse.
    /// </summary>
    public static IReadOnlyList<RoundLevel> Build(
        IReadOnlyList<Rejection> rejections, decimal step, decimal tolerance, int minTests)
    {
        if (step <= 0m)
            throw new ArgumentOutOfRangeException(nameof(step), "Le pas doit être strictement positif.");

        if (tolerance <= 0m)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "La tolérance doit être strictement positive.");

        if (minTests < 1)
            throw new ArgumentOutOfRangeException(nameof(minTests), "Il faut au moins un test.");

        var levels = new Dictionary<decimal, RoundLevel>();

        foreach (var rejection in rejections)
        {
            var nearest = Math.Round(rejection.Price / step, MidpointRounding.AwayFromZero) * step;

            if (Math.Abs(rejection.Price - nearest) > tolerance)
                continue;

            if (!levels.TryGetValue(nearest, out var level))
            {
                level = new RoundLevel(nearest, rejection.Bar);
                levels[nearest] = level;
            }

            level.Register(rejection.Bar);
        }

        return levels.Values
            .Where(l => l.Count >= minTests)
            .OrderBy(l => l.Price)
            .ToList();
    }
}
