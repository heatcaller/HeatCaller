namespace HeatCaller;

/// <summary>
/// Stores track layout attachments (a photo or drawing of the physical
/// track) as plain files on disk under one folder per track, rather than
/// embedding binary data in the JSON settings files. Only the original
/// filename is kept on the TrackLayout record itself.
/// </summary>
public static class TrackAttachmentStore
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

    private static string RootFolder => Path.Combine(
        AppPaths.Root,
        "TrackAttachments");

    private const string StashRootName = "_deleted";

    /// <summary>
    /// A track's attachment folder, or null if its id can't safely be one.
    ///
    /// Ids are the app's own Guid strings, but they come back out of
    /// track-layouts.json - which a restored backup writes - and this folder
    /// gets moved and deleted recursively. An id of "..\.." would have
    /// that happen to somewhere else entirely; "_deleted" would be the
    /// stash of every other track.
    /// </summary>
    private static string? FolderFor(string? trackId)
    {
        if (!PlainFileName.IsSafeLeaf(trackId)
            || string.Equals(trackId, StashRootName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var folder = Path.Combine(RootFolder, trackId);
        return PlainFileName.IsInside(RootFolder, folder) ? folder : null;
    }

    /// <summary>
    /// The non-image types an attachment is allowed to be: what the Attach
    /// dialog's own filter offers, and nothing else.
    /// </summary>
    private static readonly string[] DocumentExtensions = { ".pdf", ".dwg", ".dxf" };

    public static bool IsImageFile(string fileName) =>
        ImageExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <summary>
    /// Whether a file of this name may be a track attachment at all.
    ///
    /// An ALLOW-LIST, and it has to be one. PlainFileName.IsSafeLeaf checks
    /// that a name is a plain leaf - no separators, no traversal - and
    /// extensions are none of its business, so "track.hta" passed it happily.
    /// ResolvePath was the one read path in this class that never asked what
    /// kind of file it had found, and the Open button on the edit dialog hands
    /// whatever it returns to Process.Start with UseShellExecute.
    ///
    /// That made a shared backup a code-execution channel: every entry in the
    /// zip lands inside %AppData%\HeatCaller, so the restore's containment
    /// check passes, and a track-layouts.json naming the planted file gives a
    /// volunteer a button next to the words "Photo or drawing" that runs it.
    /// The file is written by a plain FileStream, so there is no
    /// Mark-of-the-Web and no SmartScreen prompt either.
    ///
    /// Checked here rather than at the Open button so that no caller - now or
    /// later - can obtain a path to anything else.
    /// </summary>
    public static bool IsAllowedAttachment(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return ImageExtensions.Contains(extension) || DocumentExtensions.Contains(extension);
    }

    /// <summary>
    /// Loads a track's attachment as a displayable Image, with EXIF
    /// orientation correction applied - see the remarks on
    /// ApplyExifOrientation for why that matters. Returns null if there's
    /// no attachment, it's missing on disk, or it isn't a recognized image
    /// type. Caller owns the returned Image and should Dispose it.
    /// </summary>
    public static Image? LoadPreviewImage(TrackLayout track)
    {
        var path = ResolvePath(track);
        return path is null ? null : LoadPreviewImageFrom(path);
    }

    /// <summary>
    /// The same, for a file that isn't attached yet - the edit dialog shows
    /// a newly picked picture before Save copies it in. Null for anything
    /// that isn't a readable image. Caller owns and disposes it.
    /// </summary>
    public static Image? LoadPreviewImageFrom(string path)
    {
        if (!IsImageFile(path))
        {
            return null;
        }

        try
        {
            // Sized from its header before anything is decoded, and no
            // bigger than Attach takes in gets through (see AttachProblem -
            // a track photo is kept just as it was picked). This used to
            // decode whatever the file held, as the Track Layouts page and
            // the online tracks page showed the track - so a photo planted
            // in a restored backup, too big to decode, froze or crashed the
            // app there every time. It shows no preview instead, as an
            // unreadable picture always has.
            if (ImageProbe.ReadStored(path) is not { } bytes)
            {
                return null;
            }

            // Copied into a Bitmap of its own before the stream closes. An
            // Image from FromStream keeps reading that stream for as long as
            // it lives, so handing back the decoded one - with its stream
            // already disposed - failed later with a "generic error in
            // GDI+" when it was drawn or saved. The orientation is fixed
            // first because the copy doesn't carry the EXIF tag across.
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            ApplyExifOrientation(decoded);
            return new Bitmap(decoded);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// GDI+'s Image.FromStream shows an image's raw pixel data and ignores
    /// its EXIF Orientation tag - the metadata phone cameras use to record
    /// "this needs rotating for display" rather than physically rotating
    /// the stored pixels. Without this, a photo that looks correctly
    /// oriented everywhere else (Windows Explorer, a phone's gallery, most
    /// photo apps - all of which do read this tag) can appear sideways or
    /// upside-down here. The original file on disk is never touched by
    /// this - only the in-memory Image object passed in.
    /// </summary>
    public static void ApplyExifOrientation(Image image)
    {
        const int OrientationPropertyId = 0x0112;

        if (!image.PropertyIdList.Contains(OrientationPropertyId))
        {
            return;
        }

        var property = image.GetPropertyItem(OrientationPropertyId);
        var orientation = property?.Value is { Length: > 0 } value ? value[0] : (byte)1;

        var rotateFlip = orientation switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };

        if (rotateFlip != RotateFlipType.RotateNoneFlipNone)
        {
            image.RotateFlip(rotateFlip);
        }

        // The tag now describes pixels that no longer need rotating -
        // remove it so nothing downstream double-applies the correction.
        image.RemovePropertyItem(OrientationPropertyId);
    }

    /// <summary>Full path to a track's current attachment, or null if it has none or the file is missing.</summary>
    public static string? ResolvePath(TrackLayout track)
    {
        if (string.IsNullOrWhiteSpace(track.AttachmentFileName))
        {
            return null;
        }

        // Same reasoning as FolderFor: the name comes from a file a backup
        // can write, and the path it makes is read, shown - and opened. See
        // IsAllowedAttachment for why the type is checked here and not at the
        // button.
        var folder = FolderFor(track.Id);
        if (folder is null
            || !PlainFileName.IsSafeLeaf(track.AttachmentFileName)
            || !IsAllowedAttachment(track.AttachmentFileName))
        {
            return null;
        }

        var path = Path.Combine(folder, track.AttachmentFileName);
        return PlainFileName.IsInside(folder, path) && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Copies a file in from wherever the user picked it (e.g. via
    /// OpenFileDialog) into this track's attachment folder, replacing any
    /// previous attachment. Sets track.AttachmentFileName on success.
    ///
    /// Returns where the previous attachment folder was set aside (see
    /// StashForTrack), or null when there was none - for an undo of the
    /// edit to put back with RestoreForTrack.
    /// </summary>
    public static string? Attach(TrackLayout track, string sourceFilePath)
    {
        // Checked before the current attachment is set aside, so a refusal
        // leaves things as they were. The caller shows the message.
        if (AttachProblem(track, sourceFilePath) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        var folder = FolderFor(track.Id)!;
        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(folder, fileName);

        // Copied in BEFORE the current attachment is set aside. The other
        // way round, a copy that failed - the file moved or deleted since
        // it was picked, a full disk - left the track with no photo at all,
        // and re-picking the current photo from the track's own folder set
        // aside the very file it was about to copy. Next to the folders
        // rather than in one, so setting the old folder aside can't take
        // it too.
        Directory.CreateDirectory(RootFolder);
        var incoming = Path.Combine(RootFolder, $"{track.Id}.{Guid.NewGuid():N}.incoming");
        string? stashed = null;

        try
        {
            File.Copy(sourceFilePath, incoming);

            // The old photo is set aside, but the track keeps its name until
            // the new one is in place. This used to call RemoveAttachment,
            // which cleared the name first: a move that then failed left
            // the track pointing at nothing, with its photo in the stash.
            stashed = StashForTrack(track.Id);

            // Nothing set aside, yet the old folder is still there: it
            // couldn't be moved (a file in it open elsewhere, say). Going on
            // would move the new photo into it over the old one, and with
            // nothing held the undo couldn't bring the old one back - so it
            // stops here instead, with the track and its photo as they were.
            // The caller shows this message; the copy made above is tidied
            // away below.
            if (stashed is null && Directory.Exists(folder))
            {
                throw new InvalidOperationException(
                    "the track's current photo couldn't be moved aside to keep it for Undo, so nothing was changed. "
                    + "If it is open in another program, close it and try again.");
            }

            try
            {
                Directory.CreateDirectory(folder);
                File.Move(incoming, destPath, overwrite: true);
            }
            catch
            {
                // Put the old photo back where the name still points.
                if (stashed is not null)
                {
                    RestoreForTrack(track.Id, stashed);
                }

                throw;
            }
        }
        finally
        {
            try
            {
                if (File.Exists(incoming))
                {
                    File.Delete(incoming);
                }
            }
            catch
            {
                // A stray copy is harmless; nothing reads it.
            }
        }

        track.AttachmentFileName = fileName;
        return stashed;
    }

    /// <summary>
    /// Why a file can't be attached to a track, or null if it can. The
    /// same checks Attach makes, for the edit dialog to make when the file
    /// is picked - it only attaches on Save, and "that name won't do" is
    /// better said straight away than after everything else is filled in.
    /// </summary>
    public static string? AttachProblem(TrackLayout track, string sourceFilePath)
    {
        if (FolderFor(track.Id) is null)
        {
            return "This track layout's id can't be used as a folder name.";
        }

        if (!PlainFileName.IsSafeLeaf(Path.GetFileName(sourceFilePath)))
        {
            return "That file's name can't be stored as-is - rename it (shorter, no unusual characters) and try again.";
        }

        // The same allow-list ResolvePath applies, said here so the answer
        // comes when the file is picked rather than as a silent nothing later.
        // The picker offers "All files (*.*)", so this is reachable by
        // accident as well as on purpose.
        if (!IsAllowedAttachment(Path.GetFileName(sourceFilePath)))
        {
            return "A track attachment has to be a picture (PNG, JPG, BMP, GIF) or a drawing (PDF, DWG, DXF).";
        }

        // A picture is sized up from its length and its header too, before
        // anything is decoded (see ImageProbe). It is shown as the track's
        // photo, and it used to go in unlooked-at and then be decoded whole
        // for every preview - so a huge photo, or a small file whose header
        // claims tens of thousands of pixels each way, froze the dialog the
        // moment it was picked, and the Track Layouts page whenever the
        // track was selected. A PDF or a drawing file is never decoded, so
        // it is only copied, as before.
        if (IsImageFile(sourceFilePath))
        {
            try
            {
                return ImageProbe.PickedProblem(sourceFilePath, "show as a track photo", "Choose a smaller copy.", out _);
            }
            catch (Exception ex)
            {
                // Gone or locked since it was picked. Said now rather than
                // when Save tries to copy it.
                return ex.Message;
            }
        }

        return null;
    }

    /// <summary>
    /// Clears the current attachment, moving the file aside rather than
    /// destroying it, so a track deletion or a change of photo can still be
    /// got back from the stash. The edit dialog calls this (and Attach)
    /// only on Save: it used to act the moment Remove or Attach File was
    /// pressed, so Cancel didn't bring the old photo back.
    /// </summary>
    /// <remarks>Returns where the folder was set aside, for RestoreForTrack; null when there was nothing.</remarks>
    public static string? RemoveAttachment(TrackLayout track)
    {
        var stashed = StashForTrack(track.Id);

        // The same guard as Attach: nothing set aside, yet the folder is
        // still there - it couldn't be moved (a PDF open in Acrobat, say).
        // Clearing the name anyway left the file behind with nothing
        // pointing at it, and the edit's undo, reading "nothing set aside"
        // as "there was no photo before", then moved that original photo
        // into the undo holding folder for good. The caller shows this.
        if (stashed is null && FolderFor(track.Id) is { } folder && Directory.Exists(folder))
        {
            throw new InvalidOperationException(
                "the track's current photo couldn't be moved aside to keep it for Undo, so nothing was changed. "
                + "If it is open in another program, close it and try again.");
        }

        track.AttachmentFileName = null;
        return stashed;
    }

    /// <summary>
    /// Moves a track's attachments aside rather than destroying them, so
    /// deleting a track layout stays undoable. The previous version deleted
    /// the folder outright, which meant an undo restored the layout record
    /// but its photo was already gone from disk.
    ///
    /// Aside means a folder of its own in the app's undo holding folder (see
    /// UndoHoldingFolder), one per action. It used to be one "_deleted" slot
    /// per track, which the next change to the same track emptied - so with
    /// more than one undo step, undoing the older one found its photo gone.
    ///
    /// Returns where the folder went, for RestoreForTrack - null when there
    /// was nothing to set aside or it couldn't be moved.
    /// </summary>
    public static string? StashForTrack(string trackId)
    {
        var folder = FolderFor(trackId);
        if (folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        return UndoHoldingFolder.MoveFolderIn(folder, UndoHoldingFolder.NewFolder());
    }

    /// <summary>
    /// Brings a set-aside attachment folder back, replacing whatever the
    /// track has now. Pairs with StashForTrack (and Attach and
    /// RemoveAttachment) to undo a track deletion or a change of photo:
    /// <paramref name="stashed"/> is what they returned. Without it, the old
    /// per-track "_deleted" stash is looked in, for anything set aside by an
    /// older version.
    /// </summary>
    public static void RestoreForTrack(string trackId, string? stashed = null)
    {
        // Only ever a folder from the undo holding folder (or the old stash):
        // anything else handed in is not ours to move into the track's place.
        if (stashed is not null && !UndoHoldingFolder.IsHeld(stashed))
        {
            return;
        }

        var stash = stashed ?? StashFolderFor(trackId);
        var folder = FolderFor(trackId);
        if (stash is null || folder is null || !Directory.Exists(stash))
        {
            return;
        }

        try
        {
            // Whatever the track has now is set aside too, not deleted - it
            // may be a photo attached since, and deleting it here was the
            // one place an undo destroyed a file for good. When it can't be
            // moved, the held folder is left where it is instead.
            if (Directory.Exists(folder) && StashForTrack(trackId) is null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
            Directory.Move(stash, folder);
        }
        catch
        {
            // Best effort - the track record still comes back either way.
        }
    }

    private static string? StashFolderFor(string? trackId)
    {
        if (FolderFor(trackId) is null)
        {
            return null;
        }

        var stashRoot = Path.Combine(RootFolder, StashRootName);
        var stash = Path.Combine(stashRoot, trackId!);
        return PlainFileName.IsInside(stashRoot, stash) ? stash : null;
    }

    /// <summary>Permanently removes a track's attachments, including anything stashed. Only for genuine cleanup, not for a deletion that might be undone.</summary>
    public static void DeleteAllForTrack(string trackId)
    {
        foreach (var folder in new[] { FolderFor(trackId), StashFolderFor(trackId) })
        {
            if (folder is null || !Directory.Exists(folder))
            {
                continue;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
