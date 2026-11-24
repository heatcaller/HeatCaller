using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the Cast tab. A slow Load Voices that
/// answered after the engine was changed on the Voice tab filled every list
/// for the old engine, so picks were saved for an engine that no longer spoke.
/// </summary>
public sealed class BugHunt24_CastControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static volatile string _now = "starting";

    private static readonly CastControl.VoiceChoice Loaded = new("loaded-eleven-id", "Loaded", string.Empty, "Loaded voice");

    private static T Field<T>(CastControl cast, string name) => (T)typeof(CastControl)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(cast)!;

    private static void LoadVoicesNow(CastControl cast) => ((Task)typeof(CastControl)
        .GetMethod("LoadVoicesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(cast, null)!).GetAwaiter().GetResult();

    private static bool CoHostListHasLoaded(CastControl cast) => Field<ComboBox>(cast, "_cmbCoHostVoice")
        .Items.OfType<CastControl.VoiceChoice>().Any(c => c.Id == Loaded.Id);

    [Fact]
    public void LoadVoices_AnswerAfterEngineChanged_LeavesListsOnEngineInUse()
    {
        var problems = StaThread.Run(CheckEngineChangedDuringLoad, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> CheckEngineChangedDuringLoad()
    {
        var problems = new List<string>();
        var engine = VoiceEngine.ElevenLabs;

        _now = "building the Cast tab";

        using var cast = new CastControl(new AppSettings())
        {
            EngineInUse = () => engine,

            // The engine is changed on the Voice tab while ElevenLabs is
            // still answering - the answer arrives for the old engine.
            LoadVoices = asked =>
            {
                engine = VoiceEngine.Kokoro;
                return Task.FromResult<IReadOnlyList<CastControl.VoiceChoice>>(new[] { Loaded });
            },
        };

        _now = "loading the voices";
        LoadVoicesNow(cast);

        if (Field<VoiceEngine>(cast, "_voiceListEngine") != VoiceEngine.Kokoro)
        {
            problems.Add($"the lists are set out for {Field<VoiceEngine>(cast, "_voiceListEngine")}, not Kokoro, the engine in use");
        }

        if (CoHostListHasLoaded(cast))
        {
            problems.Add("the co-host's list offers the ElevenLabs voice while Kokoro is the engine");
        }

        _now = "switching back to ElevenLabs";

        // The answer is kept for its own engine and comes back with it.
        engine = VoiceEngine.ElevenLabs;
        typeof(CastControl)
            .GetMethod("FillVoices", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cast, new object?[] { VoiceEngine.ElevenLabs, null });

        if (!CoHostListHasLoaded(cast))
        {
            problems.Add("the loaded ElevenLabs voices were lost when ElevenLabs was picked again");
        }

        return problems;
    }

    [Fact]
    public void LoadVoices_EngineUnchanged_FillsTheLists()
    {
        var problems = StaThread.Run(CheckEngineUnchanged, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> CheckEngineUnchanged()
    {
        var problems = new List<string>();

        _now = "building the Cast tab";

        using var cast = new CastControl(new AppSettings())
        {
            EngineInUse = () => VoiceEngine.ElevenLabs,
            LoadVoices = _ => Task.FromResult<IReadOnlyList<CastControl.VoiceChoice>>(new[] { Loaded }),
        };

        _now = "loading the voices";
        LoadVoicesNow(cast);

        if (Field<VoiceEngine>(cast, "_voiceListEngine") != VoiceEngine.ElevenLabs)
        {
            problems.Add($"the lists are set out for {Field<VoiceEngine>(cast, "_voiceListEngine")}, not ElevenLabs");
        }

        if (!CoHostListHasLoaded(cast))
        {
            problems.Add("the co-host's list doesn't offer the loaded voice");
        }

        return problems;
    }
}
