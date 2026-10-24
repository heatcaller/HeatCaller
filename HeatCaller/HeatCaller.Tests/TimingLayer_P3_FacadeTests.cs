using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// P3 of the timing layer (23 Oct 2026): the app reaches the race only
/// through RaceTiming, which has the engine's surface member for member and
/// (in inline mode, the only mode so far) forwards every call straight to
/// it. These tests keep the two surfaces the same - a member added to the
/// engine and not to the facade fails here, not at a club's PC - and prove
/// the forwarding: a lap through the facade reaches the engine, a setting
/// set on it is the engine's, and an event subscribed on it fires.
/// </summary>
public sealed class TimingLayer_P3_FacadeTests
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private const BindingFlags AnyInstance = PublicInstance | BindingFlags.NonPublic;

    /// <summary>
    /// Public engine members the facade deliberately does not offer. Empty:
    /// every public instance member is on it. (The engine's static
    /// MinSectorMs and SectorsFrom are not instance members and are used by
    /// their engine names; the nested StandingEdit and SessionSnapshot types
    /// are used by their engine names too.)
    /// </summary>
    private static readonly HashSet<string> NotOnTheFacade = new(StringComparer.Ordinal);

    /// <summary>
    /// The engine's internal members the app uses, so the facade has them
    /// (internal) as well. Everything else internal on the engine is for the
    /// tests and the replay harness, which use the engine itself.
    /// </summary>
    private static readonly string[] InternalsTheAppUses = { "Recorder", "IsOutOfTime" };

    private static RaceControlSession Racing()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    private static DirectLapEvent Pass(string car, uint deviceTimeMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        IsSynthetic = true,
    };

    private static string Describe(ParameterInfo p) =>
        $"{p.ParameterType} {p.Name}{(p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : string.Empty)}";

    private static string Signature(MethodInfo m) =>
        $"{m.ReturnType} {m.Name}({string.Join(", ", m.GetParameters().Select(Describe))})";

    private static IEnumerable<MethodInfo> PlainMethods(Type type, BindingFlags flags) =>
        type.GetMethods(flags).Where(m => !m.IsSpecialName);

    [Fact]
    public void Every_public_method_of_the_engine_is_on_the_facade_with_the_same_signature()
    {
        var facade = PlainMethods(typeof(RaceTiming), PublicInstance).Select(Signature).ToHashSet(StringComparer.Ordinal);
        var missing = PlainMethods(typeof(RaceControlSession), PublicInstance)
            .Where(m => !NotOnTheFacade.Contains(m.Name))
            .Select(Signature)
            .Where(s => !facade.Contains(s))
            .ToList();

        Assert.True(missing.Count == 0, "Not on RaceTiming: " + string.Join("; ", missing));
    }

    [Fact]
    public void Every_public_property_of_the_engine_is_on_the_facade_with_the_same_type_and_setter()
    {
        var problems = new List<string>();

        foreach (var engine in typeof(RaceControlSession).GetProperties(PublicInstance))
        {
            if (NotOnTheFacade.Contains(engine.Name))
            {
                continue;
            }

            var facade = typeof(RaceTiming).GetProperty(engine.Name, PublicInstance);
            if (facade is null)
            {
                problems.Add($"{engine.Name}: missing");
                continue;
            }

            if (facade.PropertyType != engine.PropertyType)
            {
                problems.Add($"{engine.Name}: {facade.PropertyType} instead of {engine.PropertyType}");
            }

            // A setter the app can use on the engine must be one it can use
            // here - and a private set on the engine stays read-only here.
            var engineSets = engine.SetMethod?.IsPublic == true;
            var facadeSets = facade.SetMethod?.IsPublic == true;
            if (engineSets != facadeSets || facade.GetMethod?.IsPublic != true)
            {
                problems.Add($"{engine.Name}: get/set differ");
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void Every_event_of_the_engine_is_on_the_facade_with_the_same_handler_type()
    {
        var engineEvents = typeof(RaceControlSession).GetEvents(PublicInstance);
        var problems = engineEvents
            .Where(e => !NotOnTheFacade.Contains(e.Name))
            .Where(e => typeof(RaceTiming).GetEvent(e.Name, PublicInstance)?.EventHandlerType != e.EventHandlerType)
            .Select(e => e.Name)
            .ToList();

        Assert.True(problems.Count == 0, "Not on RaceTiming: " + string.Join(", ", problems));

        // The plan counts 18; a new one is fine, as long as it is forwarded.
        Assert.True(engineEvents.Length >= 18);
    }

    [Fact]
    public void The_facade_offers_nothing_the_engine_does_not()
    {
        // Same surface both ways, so P4's threaded mode has exactly the
        // engine's members to provide and nothing of the facade's own.
        var engineNames = typeof(RaceControlSession).GetMembers(PublicInstance).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var extra = typeof(RaceTiming).GetMembers(PublicInstance)
            .Where(m => m is not ConstructorInfo)
            .Select(m => m.Name)
            .Where(n => !engineNames.Contains(n))
            .ToList();

        Assert.True(extra.Count == 0, "Only on RaceTiming: " + string.Join(", ", extra));
    }

    [Fact]
    public void The_engine_internals_the_app_uses_are_on_the_facade_as_internals()
    {
        foreach (var name in InternalsTheAppUses)
        {
            var engine = typeof(RaceControlSession).GetMember(name, AnyInstance).Single();
            var facade = typeof(RaceTiming).GetMember(name, AnyInstance).Single();

            Assert.Equal(engine.MemberType, facade.MemberType);

            switch (engine)
            {
                case PropertyInfo property:
                    Assert.Equal(property.PropertyType, ((PropertyInfo)facade).PropertyType);
                    Assert.False(((PropertyInfo)facade).GetMethod!.IsPublic);
                    break;
                case MethodInfo method:
                    Assert.Equal(Signature(method), Signature((MethodInfo)facade));
                    Assert.False(((MethodInfo)facade).IsPublic);
                    break;
            }
        }
    }

    [Fact]
    public void A_lap_through_the_facade_reaches_the_engine()
    {
        var engine = Racing();
        var timing = new RaceTiming(engine);

        timing.ProcessLapEvent(Pass("101", 11_000));
        timing.ProcessLapEvent(Pass("101", 23_000));

        var standing = engine.GetOrderedStandings().Single(s => s.TransponderId == "101");
        Assert.True(standing.Laps > 0);

        // The facade reads the very same standing - the engine's live object
        // (P4 must hand out frozen copies instead; see RaceTiming).
        Assert.Same(standing, timing.GetOrderedStandings().Single());
        Assert.Equal(engine.SessionGeneration, timing.SessionGeneration);
        Assert.Equal(RaceState.Racing, timing.State);
    }

    [Fact]
    public void Settings_and_lookups_set_on_the_facade_are_the_engines()
    {
        var engine = new RaceControlSession();
        var timing = new RaceTiming(engine);
        Func<string, string?> owner = _ => null;
        Func<int> streak = () => 5;

        timing.LapLimit = 7;
        timing.TimeLimit = TimeSpan.FromMinutes(4);
        timing.AlternateTransponderOwner = owner;
        timing.StreakThresholdProvider = streak;

        Assert.Equal(7, engine.LapLimit);
        Assert.Equal(TimeSpan.FromMinutes(4), engine.TimeLimit);
        Assert.Same(owner, engine.AlternateTransponderOwner);
        Assert.Same(streak, engine.StreakThresholdProvider);
        Assert.Equal(5, timing.StreakThreshold);
        Assert.Same(engine, timing.Engine);
    }

    [Fact]
    public void An_event_subscribed_on_the_facade_fires_and_unsubscribes()
    {
        var engine = new RaceControlSession();
        var timing = new RaceTiming(engine);
        var states = new List<RaceState>();
        var laps = new List<RacerStanding>();
        Action<RaceState> onState = states.Add;
        Action<RacerStanding, bool, bool> onLap = (standing, _, _) => laps.Add(standing);

        timing.StateChanged += onState;
        timing.LapProcessed += onLap;

        // A null handler is ignored both ways, as it is on the engine.
        timing.StateChanged += null;
        timing.StateChanged -= null;

        timing.Stage();
        timing.GreenFlag(0);
        engine.ProcessLapEvent(Pass("202", 11_000));

        Assert.Contains(RaceState.Staging, states);
        Assert.Contains(RaceState.Racing, states);
        Assert.NotEmpty(laps);
        Assert.Same(engine.GetOrderedStandings().Single(), laps[^1]);

        // Off again through the facade: the engine's event no longer has it.
        timing.StateChanged -= onState;
        timing.LapProcessed -= onLap;
        var statesBefore = states.Count;
        var lapsBefore = laps.Count;

        engine.ProcessLapEvent(Pass("202", 23_000));
        timing.Reset();

        Assert.Equal(statesBefore, states.Count);
        Assert.Equal(lapsBefore, laps.Count);
    }

    [Fact]
    public void The_facade_needs_an_engine()
    {
        Assert.Throws<ArgumentNullException>(() => new RaceTiming(null!));
    }
}
