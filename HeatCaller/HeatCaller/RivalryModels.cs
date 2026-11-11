namespace HeatCaller;

/// <summary>
/// A custom voice line configured for a specific PAIR of named drivers
/// (resolved via their current driver profiles), fired whenever one
/// overtakes the other in the live standings - in either direction, using
/// {overtaker}/{overtaken} placeholders so one entry covers both ways a
/// pair can swap position.
/// </summary>
public sealed class RivalryLine
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DriverA { get; set; } = string.Empty;
    public string DriverB { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default phrasings for a NEW rivalry - one is picked at random each
    /// time the pair swap. Both placeholders work in either direction, so a
    /// single set covers Sam passing Bill and Bill passing Sam.
    /// </summary>
    public List<string> Templates { get; set; } = new()
    {
        "[excited] {overtaker} gets past {overtaken}!",
        "[excited] {overtaker} takes the position back from {overtaken}!",
        "[excited] {overtaker} is through on {overtaken}!",
        "[excited] There it is - {overtaker} muscles by {overtaken}!",
        "[excited] {overtaker} finds a way past {overtaken}!",
        "[excited] {overtaken} loses out to {overtaker}!",
        "[excited] That's {overtaker} ahead of {overtaken} again!",
        "[excited] {overtaker} won't be denied - past {overtaken}!",
        "[excited] Another swap! {overtaker} leads {overtaken}!",
        "[excited] {overtaker} pounces on {overtaken}!",
        "[excited] {overtaken} had no answer for {overtaker}!",
        "[excited] These two again - {overtaker} clears {overtaken}!",
    };
}
