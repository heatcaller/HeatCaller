using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The Connection page's Source picker for the decoders from 3 up.
/// </summary>
public sealed class StreamDecoderSourceTests
{
    [Fact]
    public void Changing_source_keeps_an_address_typed_but_not_yet_connected_with()
    {
        StaThread.Run(() =>
        {
            using var data = new TempAppData();

            var settings = new AppSettings
            {
                StartMaximized = false,
                HasCompletedFirstRun = true,
                HasSeenRaceDaySetup = true,
                AutoBackupEnabled = false,
                LastConnectionType = DecoderConnection.MyLapsP3,
                ConnectDecodersAtStartup = false,
            };

            var p3 = settings.StreamDecoder(DecoderConnection.MyLapsP3);
            p3.Transport = DecoderTransport.Network;
            p3.Host = "192.168.1.50";
            settings.Save();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();
                window.ShowPageForLayoutCheck("Connection");
                StaThread.Settle();

                var source = Field<ComboBox>(window, "cmbStreamSource");
                var host = Field<TextBox>(window, "txtStreamHost");
                var port = Field<TextBox>(window, "txtStreamPort");
                var sources = Field<DecoderTransport[]>(window, "_streamSources");

                Assert.Equal("192.168.1.50", host.Text);

                host.Text = "10.0.0.7";
                port.Text = "6000";

                source.SelectedIndex = Array.IndexOf(sources, DecoderTransport.Simulated);
                StaThread.Settle();
                source.SelectedIndex = Array.IndexOf(sources, DecoderTransport.Network);
                StaThread.Settle();

                Assert.Equal("10.0.0.7", host.Text);
                Assert.Equal("6000", port.Text);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }

            return 0;
        }, TimeSpan.FromMinutes(2), () => "source change");
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
