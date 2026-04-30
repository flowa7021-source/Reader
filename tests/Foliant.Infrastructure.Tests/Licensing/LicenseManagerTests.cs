using FluentAssertions;
using Foliant.Application.Services;
using Foliant.Domain;
using Foliant.Infrastructure.Licensing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Foliant.Infrastructure.Tests.Licensing;

public sealed class LicenseManagerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ILicenseStorage _storage = Substitute.For<ILicenseStorage>();
    private readonly ILicenseVerifier _verifier = Substitute.For<ILicenseVerifier>();
    private readonly TimeProvider _clock = new FixedClock(Now);
    private readonly LicenseManager _sut;

    public LicenseManagerTests()
    {
        _sut = new LicenseManager(_storage, _verifier, _clock, NullLogger<LicenseManager>.Instance);
    }

    [Fact]
    public async Task Current_NoStoredBlob_ReturnsMissing()
    {
        _storage.LoadAsync(Arg.Any<CancellationToken>()).Returns((LicenseBlob?)null);

        var result = await _sut.CurrentAsync(default);

        result.Status.Should().Be(LicenseStatus.Missing);
        _verifier.DidNotReceive().Verify(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Current_StoredBlob_DelegatesToVerifier()
    {
        var blob = new LicenseBlob("{\"User\":\"alice\"}", "sig==");
        _storage.LoadAsync(Arg.Any<CancellationToken>()).Returns(blob);
        var license = new License("alice", "Pro", Now.AddDays(180), ["editor"]);
        _verifier.Verify(blob.LicenseJson, blob.SignatureBase64, Now)
                 .Returns(LicenseValidationResult.Valid(license));

        var result = await _sut.CurrentAsync(default);

        result.Status.Should().Be(LicenseStatus.Valid);
        result.License!.User.Should().Be("alice");
    }

    [Fact]
    public async Task Import_VerifierAcceptsLicense_PersistsAndReturnsValid()
    {
        var json = "{\"User\":\"alice\"}";
        var sig = "abc==";
        var license = new License("alice", "Pro", Now.AddYears(1), []);
        _verifier.Verify(json, sig, Now).Returns(LicenseValidationResult.Valid(license));

        var result = await _sut.ImportAsync(json, sig, default);

        result.Status.Should().Be(LicenseStatus.Valid);
        await _storage.Received(1).SaveAsync(
            Arg.Is<LicenseBlob>(b => b.LicenseJson == json && b.SignatureBase64 == sig),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_VerifierRejects_DoesNotPersist()
    {
        var json = "{\"User\":\"mallory\"}";
        var sig = "tampered==";
        _verifier.Verify(json, sig, Now)
                 .Returns(LicenseValidationResult.Invalid("Bad signature"));

        var result = await _sut.ImportAsync(json, sig, default);

        result.Status.Should().Be(LicenseStatus.Invalid);
        await _storage.DidNotReceive().SaveAsync(Arg.Any<LicenseBlob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_VerifierExpired_DoesNotPersist()
    {
        var json = "{\"User\":\"alice\"}";
        var sig = "ok==";
        var license = new License("alice", "Pro", Now.AddDays(-1), []);
        _verifier.Verify(json, sig, Now).Returns(LicenseValidationResult.Expired(license));

        var result = await _sut.ImportAsync(json, sig, default);

        result.Status.Should().Be(LicenseStatus.Expired);
        await _storage.DidNotReceive().SaveAsync(Arg.Any<LicenseBlob>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clear_DelegatesToStorage()
    {
        await _sut.ClearAsync(default);

        await _storage.Received(1).ClearAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_NullArgs_Throw()
    {
        var act1 = () => _sut.ImportAsync(null!, "sig", default);
        var act2 = () => _sut.ImportAsync("json", null!, default);

        await act1.Should().ThrowAsync<ArgumentNullException>();
        await act2.Should().ThrowAsync<ArgumentNullException>();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
