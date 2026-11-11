namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16, built-in model: a load left running by a line that timed out
/// no longer installs itself (and disposes the weights in use) once the model
/// already held has been asked for again, and a client never writes with
/// weights another client's load swapped in. Real weights cannot be loaded
/// in a test, so these pin the two decisions the load path now makes.
/// </summary>
public sealed class BugHunt16_BundledLlmWeightsTests
{
    private const string Bundled = "bundled\u001f4096\u001f2";
    private const string Override = "big-override\u001f4096\u001f2";

    [Fact]
    public void Asking_for_the_held_model_overtakes_a_pending_load_of_another()
    {
        Assert.True(BundledLlmClient.OvertakesPendingLoad(Bundled, Override, Bundled));
    }

    [Fact]
    public void Nothing_pending_or_the_same_model_pending_is_not_overtaken()
    {
        Assert.False(BundledLlmClient.OvertakesPendingLoad(Bundled, null, Bundled));
        Assert.False(BundledLlmClient.OvertakesPendingLoad(Bundled, Bundled, Bundled));
        Assert.False(BundledLlmClient.OvertakesPendingLoad(Override, Override, Bundled));
    }

    [Fact]
    public void A_client_is_not_handed_weights_another_client_swapped_in()
    {
        Assert.False(BundledLlmClient.CanUseHeldWeights(Override, Bundled, loadPending: false));
    }

    [Fact]
    public void A_client_that_has_not_chosen_weights_yet_takes_the_load_path()
    {
        Assert.False(BundledLlmClient.CanUseHeldWeights(Bundled, null, loadPending: false));
    }

    [Fact]
    public void A_pending_load_sends_even_the_right_client_through_the_load_path()
    {
        Assert.False(BundledLlmClient.CanUseHeldWeights(Bundled, Bundled, loadPending: true));
        Assert.True(BundledLlmClient.CanUseHeldWeights(Bundled, Bundled, loadPending: false));
    }
}
