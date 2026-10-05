namespace ShiftClub.Shared;

public static class PhoneDigits
{
    public static string Normalize(string? raw)
    {
        var d = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (d.Length == 11 && d[0] == '8')
            d = "7" + d[1..];
        else if (d.Length == 10)
            d = "7" + d;
        return d;
    }

    public static string Last10(string? raw)
    {
        var d = Normalize(raw);
        return d.Length >= 10 ? d[^10..] : d;
    }
}
