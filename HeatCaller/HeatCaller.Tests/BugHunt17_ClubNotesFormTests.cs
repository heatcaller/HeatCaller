using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): adding a typed note emptied the box, and an
/// empty box made "Let the announcer say it" the accept button - so an
/// Enter held a little long, or tapped twice, added the note and then
/// approved the oldest waiting note from the room unread.
/// </summary>
public sealed class BugHunt17_ClubNotesFormTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static T Field<T>(ClubNotesForm form, string name) =>
        (T)typeof(ClubNotesForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    [Fact]
    public void Enter_does_not_approve_straight_after_a_note_is_added()
    {
        var notes = ClubNotes.Load();

        notes.NewMeeting("tonight");
        Assert.Null(notes.AddFromTheRoom("Shout out to the canteen", "Dot"));

        var (before, typing, after) = StaThread.Run(() =>
        {
            using var form = new ClubNotesForm(notes);

            var box = Field<TextBox>(form, "_txtNote");
            var approve = Field<Button>(form, "_btnApprove");
            var add = Field<Button>(form, "_btnAdd");
            var grid = Field<DataGridView>(form, "_grid");

            // A window that is never shown has no current row; one on
            // screen has the first. Put there by hand.
            _ = grid.Handle;
            grid.CurrentCell = grid.Rows[0].Cells[0];

            // The window as it opens: the waiting note is current, and
            // Enter approves it.
            var opened = ReferenceEquals(form.AcceptButton, approve);

            box.Text = "Raffle closes at eight";
            var whileTyping = ReferenceEquals(form.AcceptButton, add);

            typeof(ClubNotesForm)
                .GetMethod("AddTyped", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, null);

            return (opened, whileTyping, form.AcceptButton);
        }, TimeSpan.FromSeconds(30), () => "adding a typed note with one from the room waiting");

        Assert.True(before, "Enter approves the waiting note when the window opens");
        Assert.True(typing, "Enter adds the note while one is being typed");

        // The note went in, and the next Enter has no button to press.
        Assert.Null(after);
        Assert.Equal(2, notes.All.Count);
        Assert.Equal(1, notes.Counts().ToApprove);
    }
}
