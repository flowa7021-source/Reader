using FluentAssertions;
using Xunit;

namespace Foliant.Domain.Tests;

public sealed class TrialStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Equality_IsValueBased_AcrossAllFields()
    {
        var a = new TrialState(Start, Start.AddDays(3), "nonce-1");
        var b = new TrialState(Start, Start.AddDays(3), "nonce-1");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Inequality_WhenAnyFieldDiffers()
    {
        var baseline = new TrialState(Start, Start.AddDays(3), "nonce-1");

        baseline.Should().NotBe(baseline with { StartedAt = Start.AddDays(1) });
        baseline.Should().NotBe(baseline with { MaxObservedAt = Start.AddDays(4) });
        baseline.Should().NotBe(baseline with { Nonce = "nonce-2" });
    }

    [Fact]
    public void With_MutatesOnlyTargetedField()
    {
        var baseline = new TrialState(Start, Start.AddDays(3), "nonce-1");

        var mutated = baseline with { MaxObservedAt = Start.AddDays(5) };

        mutated.StartedAt.Should().Be(baseline.StartedAt);
        mutated.Nonce.Should().Be(baseline.Nonce);
        mutated.MaxObservedAt.Should().Be(Start.AddDays(5));
    }

    [Theory]
    [InlineData(TrialStatus.NotStarted)]
    [InlineData(TrialStatus.Active)]
    [InlineData(TrialStatus.Expired)]
    [InlineData(TrialStatus.Tampered)]
    public void TrialEvaluation_PreservesStatus(TrialStatus status)
    {
        var evaluation = new TrialEvaluation(status, DaysRemaining: 5, TamperReason: null);

        evaluation.Status.Should().Be(status);
        evaluation.DaysRemaining.Should().Be(5);
        evaluation.TamperReason.Should().BeNull();
    }

    [Fact]
    public void TrialEvaluation_TamperedCarriesReason()
    {
        var evaluation = new TrialEvaluation(TrialStatus.Tampered, DaysRemaining: 0, TamperReason: "clock rollback");

        evaluation.Status.Should().Be(TrialStatus.Tampered);
        evaluation.TamperReason.Should().Be("clock rollback");
    }

    [Fact]
    public void TrialEvaluation_EqualityIsValueBased()
    {
        var a = new TrialEvaluation(TrialStatus.Active, 7, null);
        var b = new TrialEvaluation(TrialStatus.Active, 7, null);

        a.Should().Be(b);
        a.Should().NotBe(b with { DaysRemaining = 6 });
        a.Should().NotBe(b with { Status = TrialStatus.Expired });
    }
}
