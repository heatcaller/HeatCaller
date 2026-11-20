namespace HeatCaller;

/// <summary>
/// MainForm's side of the webcam photo finish (PhotoFinish): following the
/// race for the crossings worth a picture, lending the camera page to the
/// web server, and the two buttons on the Connection page - Open Camera
/// Page and Photo Finish.
/// </summary>
public partial class MainForm
{
    /// <summary>The Photo Finish window while it is open. One at a time.</summary>
    private PhotoFinishForm? _photoFinishForm;

    /// <summary>Called once, from the constructor, after the race and the web server exist.</summary>
    private void WirePhotoFinish()
    {
        _photoFinish.NameOf = DriverNameOf;
        _photoFinish.Attach(_race);

        // From a web server thread: the console marshals for itself.
        _photoFinish.Saved += line => OnClientRawMessage($"{AppTime.LogStampNow()}  {line}");

        _webServer.PhotoFinish = _photoFinish;
    }

    /// <summary>Opens the camera page in this PC's browser - it has to be this PC, since only "localhost" may use a camera.</summary>
    private void OpenCameraPage()
    {
        if (!_webServer.IsRunning)
        {
            AppDialog.Show(this,
                "The finish camera is a page served by HeatCaller's own web server, which isn't running. "
                + "Start it on the Local Screens page, then open the camera again.",
                "Finish camera", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var address = $"http://localhost:{_webServer.Port}/camera";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't open the browser ({ex.Message}). Open {address} in Chrome or Edge on this PC.",
                "Finish camera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Opens the Photo Finish window, or brings it to the front when it is open already.</summary>
    internal void ShowPhotoFinish()
    {
        AppUsageTracker.Note("photo_finish_opened");
        if (_photoFinishForm is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.Activate();
            return;
        }

        _photoFinishForm = new PhotoFinishForm();
        _photoFinishForm.FormClosed += (_, _) => _photoFinishForm = null;
        _photoFinishForm.Show(this);
    }
}
