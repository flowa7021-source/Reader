using Foliant.Application.Services;
using Foliant.Domain;

namespace Foliant.Infrastructure.Licensing;

/// <summary>
/// Process-local non-persistent <see cref="ILicenseStorage"/> для dev-сборок и
/// тестов. Production-сборка должна регистрировать DPAPI-impl (S13/F).
/// </summary>
public sealed class InMemoryLicenseStorage : ILicenseStorage
{
    private LicenseBlob? _blob;
    private readonly Lock _gate = new();

    public Task<LicenseBlob?> LoadAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_blob);
        }
    }

    public Task SaveAsync(LicenseBlob blob, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(blob);
        lock (_gate)
        {
            _blob = blob;
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _blob = null;
        }
        return Task.CompletedTask;
    }
}
