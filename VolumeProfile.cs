using System;
using System.Collections.Generic;
using System.Linq;

namespace NqContextLevels;

/// <summary>
/// Accumulateur de poids par niveau de prix (binné).
/// Une seule responsabilité : stocker le poids par bin et en dériver le POC et les nœuds HVN / LVN.
/// Le calcul de value area a été retiré : VAH / VAL viennent du Volume Profile &amp; TPO natif.
/// </summary>
internal sealed class VolumeProfile
{
    private readonly Dictionary<decimal, decimal> _bins = new();
    private readonly decimal _binSize;

    public VolumeProfile(decimal binSize)
    {
        if (binSize <= 0m)
            throw new ArgumentOutOfRangeException(nameof(binSize), "La taille de bin doit être strictement positive.");

        _binSize = binSize;
    }

    public bool IsEmpty => _bins.Count == 0;

    public void Add(decimal price, decimal weight)
    {
        if (weight <= 0m)
            return;

        var bin = Math.Floor(price / _binSize) * _binSize;
        _bins.TryGetValue(bin, out var current);
        _bins[bin] = current + weight;
    }

    public void Merge(VolumeProfile other)
    {
        if (other._binSize != _binSize)
            throw new InvalidOperationException("Impossible de fusionner deux profils de bin différente.");

        foreach (var pair in other._bins)
        {
            _bins.TryGetValue(pair.Key, out var current);
            _bins[pair.Key] = current + pair.Value;
        }
    }

    /// <summary>
    /// Prix du bin le plus chargé. Sert de référence aux naked POC.
    /// </summary>
    public decimal FindPoc()
    {
        if (IsEmpty)
            throw new InvalidOperationException("Profil vide : FindPoc ne doit pas être appelé.");

        var poc = _bins.First();

        foreach (var pair in _bins)
        {
            if (pair.Value > poc.Value)
                poc = pair;
        }

        return poc.Key;
    }

    /// <summary>
    /// Nœuds de haut volume : maxima locaux du profil lissé au-dessus d'un seuil
    /// relatif au bin le plus chargé. Zones d'aimantation et de rotation.
    /// </summary>
    public IReadOnlyList<decimal> FindHighVolumeNodes(int smoothing, decimal thresholdRatio, int minSeparationBins)
        => FindNodes(smoothing, thresholdRatio, minSeparationBins, isHigh: true);

    /// <summary>
    /// Nœuds de faible volume : minima locaux sous le seuil. Zones de traversée rapide —
    /// mauvaises zones d'entrée, bonnes zones de cible.
    /// </summary>
    public IReadOnlyList<decimal> FindLowVolumeNodes(int smoothing, decimal thresholdRatio, int minSeparationBins)
        => FindNodes(smoothing, thresholdRatio, minSeparationBins, isHigh: false);

    private IReadOnlyList<decimal> FindNodes(int smoothing, decimal thresholdRatio, int minSeparationBins, bool isHigh)
    {
        if (smoothing < 0)
            throw new ArgumentOutOfRangeException(nameof(smoothing), "Le lissage ne peut pas être négatif.");

        if (minSeparationBins < 1)
            throw new ArgumentOutOfRangeException(nameof(minSeparationBins), "La séparation minimale doit valoir au moins 1 bin.");

        var result = new List<decimal>();

        if (IsEmpty)
            return result;

        var ordered = _bins.OrderBy(p => p.Key).ToArray();

        if (ordered.Length < 3)
            return result;

        var smoothed = Smooth(ordered.Select(p => p.Value).ToArray(), smoothing);
        var peak = smoothed.Max();

        if (peak <= 0m)
            return result;

        var threshold = peak * thresholdRatio;
        var lastIndex = int.MinValue;

        for (var i = 1; i < smoothed.Length - 1; i++)
        {
            var isNode = isHigh
                ? smoothed[i] > smoothed[i - 1] && smoothed[i] >= smoothed[i + 1] && smoothed[i] >= threshold
                : smoothed[i] < smoothed[i - 1] && smoothed[i] <= smoothed[i + 1] && smoothed[i] <= threshold;

            if (!isNode)
                continue;

            if (i - lastIndex < minSeparationBins)
                continue;

            result.Add(ordered[i].Key);
            lastIndex = i;
        }

        return result;
    }

    private static decimal[] Smooth(decimal[] values, int radius)
    {
        if (radius == 0)
            return values;

        var smoothed = new decimal[values.Length];

        for (var i = 0; i < values.Length; i++)
        {
            var from = Math.Max(0, i - radius);
            var to = Math.Min(values.Length - 1, i + radius);
            var sum = 0m;

            for (var j = from; j <= to; j++)
                sum += values[j];

            smoothed[i] = sum / (to - from + 1);
        }

        return smoothed;
    }
}
