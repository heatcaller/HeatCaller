namespace HeatCaller.Tests;

/// <summary>
/// "Don't ask about the sound again", as the setup wizards offer it.
///
/// The tick used to reach the settings only when the wizard's step was left
/// with Next, Back or a rail click (SoundOffer.Save). Closing the window with
/// its X, or Skip setup in Set Up Tonight, threw it away, and the next wizard
/// offered the sound again. Now it is kept the moment it changes.
/// </summary>
public sealed class BugHuntH2WizardTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static SoundOfferHost Host()
    {
        var soundscapes = Theme.CreateBuiltInDefaults();

        return new SoundOfferHost(
            Soundscapes: () => soundscapes,
            SetNow: () => string.Empty,
            ApplySoundscape: _ => { },
            ApplyLapTimesOnly: () => { });
    }

    [Fact]
    public void The_tick_is_kept_without_the_step_being_left()
    {
        Assert.True(new AppSettings().Save());

        var asked = StaThread.Run(
            () =>
            {
                var settings = AppSettings.Load();
                var offer = new SoundOffer(settings, Host());

                // Ticked, and the wizard closed - Save is never called.
                offer.DontAsk.Checked = true;
                var afterTick = AppSettings.Load().AskAboutSoundInWizards;

                // Unticked again before closing: asked again, as the tick says.
                offer.DontAsk.Checked = false;
                var afterUntick = AppSettings.Load().AskAboutSoundInWizards;

                offer.Picker.Dispose();
                offer.Hint.Dispose();
                offer.DontAsk.Dispose();

                return (afterTick, afterUntick);
            },
            TimeSpan.FromMinutes(1),
            () => "the sound offer's tick");

        Assert.False(asked.afterTick);
        Assert.True(asked.afterUntick);
    }
}
