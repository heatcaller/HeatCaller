using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The Cast tab's side of ElevenLabs retiring its Default voices (24 Nov
/// 2026, Asher): a seat on Charlie shows the retirement line, its list offers
/// HeatCaller's six, and Switch moves the seat and clears the line.
/// </summary>
public sealed class ElevenLabsRetiredVoicesCastTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static volatile string _now = "starting";

    private static T Field<T>(CastControl cast, string name) => (T)typeof(CastControl)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(cast)!;

    [Fact]
    public void A_seat_on_a_retired_voice_is_told_and_switch_moves_it()
    {
        var problems = StaThread.Run(Check, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> Check()
    {
        var problems = new List<string>();
        var settings = new AppSettings { PrincipalElevenLabsVoiceId = "IKne3meq5aSn9XLyUdCD", PrincipalElevenLabsVoiceName = "Charlie" };

        _now = "building the Cast tab";

        using var cast = new CastControl(settings) { EngineInUse = () => VoiceEngine.ElevenLabs };

        var principal = Field<ComboBox>(cast, "_cmbPrincipalVoice");

        if (principal.Items.OfType<CastControl.VoiceChoice>().Count(c => c.Display.StartsWith(RetiredVoices.SuggestedMark, StringComparison.Ordinal)) != 6)
        {
            problems.Add("the Team Principal's list doesn't offer HeatCaller's six");
        }

        var notices = Field<Array>(cast, "_notices");
        var notice = notices.Cast<object>().First(n => (RetiredVoices.Seat)n.GetType().GetProperty("Seat")!.GetValue(n)! == RetiredVoices.Seat.TeamPrincipal);
        var words = (Label)notice.GetType().GetProperty("Words")!.GetValue(notice)!;

        if (words.Text != "ElevenLabs retires Charlie on 31 December. Switch to Darian?")
        {
            problems.Add($"the Team Principal's line reads \"{words.Text}\"");
        }

        _now = "switching";
        typeof(CastControl).GetMethod("SwitchSeat", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cast, new[] { notice });

        if (settings.PrincipalElevenLabsVoiceId != "gOupLcAkjEnguROwi4oS")
        {
            problems.Add($"Switch saved {settings.PrincipalElevenLabsVoiceId}");
        }

        if (words.Text.Length > 0)
        {
            problems.Add($"the line still reads \"{words.Text}\" after Switch");
        }

        if (principal.SelectedItem is not CastControl.VoiceChoice { Id: "gOupLcAkjEnguROwi4oS" })
        {
            problems.Add("the list doesn't show Darian after Switch");
        }

        return problems;
    }
}
