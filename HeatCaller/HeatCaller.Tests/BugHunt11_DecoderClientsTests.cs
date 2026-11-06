using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), the decoder clients and the Connection page:
/// Disconnect must stay usable while a decoder from 3 up keeps retrying, and
/// the Line Up offset must be turned round after a restart that began on one.
/// </summary>
public sealed class BugHunt11_DecoderClientsTests
{
    [Fact]
    public void Disconnect_stays_usable_while_a_stream_decoder_keeps_retrying_after_an_error()
    {
        StaThread.Run(() =>
        {
            using var data = new TempAppData();

            // A port nothing listens on: every try is refused, and retried.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var settings = NewSettings(DecoderConnection.MyLapsP3);
            var p3 = settings.StreamDecoder(DecoderConnection.MyLapsP3);
            p3.Transport = DecoderTransport.Network;
            p3.Host = "127.0.0.1";
            p3.Port = closedPort;
            settings.Save();

            var window = NewWindow();

            try
            {
                window.Show();
                StaThread.Settle();
                window.ShowPageForLayoutCheck("Connection");
                StaThread.Settle();

                var client = Field<StreamDecoderClient>(window, "_streamClient");
                var status = Field<Label>(window, "lblStatus");
                var connect = Field<Button>(window, "btnConnect");
                var disconnect = Field<Button>(window, "btnDisconnect");

                var connected = (bool)typeof(MainForm)
                    .GetMethod("ConnectStreamDecoder", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { false })!;
                Assert.True(connected);

                var waited = Stopwatch.StartNew();

                while (!(client.State == DecoderConnectionState.Error && status.Text.Contains("press Disconnect to stop", StringComparison.Ordinal))
                       && waited.Elapsed < TimeSpan.FromSeconds(20))
                {
                    StaThread.Settle();
                    Thread.Sleep(50);
                }

                StaThread.Settle();

                Assert.Equal(DecoderConnectionState.Error, client.State);
                Assert.Contains("press Disconnect to stop", status.Text);
                Assert.True(disconnect.Enabled, "Disconnect is greyed out while the client keeps retrying.");
                Assert.True(connect.Enabled);

                disconnect.PerformClick();
                StaThread.Settle();

                Assert.Equal(DecoderConnectionState.Disconnected, client.State);
                Assert.False(disconnect.Enabled);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }

            return 0;
        }, TimeSpan.FromMinutes(2), () => "stream decoder retrying");
    }

    [Fact]
    public void Line_Up_offset_is_turned_round_after_a_restart_that_began_on_a_decoder_from_3_up()
    {
        StaThread.Run(() =>
        {
            using var data = new TempAppData();

            // Line Up measured with the EasyLap as the line.
            var settings = NewSettings(DecoderConnection.EasyLap);
            settings.SplitLoopOffsetMs = 40;
            settings.Save();

            // First run: the line moves to a decoder from 3 up, and the app is closed.
            var first = NewWindow();

            try
            {
                first.Show();
                StaThread.Settle();
                first.ShowPageForLayoutCheck("Connection");
                StaThread.Settle();

                Field<ComboBox>(first, "cmbConnectionType").SelectedIndex = DecoderConnection.IndexOf(DecoderConnection.MyLapsP3);
                StaThread.Settle();

                Assert.Equal(40, Field<AppSettings>(first, "_settings").SplitLoopOffsetMs);
            }
            finally
            {
                first.Close();
                first.Dispose();
                StaThread.Settle();
            }

            // Next run starts on the P3; the line then moves to the I-Lap -
            // the EasyLap is now the split, so the offset runs the other way.
            var second = NewWindow();

            try
            {
                second.Show();
                StaThread.Settle();
                second.ShowPageForLayoutCheck("Connection");
                StaThread.Settle();

                Assert.Equal(DecoderConnection.MyLapsP3, Field<AppSettings>(second, "_settings").LastConnectionType);

                Field<ComboBox>(second, "cmbConnectionType").SelectedIndex = DecoderConnection.IndexOf(DecoderConnection.ILap);
                StaThread.Settle();

                Assert.Equal(-40, Field<AppSettings>(second, "_settings").SplitLoopOffsetMs);
            }
            finally
            {
                second.Close();
                second.Dispose();
                StaThread.Settle();
            }

            Assert.Equal(-40, AppSettings.Load().SplitLoopOffsetMs);

            return 0;
        }, TimeSpan.FromMinutes(2), () => "Line Up offset across a restart");
    }

    private static AppSettings NewSettings(int line) => new()
    {
        StartMaximized = false,
        HasCompletedFirstRun = true,
        HasSeenRaceDaySetup = true,
        AutoBackupEnabled = false,
        SettingsMode = SettingsMode.Advanced,
        SettingsModeDecided = true,
        LastConnectionType = line,
        ConnectDecodersAtStartup = false,
        DirectDecoderAutoConnect = false,
    };

    private static MainForm NewWindow() =>
        new() { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
