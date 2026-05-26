namespace Foliant.App.Composition;

/// <summary>
/// Встроенный публичный ключ верификатора лицензий (ECDSA P-256, SubjectPublicKeyInfo PEM).
/// Это DEV-ключ: парный приватный ключ держит разработчик и подписывает им тестовые лицензии.
/// Перед релизом заменить на боевой публичный ключ (приватный — в офлайн-HSM/сейфе вендора).
/// </summary>
internal static class LicenseKeys
{
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE8x3fk9jE5H7JDLe2kr6LxHBKdBTo
        1tik2Sdu0tyZitnHBy94Oj/gfvnbxtemKmc9oUzP2M9JJz66DaEWxRPyzw==
        -----END PUBLIC KEY-----
        """;
}
