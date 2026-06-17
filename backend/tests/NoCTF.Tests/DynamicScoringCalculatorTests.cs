using NoCTF.Plugins.CTF;
using NoCTF.Core;

namespace NoCTF.Tests;

public class DynamicScoringCalculatorTests
{
    // CTFd formula: value = ((min - initial) / decay^2) * solves^2 + initial
    // With defaults: initial=1000, min=100, decay=450

    private static readonly PointsConfig DefaultConfig = new(
        InitialPoints: 1000,
        MinimumPoints: 100,
        DecayFactor: 450
    );

    [Fact]
    public void Calculate_ZeroSolves_ReturnsInitialPoints()
    {
        var calc = new DynamicScoringCalculator();
        var result = calc.Calculate(0, DefaultConfig);
        Assert.Equal(1000, result);
    }

    [Fact]
    public void Calculate_AtDecayCount_ReturnsApproximatelyHalfway()
    {
        // At solves == decay, formula gives: ((min-initial)/decay^2)*decay^2 + initial = min-initial+initial = min
        // Wait: ((100-1000)/450^2)*450^2 + 1000 = (100-1000) + 1000 = 100
        var calc = new DynamicScoringCalculator();
        var result = calc.Calculate(450, DefaultConfig);
        Assert.Equal(100, result);
    }

    [Fact]
    public void Calculate_NeverBelowMinimum()
    {
        var calc = new DynamicScoringCalculator();
        // Very high solve count should still return at least minimum
        var result = calc.Calculate(10000, DefaultConfig);
        Assert.True(result >= DefaultConfig.MinimumPoints);
    }

    [Fact]
    public void Calculate_NeverAboveInitial()
    {
        var calc = new DynamicScoringCalculator();
        var result = calc.Calculate(0, DefaultConfig);
        Assert.True(result <= DefaultConfig.InitialPoints);
    }

    [Fact]
    public void Calculate_MonotonicallyDecreasing()
    {
        var calc = new DynamicScoringCalculator();
        var prev = calc.Calculate(0, DefaultConfig);
        for (int i = 1; i <= 100; i++)
        {
            var curr = calc.Calculate(i, DefaultConfig);
            Assert.True(curr <= prev, $"Score at {i} solves ({curr}) should be <= score at {i - 1} solves ({prev})");
            prev = curr;
        }
    }

    [Fact]
    public void Calculate_OneSolve_BetweenMinAndInitial()
    {
        var calc = new DynamicScoringCalculator();
        var result = calc.Calculate(1, DefaultConfig);
        Assert.True(result > DefaultConfig.MinimumPoints);
        Assert.True(result <= DefaultConfig.InitialPoints);
    }

    [Fact]
    public void Calculate_CustomConfig_UsesCorrectFormula()
    {
        // initial=500, min=50, decay=100
        // At 100 solves: ((50-500)/100^2)*100^2 + 500 = (50-500)+500 = 50
        var config = new PointsConfig(InitialPoints: 500, MinimumPoints: 50, DecayFactor: 100);
        var calc = new DynamicScoringCalculator();
        var result = calc.Calculate(100, config);
        Assert.Equal(50, result);
    }

    [Fact]
    public void Calculate_NegativeSolves_TreatedAsZero()
    {
        var calc = new DynamicScoringCalculator();
        var result = calc.Calculate(-5, DefaultConfig);
        Assert.Equal(1000, result);
    }
}
