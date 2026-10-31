namespace ShiftClub.Shared.Licensing;

/// <summary>
/// Публичный ключ центра лицензий. Приватный ключ живёт только на сервере licence.shift-club.kz.
/// </summary>
public static class LicensePublicKey
{
    public const string Pem =
        """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAtXkiv4d0mHCC34hVn86a
        J6PfXvQ50cSkNCWlQflVE/m/xC0LREdptdu8s+kYxbtkQPnNPkNDLZO7ZwvKJCF6
        tGz+by0GMu6Z7qNOhzMkErJbw/6gTIljY8IMQY2JDJzMpsfAx/+pZmc8RHFs8eXr
        qcAuy50m7ZD1hRpA3rQxHjTU2jKEn5coxEFjm3MKwy1XrA1mzVpZ990AQkIr8LvV
        m2xObOpltGkxVu6sSsq/cqn32Dm3+u/U7P15t60qtvGeU9wqY7ozlO6jtcL0tBWV
        rDqHGMRa4dKh5LRmGp7LKrUN7b0z4m5kqMPv3DmIEPZNbcRoDac6zf3ZOMHe5rD5
        MwIDAQAB
        -----END PUBLIC KEY-----
        """;
}
