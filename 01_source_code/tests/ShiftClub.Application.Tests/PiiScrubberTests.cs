using ShiftClub.Application.Diagnostics;
using Xunit;

namespace ShiftClub.Application.Tests;

public class PiiScrubberTests
{
    [Fact]
    public void PhoneNumberIsRemoved()
    {
        var result = PiiScrubber.Scrub("Не нашли клиента +7 701 234 56 78 в базе");

        Assert.DoesNotContain("701", result);
        Assert.Contains("[телефон]", result);
    }

    [Fact]
    public void EmailIsRemoved()
    {
        var result = PiiScrubber.Scrub("Письмо не ушло на asset.omarov@gmail.com");

        Assert.DoesNotContain("omarov", result);
        Assert.Contains("[email]", result);
    }

    [Fact]
    public void ConnectionStringPasswordIsRemoved()
    {
        var result = PiiScrubber.Scrub(
            "Host=127.0.0.1;Database=shiftclub;Username=shiftclub;Password=SuperSecret1;");

        Assert.DoesNotContain("SuperSecret1", result);
        Assert.Contains("password=[скрыто]", result);
    }

    [Fact]
    public void TelegramBotTokenIsRemoved()
    {
        var result = PiiScrubber.Scrub("Telegram ответил 401 для 123456789:AAFakeTokenValueThatIsLongEnoughHere");

        Assert.DoesNotContain("AAFakeTokenValue", result);
        Assert.Contains("[токен]", result);
    }

    [Fact]
    public void JwtIsRemoved()
    {
        var result = PiiScrubber.Scrub(
            "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJvd25lciJ9.6Fx3Qd1Zm9lKqPwTtYbN2s");

        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", result);
    }

    [Fact]
    public void CardLikeNumberIsRemoved()
    {
        var result = PiiScrubber.Scrub("Kaspi вернул отказ по карте 4400430012345678");

        Assert.DoesNotContain("4400430012345678", result);
    }

    [Fact]
    public void WindowsUserFolderIsRemoved()
    {
        var result = PiiScrubber.Scrub(@"Не найден файл C:\Users\Aset\AppData\shell.log");

        Assert.DoesNotContain("Aset", result);
        Assert.Contains("[пользователь]", result);
    }

    [Fact]
    public void OrdinaryTextSurvives()
    {
        const string text = "Сеанс не стартовал: зона Hall не активна";

        Assert.Equal(text, PiiScrubber.Scrub(text));
    }

    [Fact]
    public void NullBecomesEmpty()
    {
        Assert.Equal("", PiiScrubber.Scrub(null));
    }

    [Fact]
    public void NormalizeHidesIdentifiersSoSameFaultGroupsTogether()
    {
        var first = PiiScrubber.Normalize("Сеанс 7f1d0c4e-8a21-4b3e-9f10-2c5d6e7a8b90 уже закрыт (попытка 3)");
        var second = PiiScrubber.Normalize("Сеанс 11112222-3333-4444-5555-666677778888 уже закрыт (попытка 9)");

        Assert.Equal(first, second);
    }
}
