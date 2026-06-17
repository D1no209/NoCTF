using NoCTF.Core;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// Implements CTFd-style dynamic scoring formula:
/// value = ((min - initial) / decay^2) * solves^2 + initial
/// </summary>
public class DynamicScoringCalculator
{
    /// <summary>
    /// Calculates the current point value for a challenge given the number of solves.
    /// </summary>
    /// <param name="solveCount">Number of teams that have solved the challenge (excluding admin/solver teams).</param>
    /// <param name="config">Points configuration for the challenge.</param>
    /// <returns>Current point value, clamped to [MinimumPoints, InitialPoints].</returns>
    public int Calculate(int solveCount, PointsConfig config)
    {
        if (solveCount <= 0)
            return config.InitialPoints;

        var initial = (double)config.InitialPoints;
        var min = (double)config.MinimumPoints;
        var decay = (double)config.DecayFactor;
        var solves = (double)solveCount;

        // CTFd formula: value = ((min - initial) / decay^2) * solves^2 + initial
        var value = ((min - initial) / (decay * decay)) * (solves * solves) + initial;

        // Clamp to [min, initial]
        var clamped = Math.Max(min, Math.Min(initial, value));
        return (int)Math.Round(clamped);
    }
}
