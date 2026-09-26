namespace HeatCaller;

/// <summary>What became of a race-control command sent from the phone remote.</summary>
public enum RemoteCommandStatus
{
    /// <summary>Race Control carried it out.</summary>
    Done,

    /// <summary>Race Control would not do it. The message says why.</summary>
    Refused,

    /// <summary>
    /// It reached Race Control, which had not finished with it by the time
    /// the phone had to be answered - most often because the laptop is
    /// asking its operator a question about it. What happens next is
    /// decided on the laptop.
    /// </summary>
    Waiting,

    /// <summary>It never reached Race Control (the window closing, or not up yet).</summary>
    NotDelivered,

    /// <summary>
    /// A Stage or Countdown the phone sent without its "yes, abandon the
    /// running race" - which it does with every press - and a race was
    /// running when it reached Race Control. The phone asks its user, and
    /// sends it again confirmed if they say yes.
    /// Added last: the values are never saved, but the order is kept anyway.
    /// </summary>
    NeedsConfirm,
}

/// <summary>
/// The answer the phone remote gets for one command: what happened, and a
/// short sentence to show on the phone.
///
/// The phone used to be told "sent" as soon as a command had been handed
/// to the laptop's UI thread, and a command that Race Control then refused
/// (Stage during practice, a checkered with no race running) still read as
/// sent. Race Control now says what it did with it.
/// </summary>
public readonly record struct RemoteCommandOutcome(RemoteCommandStatus Status, string Message)
{
    public static RemoteCommandOutcome Done(string message) => new(RemoteCommandStatus.Done, message);

    public static RemoteCommandOutcome Refused(string message) => new(RemoteCommandStatus.Refused, message);

    public static RemoteCommandOutcome Waiting(string message) => new(RemoteCommandStatus.Waiting, message);

    public static RemoteCommandOutcome NotDelivered(string message) => new(RemoteCommandStatus.NotDelivered, message);

    public static RemoteCommandOutcome NeedsConfirm(string message) => new(RemoteCommandStatus.NeedsConfirm, message);
}
