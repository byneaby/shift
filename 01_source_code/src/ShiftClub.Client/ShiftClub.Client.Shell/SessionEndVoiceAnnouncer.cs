using System.Globalization;
using System.Speech.Synthesis;

namespace ShiftClub.Client.Shell;

/// <summary>
/// Голосовое предупреждение о малом остатке времени — без окон поверх игры.
/// </summary>
internal static class SessionEndVoiceAnnouncer
{
    private static readonly object Gate = new();
    private static SpeechSynthesizer? _synth;

    public static void AnnounceMinutesLeft(int minutesLeft)
    {
        if (minutesLeft is not (7 or 5 or 3 or 1))
            return;

        var timePhrase = minutesLeft <= 1
            ? "осталась одна минута"
            : $"осталось {FormatMinutes(minutesLeft)}";

        Speak($"До конца сеанса {timePhrase}.");
    }

    public static void AnnounceSessionEnded(int rebootSecondsLeft)
    {
        var mins = Math.Max(1, (rebootSecondsLeft + 59) / 60);
        Speak(
            $"Сеанс завершён. Чтобы продолжить играть, добавьте время с баланса или банка в Shell. " +
            $"До перезагрузки компьютера {FormatMinutes(mins)}.");
    }

    private static string FormatMinutes(int minutes)
    {
        var n = Math.Abs(minutes);
        var mod100 = n % 100;
        var mod10 = n % 10;
        var word = mod100 is >= 11 and <= 14 ? "минут"
            : mod10 == 1 ? "минута"
            : mod10 is >= 2 and <= 4 ? "минуты"
            : "минут";
        return $"{n.ToString(CultureInfo.InvariantCulture)} {word}";
    }

    private static void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _ = Task.Run(() =>
        {
            lock (Gate)
            {
                try
                {
                    _synth ??= CreateSynth();
                    _synth.SpeakAsyncCancelAll();
                    _synth.Speak(text);
                }
                catch
                {
                    /* TTS unavailable — ignore */
                }
            }
        });
    }

    private static SpeechSynthesizer CreateSynth()
    {
        var synth = new SpeechSynthesizer();
        synth.SetOutputToDefaultAudioDevice();
        synth.Rate = 0;
        synth.Volume = 100;

        var ruVoice = synth.GetInstalledVoices()
            .FirstOrDefault(v => v.Enabled
                                 && v.VoiceInfo.Culture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        if (ruVoice is not null)
            synth.SelectVoice(ruVoice.VoiceInfo.Name);

        return synth;
    }
}
