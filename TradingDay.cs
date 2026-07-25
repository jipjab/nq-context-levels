using System;

namespace NqContextLevels;

/// <summary>
/// Une plage de prix accumulée sur une fenêtre de session.
/// </summary>
internal sealed class PriceRange
{
    public decimal High { get; private set; } = decimal.MinValue;
    public decimal Low { get; private set; } = decimal.MaxValue;

    public bool HasData => High > decimal.MinValue;

    public void Add(decimal high, decimal low)
    {
        if (high > High)
            High = high;

        if (low < Low)
            Low = low;
    }
}

/// <summary>
/// Toutes les données de contexte d'une journée de trading (18:00 -> 17:00 heure marché).
/// </summary>
internal sealed class TradingDay
{
    public TradingDay(DateTime sessionDate, int firstBar, decimal binSize)
    {
        SessionDate = sessionDate;
        FirstBar = firstBar;
        LastBar = firstBar;
        Distribution = new VolumeProfile(binSize);
    }

    public DateTime SessionDate { get; }
    public int FirstBar { get; }
    public int LastBar { get; set; }

    public PriceRange Rth { get; } = new();
    public PriceRange Overnight { get; } = new();
    public PriceRange Asia { get; } = new();
    public PriceRange London { get; } = new();

    /// <summary>Distribution accumulée : RTH seule ou journée complète selon le réglage.</summary>
    public VolumeProfile Distribution { get; }

    /// <summary>Dernier prix traité en RTH — proxy du settlement.</summary>
    public decimal RthClose { get; set; }

    public bool HasRthData => Rth.HasData;

    /// <summary>POC de la journée, calculé une seule fois à sa clôture. Source des naked POC.</summary>
    public decimal? Poc { get; private set; }

    public void FinalizePoc()
    {
        if (Distribution.IsEmpty)
            return;

        Poc = Distribution.FindPoc();
    }
}

/// <summary>
/// Découpage du temps en fenêtres de session, robuste au passage de minuit.
/// Tout est exprimé en "minutes écoulées depuis l'ouverture Globex".
/// </summary>
internal static class SessionClock
{
    private const int MinutesPerDay = 1440;

    /// <summary>
    /// Minutes écoulées depuis <paramref name="anchor"/>, ramenées dans [0;1440[.
    /// </summary>
    public static int MinutesSince(TimeSpan time, TimeSpan anchor)
    {
        var minutes = (int)(time - anchor).TotalMinutes % MinutesPerDay;
        return minutes < 0 ? minutes + MinutesPerDay : minutes;
    }

    /// <summary>
    /// Date de session à laquelle appartient un horodatage marché.
    /// Tout ce qui suit l'ouverture Globex appartient à la session du lendemain.
    /// </summary>
    public static DateTime SessionDate(DateTime marketTime, TimeSpan globexOpen)
        => marketTime.TimeOfDay >= globexOpen
            ? marketTime.Date.AddDays(1)
            : marketTime.Date;

    /// <summary>
    /// Teste l'appartenance à une fenêtre [start;end[ exprimée en minutes depuis l'ancre.
    /// </summary>
    public static bool IsInWindow(int minutesSinceAnchor, int windowStart, int windowEnd)
        => minutesSinceAnchor >= windowStart && minutesSinceAnchor < windowEnd;
}
