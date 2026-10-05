using ShiftClub.Application.Import;

namespace ShiftClub.Application.Tests;

public class CustomerCsvParserTests
{
    [Fact]
    public void ParsesRussianHeadersAndSemicolons()
    {
        var csv = string.Join('\n',
            "Телефон;Имя;Фамилия;Баланс;Бонусы",
            "+7 701 123 45 67;Асет;Жумабек;1 500,50;200");

        var result = CustomerCsvParser.Parse(csv);

        var row = Assert.Single(result.Rows);
        Assert.True(row.IsValid);
        Assert.Equal("77011234567", row.Phone);
        Assert.Equal("Асет", row.FirstName);
        Assert.Equal("Жумабек", row.LastName);
        Assert.Equal(1500.50m, row.Balance);
        Assert.Equal(200m, row.BonusBalance);
    }

    [Fact]
    public void ParsesEnglishHeadersAndCommas()
    {
        var csv = string.Join('\n',
            "phone,first_name,last_name,balance",
            "87012223344,Daniyar,Omarov,1500.5");

        var result = CustomerCsvParser.Parse(csv);

        var row = Assert.Single(result.Rows);
        Assert.Equal("77012223344", row.Phone);
        Assert.Equal(1500.5m, row.Balance);
    }

    [Fact]
    public void HandlesExcelBomAndTabs()
    {
        var csv = "\uFEFFphone\tname\tbalance\n77001112233\tАйдана\t500";

        var result = CustomerCsvParser.Parse(csv);

        var row = Assert.Single(result.Rows);
        Assert.Equal("77001112233", row.Phone);
        Assert.Equal("Айдана", row.FirstName);
        Assert.Equal(500m, row.Balance);
    }

    [Fact]
    public void QuotedFieldKeepsSeparatorInside()
    {
        var csv = string.Join('\n',
            "phone,name,notes",
            "77001112233,Иван,\"должник, не пускать\"");

        var result = CustomerCsvParser.Parse(csv);

        var row = Assert.Single(result.Rows);
        Assert.Equal("должник, не пускать", row.Notes);
    }

    [Fact]
    public void FioColumnIsReadAsSurnameFirst()
    {
        var csv = "Телефон;ФИО\n77001112233;Жумабек Асет";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        Assert.Equal("Жумабек", row.LastName);
        Assert.Equal("Асет", row.FirstName);
    }

    [Fact]
    public void NameColumnIsReadAsFirstNameFirst()
    {
        var csv = "Телефон;Имя\n77001112233;Асет Жумабек";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        Assert.Equal("Асет", row.FirstName);
        Assert.Equal("Жумабек", row.LastName);
    }

    [Fact]
    public void ExplicitLastNameColumnWinsOverSplitting()
    {
        var csv = "Телефон;Имя;Фамилия\n77001112233;Асет Нурланович;Жумабек";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        Assert.Equal("Асет Нурланович", row.FirstName);
        Assert.Equal("Жумабек", row.LastName);
    }

    [Fact]
    public void MissingNameFallsBackToGuestAndWarns()
    {
        var csv = "Телефон;Баланс\n77001112233;100";

        var result = CustomerCsvParser.Parse(csv);

        Assert.Equal("Гость", Assert.Single(result.Rows).FirstName);
        Assert.Contains(result.Warnings, w => w.Contains("Гость"));
    }

    [Fact]
    public void MissingPhoneColumnIsRejectedWithExplanation()
    {
        var csv = "Имя;Баланс\nАсет;100";

        var result = CustomerCsvParser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("телефон", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BadPhoneIsFlaggedNotDropped()
    {
        var csv = string.Join('\n',
            "Телефон;Имя",
            "123;Асет",
            ";Данияр");

        var result = CustomerCsvParser.Parse(csv);

        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, r => Assert.False(r.IsValid));
        Assert.Contains("не похож на номер", result.Rows[0].Problem);
        Assert.Equal("Нет телефона", result.Rows[1].Problem);
    }

    [Fact]
    public void DuplicatePhoneInsideFileIsFlagged()
    {
        var csv = string.Join('\n',
            "Телефон;Имя",
            "77001112233;Асет",
            "8 700 111 22 33;Асет ещё раз");

        var result = CustomerCsvParser.Parse(csv);

        Assert.True(result.Rows[0].IsValid);
        Assert.False(result.Rows[1].IsValid);
        Assert.Contains("строке 2", result.Rows[1].Problem);
    }

    [Fact]
    public void NegativeBalanceIsFlagged()
    {
        var csv = "Телефон;Имя;Баланс\n77001112233;Асет;-500";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        Assert.False(row.IsValid);
        Assert.Contains("Отрицательный", row.Problem);
    }

    [Fact]
    public void NonNumericBalanceIsFlaggedNotSilentlyZeroed()
    {
        var csv = "Телефон;Имя;Баланс\n77001112233;Асет;нет данных";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        // Буквы без цифр превращаются в пустую строку, это честный ноль.
        Assert.True(row.IsValid);
        Assert.Equal(0m, row.Balance);
    }

    [Theory]
    [InlineData("1 500,50", 1500.50)]
    [InlineData("1500.5", 1500.5)]
    [InlineData("1.500,00", 1500.00)]
    [InlineData("2 000 ₸", 2000)]
    [InlineData("0", 0)]
    public void MoneyFormatsFromDifferentSystemsAreUnderstood(string raw, double expected)
    {
        var csv = $"Телефон;Баланс\n77001112233;{raw}";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        Assert.Equal((decimal)expected, row.Balance);
    }

    [Theory]
    [InlineData("01.02.1995")]
    [InlineData("1995-02-01")]
    [InlineData("01/02/1995")]
    public void BirthDateFormatsAreUnderstood(string raw)
    {
        var csv = $"Телефон;Дата рождения\n77001112233;{raw}";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        Assert.Equal(new DateOnly(1995, 2, 1), row.BirthDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputIsHandled(string? csv)
    {
        var result = CustomerCsvParser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void HeaderOnlyFileWarnsInsteadOfFailing()
    {
        var result = CustomerCsvParser.Parse("Телефон;Имя");

        Assert.Empty(result.Rows);
        Assert.Contains(result.Warnings, w => w.Contains("только заголовок"));
    }

    [Fact]
    public void InvalidEmailIsDroppedRatherThanStored()
    {
        var csv = "Телефон;Email\n77001112233;не-почта";

        var row = Assert.Single(CustomerCsvParser.Parse(csv).Rows);

        Assert.Null(row.Email);
    }
}
