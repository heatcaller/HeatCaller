using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Salted, slow PIN hashes and the manager phone's PIN key (26 Sep). The
/// vectors here are held by the relay's own tests too (relay/test/
/// pinsalt.test.ts) and were worked out a third time in Python: the laptop
/// and the relay must make the same bytes from the same PIN, or nobody signs
/// in - and the manager page must make the key this laptop checks.
/// </summary>
public sealed class PinKdfTests : IDisposable
{
    private readonly TempAppData _data = new();

    private const string Salt = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
    private const string Club = "acme-rc";

    public void Dispose() => _data.Dispose();

    // --- The shared vectors --------------------------------------------------------------

    [Fact]
    public void The_staff_racer_manager_and_invite_hashes_match_the_relay()
    {
        Assert.Equal(100_000, PinKdf.Iterations);
        Assert.Equal("8869916b2ae8b3a8a2f6d494751f95fbfddf32f955a04fbda124da7cc5150132", RelayCheckIn.PinHash(Club, "123456", Salt));
        Assert.Equal("c42a9f466ad711daaa64bad315d08406f3b7480f2770a96dc7cf6afc66a0da52", RelayCheckIn.RacerPinHash(Club, "482913", Salt));
        Assert.Equal("767e53f17da0020d7119b54479a0d36b7089c7183a2644da8ea99e7048b85fc3", RelayManage.ManagePinHash(Club, "123456", Salt));
        Assert.Equal("20efd8d81af04b85b5bde3dd4e152a1eb4e794d1956fead993823f9859a5efa9", SignupStore.InviteHash(Club, "48291375", Salt));
    }

    [Fact]
    public void The_manager_phone_s_key_and_signature_match_the_page()
    {
        var key = RelayManagerDevices.PinKeyHex("123456", Club, Salt);

        Assert.Equal("dfc7298c66de999315ed4b72e1219af794b8020f71b9021f49a6d45b61154588", key);
        Assert.Equal(
            "9f61d72c1ef12a9eac91e8ee9746f99e351549988ac2fbb9aa5b4ead48e25495",
            RelayManagerDevices.SignWithPinKey(key, Club, "0123456789abcdef01234567", "req-1", "2026-09-25T10:00:00.000Z", "{\"op\":\"Hello\",\"args\":{}}"));
    }

    [Fact]
    public void Without_a_salt_the_old_plain_hash_is_made_so_nothing_already_published_changes_by_itself()
    {
        Assert.Equal("2652164cb7d8cbe4a0bff22a9c610b100d39e4fa4a537a8ee24de8e78a87e502", RelayManage.ManagePinHash(Club, "654321"));
    }

    [Fact]
    public void The_key_is_never_the_hash_the_relay_holds()
    {
        Assert.NotEqual(RelayManage.ManagePinHash(Club, "123456", Salt), RelayManagerDevices.PinKeyHex("123456", Club, Salt));
    }

    // --- Checking a manager request ----------------------------------------------------------

    private static readonly DateTime SentAt = new(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);
    private const string Phone = "0123456789abcdef01234567";
    private const string Payload = "{\"op\":\"Hello\",\"args\":{}}";

    private static string Signed(string pin) =>
        RelayManagerDevices.SignWithPinKey(RelayManagerDevices.PinKeyHex(pin, Club, Salt), Club, Phone, "req-1", "2026-09-25T10:00:00.000Z", Payload);

    private static bool Check(string mac, string payload = Payload, DateTime? now = null, string phone = Phone) =>
        RelayManagerDevices.VerifyPinSigned("123456", Salt, Club, phone, "req-1", "2026-09-25T10:00:00.000Z", payload, mac, now ?? SentAt.AddSeconds(3), out _);

    [Fact]
    public void A_request_signed_with_the_manager_PIN_runs()
    {
        Assert.True(Check(Signed("123456")));
    }

    [Fact]
    public void A_request_signed_with_any_other_PIN_is_refused()
    {
        Assert.False(Check(Signed("123457")));
    }

    [Fact]
    public void A_request_the_relay_made_up_or_altered_is_refused()
    {
        Assert.False(Check(string.Empty));
        Assert.False(Check(Signed("123456"), payload: "{\"op\":\"DeleteRound\",\"args\":{}}"));
    }

    [Fact]
    public void A_request_from_outside_five_minutes_is_refused_either_way()
    {
        Assert.False(Check(Signed("123456"), now: SentAt.AddMinutes(6)));
        Assert.False(Check(Signed("123456"), now: SentAt.AddMinutes(-6)));
    }

    [Fact]
    public void A_phone_id_not_of_the_relay_s_shape_is_refused()
    {
        Assert.False(Check(Signed("123456"), phone: "not-a-phone"));
    }

    // --- What is published ---------------------------------------------------------------------

    [Fact]
    public void The_manage_document_carries_the_salted_hash_and_the_salt()
    {
        var settings = new AppSettings { RelayClub = "Acme-RC", ManagerPin = "123456", ManagerViaRelay = true, RelayPinSalt = Salt };

        using var doc = JsonDocument.Parse(RelayDocuments.Manage(settings));

        Assert.Equal("767e53f17da0020d7119b54479a0d36b7089c7183a2644da8ea99e7048b85fc3", doc.RootElement.GetProperty("pinSha256").GetString());
        Assert.Equal(Salt, doc.RootElement.GetProperty("pinSalt").GetString());
    }

    [Fact]
    public void With_the_manager_off_the_relay_is_given_neither_hash_nor_salt()
    {
        var settings = new AppSettings { RelayClub = Club, ManagerPin = "123456", ManagerViaRelay = false, RelayPinSalt = Salt };

        using var doc = JsonDocument.Parse(RelayDocuments.Manage(settings));

        Assert.Equal(string.Empty, doc.RootElement.GetProperty("pinSha256").GetString());
        Assert.False(doc.RootElement.TryGetProperty("pinSalt", out _));
    }

    [Fact]
    public void A_club_gets_a_salt_of_its_own_once_and_keeps_it()
    {
        var settings = new AppSettings();

        var first = PinKdf.EnsureSalt(settings);
        var again = PinKdf.EnsureSalt(settings);

        Assert.True(PinKdf.IsSalt(first));
        Assert.Equal(first, again);
        Assert.NotEqual(first, PinKdf.EnsureSalt(new AppSettings()));
    }

    [Fact]
    public void An_invite_made_before_the_salt_keeps_its_old_hash_and_a_new_one_is_salted()
    {
        var old = new SignupInvite { Club = Club, Code = "48291375" };
        var fresh = new SignupInvite { Club = Club, Code = "48291375", Salt = Salt };

        Assert.Equal(SignupStore.InviteHash(Club, "48291375"), old.Hash);
        Assert.Equal("20efd8d81af04b85b5bde3dd4e152a1eb4e794d1956fead993823f9859a5efa9", fresh.Hash);
    }
}
