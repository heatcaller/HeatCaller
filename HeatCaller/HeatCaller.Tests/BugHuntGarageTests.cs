namespace HeatCaller.Tests;

/// <summary>
/// An old setup entry, saved before setup sheets existed, folded into the
/// Kyosho sheet: the motor lands in "Motor" rather than "Motor mount", and
/// an old value with no field of its own - ride height - is kept in the
/// notes, where it can still be seen, instead of vanishing once the entry
/// is saved against the sheet.
/// </summary>
public sealed class BugHuntGarageTests
{
    private static SetupField Field(ChassisTemplate template, string label) =>
        template.AllFields.Single(f => f.Label == label);

    [Fact]
    public void The_old_motor_goes_into_Motor_not_Motor_mount()
    {
        var template = ChassisTemplate.CreateKyoshoDefault();
        var entry = new SetupEntry { Motor = "Atomic 50T" };

        entry.MigrateLegacyFields(template);

        Assert.Equal("Atomic 50T", entry.Values[Field(template, "Motor").Id]);
        Assert.False(entry.Values.ContainsKey(Field(template, "Motor mount").Id));
    }

    [Fact]
    public void A_ride_height_with_no_field_is_kept_in_the_notes_after_the_old_notes()
    {
        var template = ChassisTemplate.CreateKyoshoDefault();
        var entry = new SetupEntry { RideHeight = "3mm", Notes = "Loose on the hairpin" };

        entry.MigrateLegacyFields(template);

        Assert.Equal("Loose on the hairpin; Ride height: 3mm", entry.Values[Field(template, "Notes").Id]);
    }

    [Fact]
    public void Old_notes_alone_go_into_the_notes_unchanged()
    {
        var template = ChassisTemplate.CreateKyoshoDefault();
        var entry = new SetupEntry { Notes = "Fresh tires" };

        entry.MigrateLegacyFields(template);

        Assert.Equal("Fresh tires", entry.Values[Field(template, "Notes").Id]);
    }
}
