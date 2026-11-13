using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, Garage > Chassis Types. Enter pressed Close, so Add, type
/// the name, Enter shut the window without saving - the name was lost and a
/// chassis called "New chassis" stayed in the list. Enter now presses Save
/// Changes; Esc is still Close.
/// </summary>
public sealed class BugHunt17_GarageFormsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Enter_saves_the_chassis_and_Esc_closes()
    {
        var save = typeof(ChassisTypesForm).GetField("_btnSave", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var (enterSaves, esc) = StaThread.Run(() =>
        {
            using var form = new ChassisTypesForm(ChassisTypeStore.Load(), ChassisTemplateStore.Load(), CarStore.Load());

            return (ReferenceEquals(form.AcceptButton, save.GetValue(form)), (form.CancelButton as Button)?.Text);
        }, TimeSpan.FromSeconds(30), () => "building the Chassis Types window");

        Assert.True(enterSaves);
        Assert.Equal("Close", esc);
    }
}
