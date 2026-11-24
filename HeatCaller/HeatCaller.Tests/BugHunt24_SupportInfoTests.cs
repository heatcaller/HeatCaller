namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Save Support Info: the crew, mentor and
/// guardian names kept in Driver Hub are replaced in the support zip, as the
/// drivers' own names are.
/// </summary>
public sealed class BugHunt24_SupportInfoTests
{
    private static Redactor RedactorFor(params DriverProfile[] profiles) =>
        Redactor.For(SupportInfo.PeopleFor(profiles), @"C:\Users\asher");

    [Fact]
    public void CrewMentorAndGuardianNamesAreReplaced()
    {
        var redactor = RedactorFor(new DriverProfile
        {
            Transponder = "1001",
            DriverName = "Sam Carter",
            CrewPrincipalName = "Dana Whitlock",
            CrewPrincipalSaidAs = "Dayna Witlock",
            CrewTechName = "Priya Nair",
            Mentor = "Lee Marsh",
            WaiverGuardian = "Morgan Ellery",
        });

        var lines = new[]
        {
            "[commentary] On the recap show: Dana Whitlock in the booth in the announcer's own voice",
            "[commentary] At the flag: down to pit lane for a word with Priya Nair",
            "[voice] Announcing: \"Dayna Witlock, how did Sam Carter do?\"",
            "mentor Lee Marsh, guardian Morgan Ellery",
        };

        var output = string.Join("\n", lines.Select(l => redactor.Apply(l, out _)));

        foreach (var word in new[] { "Dana", "Whitlock", "Dayna", "Witlock", "Priya", "Nair", "Lee", "Marsh", "Morgan", "Ellery", "Sam", "Carter" })
        {
            Assert.DoesNotContain(word, output);
        }

        Assert.Equal("how did [driver 1] do?", redactor.Apply("how did Sam Carter do?", out _));
    }

    [Fact]
    public void ADriversNumbersStayTheSameWithCrewNamed()
    {
        var redactor = RedactorFor(
            new DriverProfile { Transponder = "1001", DriverName = "Sam Carter", CrewPrincipalName = "Dana Whitlock" },
            new DriverProfile { Transponder = "1001", DriverName = "Frank Lee" });

        Assert.Equal("[driver 1] and [driver 2]", redactor.Apply("Sam Carter and Frank Lee", out _));
    }

    [Fact]
    public void AMentorWhoIsAlsoADriverKeepsTheDriversNumber()
    {
        var people = SupportInfo.PeopleFor(new[]
        {
            new DriverProfile { Transponder = "1001", DriverName = "Sam Carter", Mentor = "Frank Lee" },
            new DriverProfile { Transponder = "1001", DriverName = "Frank Lee", WaiverGuardian = "Pat Carter" },
            new DriverProfile { Transponder = "1001", DriverName = "Jo Carter", WaiverGuardian = "pat carter" },
        });

        // Three drivers and one guardian, named once.
        Assert.Equal(4, people.Count);

        var redactor = Redactor.For(people, null);
        Assert.Equal("mentor [driver 2]", redactor.Apply("mentor Frank Lee", out _));
        Assert.Equal("guardian [driver 4]", redactor.Apply("guardian Pat Carter", out _));
    }

    [Fact]
    public void BlankCrewFieldsAddNobody()
    {
        var people = SupportInfo.PeopleFor(new[] { new DriverProfile { Transponder = "1001", DriverName = "Sam Carter", CrewTechSaidAs = "  " } });

        Assert.Single(people);
    }
}
