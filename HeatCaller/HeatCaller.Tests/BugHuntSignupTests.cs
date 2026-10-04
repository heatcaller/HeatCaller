namespace HeatCaller.Tests;

/// <summary>
/// Online sign-up fixes that can be pinned without a window: the warning
/// about invites whose codes were sealed on another PC.
/// </summary>
public sealed class BugHuntSignupTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>
    /// signups.json from another PC: one invite still out, its code sealed
    /// somewhere this PC can't open ("Not a sealed value" stands in for it).
    /// </summary>
    private void WriteFileFromAnotherPc()
    {
        Directory.CreateDirectory(AppPaths.Root);
        var expires = DateTime.UtcNow.AddDays(7).ToString("o");
        File.WriteAllText(_data.PathOf("signups.json"),
            "{ \"Enabled\": true, \"SealedCodes\": \"sealed-on-another-pc\", \"Invites\": ["
            + "{ \"Id\": \"elsewhere\", \"Club\": \"club\", \"Email\": \"sam@example.com\", \"ExpiresUtc\": \"" + expires + "\" } ] }");
    }

    [Fact]
    public void Invites_from_another_pc_are_flagged_when_read()
    {
        WriteFileFromAnotherPc();

        var store = SignupStore.Load();

        Assert.Single(store.All);
        Assert.True(store.CodesLocked);
    }

    [Fact]
    public void Making_an_invite_here_keeps_the_warning_for_the_ones_from_another_pc()
    {
        WriteFileFromAnotherPc();
        var store = SignupStore.Load();

        // It used to be cleared by the save that sealed the new code, while
        // the other PC's invite was still out and still unusable here.
        store.Create(new AppSettings(), "jo@example.com", string.Empty);
        store.Save();

        Assert.True(store.CodesLocked);
        Assert.True(SignupStore.Load().CodesLocked);
    }

    [Fact]
    public void No_warning_once_none_of_the_other_pcs_invites_is_still_out()
    {
        WriteFileFromAnotherPc();
        var store = SignupStore.Load();

        store.Get("elsewhere")!.State = SignupInviteState.Cancelled;

        Assert.False(store.CodesLocked);
    }
}
