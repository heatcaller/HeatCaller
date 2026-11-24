using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HeatCaller;

/// <summary>
/// Locks a whole backup with the club's backup password (15 Nov 2026,
/// children's-privacy plan 2.4).
///
/// WHY: a backup is the entire data folder - every driver's name, email,
/// guardian, photo, check-ins and results, many of them children - and it
/// was a plain zip. Only secrets.hgk inside it was locked. Backups sit in
/// Documents, get copied to USB sticks, and go up to Google Drive, iCloud
/// or OneDrive. With a backup password set, the whole file is now locked,
/// so a copied backup is useless to anyone without the password.
///
/// HOW: the zip is written straight through AES-GCM in 1 MB pieces, never
/// to disk in the clear. The key is stretched from the password with
/// PBKDF2-SHA256 (the same 210,000 rounds SecretBox.PasswordSeal uses).
/// Each piece has its own nonce (a random prefix and its number), and its
/// number and whether it is the last are in what the tag covers, so pieces
/// can't be reordered, dropped or cut off the end without the restore
/// noticing. The file keeps its .zip name, so listing, pruning and cloud
/// copies treat it as before; IsEncrypted tells the two apart by the header.
///
/// Layout (version 1):
///   "HCBKENC" (7 bytes) | version (1) | iterations (int32 LE) | salt (16)
///   | nonce prefix (4) | piece size (int32 LE)
///   then pieces: plain length (int32 LE) | last? (1) | cipher | tag (16)
/// </summary>
internal static class BackupCrypto
{
    /// <summary>What every locked backup starts with. A zip starts "PK".</summary>
    internal static readonly byte[] Magic = "HCBKENC"u8.ToArray();

    private const byte Version = 1;

    internal const int Iterations = 210_000;

    private const int SaltLength = 16;

    private const int NoncePrefixLength = 4;

    private const int TagLength = 16;

    private const int PieceSize = 1024 * 1024;

    private static int HeaderLength => Magic.Length + 1 + 4 + SaltLength + NoncePrefixLength + 4;

    /// <summary>True when the file starts like a locked backup. Never throws.</summary>
    public static bool IsEncrypted(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = new byte[Magic.Length];
            return file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false) == start.Length && start.AsSpan().SequenceEqual(Magic);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// A write-only stream that locks everything written to it into
    /// <paramref name="output"/>. Disposing it writes the last piece - a
    /// backup cut off before that is refused on restore.
    /// </summary>
    public static Stream Encrypt(Stream output, string password, int iterations = Iterations)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("A backup password is needed to lock a backup.", nameof(password));
        }

        return new EncryptingStream(output, password, iterations);
    }

    /// <summary>
    /// Unlocks a locked backup into <paramref name="output"/>. Throws
    /// <see cref="WrongBackupPasswordException"/> when the password does not
    /// open it, and InvalidDataException when the file is damaged or cut short.
    /// </summary>
    public static void Decrypt(Stream input, Stream output, string password) => Decrypt(input, output, password, firstPieceOnly: false);

    private static void Decrypt(Stream input, Stream output, string password, bool firstPieceOnly)
    {
        var header = new byte[HeaderLength];
        if (input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length
            || !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("That isn't a locked HeatCaller backup.");
        }

        if (header[Magic.Length] != Version)
        {
            throw new InvalidDataException("That backup was locked by a newer HeatCaller. Update HeatCaller to restore it.");
        }

        var at = Magic.Length + 1;
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(at, 4));
        at += 4;
        var salt = header.AsSpan(at, SaltLength).ToArray();
        at += SaltLength;
        var prefix = header.AsSpan(at, NoncePrefixLength).ToArray();
        at += NoncePrefixLength;
        var pieceSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(at, 4));

        if (iterations is < 10_000 or > 5_000_000 || pieceSize is < 1 or > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("That backup's lock is damaged.");
        }

        var key = Rfc2898DeriveBytes.Pbkdf2(password ?? string.Empty, salt, iterations, HashAlgorithmName.SHA256, 32);

        try
        {
            using var aes = new AesGcm(key, TagLength);
            var lead = new byte[5];
            var cipher = new byte[pieceSize];
            var plain = new byte[pieceSize];
            var tag = new byte[TagLength];
            long number = 0;

            while (true)
            {
                if (input.ReadAtLeast(lead, lead.Length, throwOnEndOfStream: false) != lead.Length)
                {
                    throw new InvalidDataException("That backup is cut short - it was not finished being written or copied. Nothing has been restored.");
                }

                var length = BinaryPrimitives.ReadInt32LittleEndian(lead.AsSpan(0, 4));
                var last = lead[4] == 1;

                if (length < 0 || length > pieceSize || lead[4] > 1)
                {
                    throw new InvalidDataException("That backup is damaged. Nothing has been restored.");
                }

                if (input.ReadAtLeast(cipher.AsSpan(0, length), length, throwOnEndOfStream: false) != length
                    || input.ReadAtLeast(tag, TagLength, throwOnEndOfStream: false) != TagLength)
                {
                    throw new InvalidDataException("That backup is cut short - it was not finished being written or copied. Nothing has been restored.");
                }

                try
                {
                    aes.Decrypt(NonceFor(prefix, number), cipher.AsSpan(0, length), tag, plain.AsSpan(0, length), Associated(header, number, last));
                }
                catch (CryptographicException) when (number == 0)
                {
                    // The first piece not opening is, all but always, the
                    // wrong password - said as that.
                    throw new WrongBackupPasswordException();
                }
                catch (CryptographicException)
                {
                    throw new InvalidDataException("That backup is damaged partway through. Nothing has been restored.");
                }

                output.Write(plain, 0, length);
                number++;

                if (last || firstPieceOnly)
                {
                    return;
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Whether <paramref name="password"/> opens the locked backup at
    /// <paramref name="path"/>, by opening its first piece only. Slow on
    /// purpose (the key stretching), so not on a timer.
    ///
    /// False only when the password is what stops it. A backup that can't
    /// be read for another reason - cut short, damaged, locked by a newer
    /// HeatCaller, or the file can't be opened just now - is true here, so
    /// the restore goes on and its own unlocking says what is really wrong
    /// (before anything in the data folder is touched).
    /// </summary>
    public static bool PasswordOpens(string path, string password)
    {
        // An empty password never locked a backup (Encrypt refuses one).
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Decrypt(input, Stream.Null, password, firstPieceOnly: true);
            return true;
        }
        catch (WrongBackupPasswordException)
        {
            return false;
        }
        catch
        {
            // WHY (23 Nov 2026, bug hunt 24): this used to return false for
            // every failure, so a locked backup that was cut short, damaged
            // or locked by a newer HeatCaller was reported as "That password
            // doesn't open this backup" - even for the right password, try
            // after try - and the real reason was never shown. Now only a
            // wrong password is false; anything else lets the restore run,
            // and BackupService.RestoreBackup's unlocking refuses it with the
            // worded reason ("cut short", "damaged", "Update HeatCaller")
            // before any file is replaced.
            return true;
        }
    }

    private static byte[] NonceFor(byte[] prefix, long number)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), number);
        return nonce;
    }

    /// <summary>What each piece's tag covers besides the piece: the whole header, its number, and whether it is the last.</summary>
    private static byte[] Associated(byte[] header, long number, bool last)
    {
        var data = new byte[header.Length + 9];
        header.CopyTo(data, 0);
        BinaryPrimitives.WriteInt64BigEndian(data.AsSpan(header.Length), number);
        data[^1] = last ? (byte)1 : (byte)0;
        return data;
    }

    private sealed class EncryptingStream : Stream
    {
        private readonly Stream _output;
        private readonly AesGcm _aes;
        private readonly byte[] _header;
        private readonly byte[] _prefix;
        private readonly byte[] _buffer = new byte[PieceSize];
        private readonly byte[] _cipher = new byte[PieceSize];
        private readonly byte[] _tag = new byte[TagLength];
        private int _filled;
        private long _number;
        private long _written;
        private bool _closed;

        public EncryptingStream(Stream output, string password, int iterations)
        {
            _output = output;
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            _prefix = RandomNumberGenerator.GetBytes(NoncePrefixLength);

            _header = new byte[HeaderLength];
            Magic.CopyTo(_header, 0);
            var at = Magic.Length;
            _header[at++] = Version;
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(at, 4), iterations);
            at += 4;
            salt.CopyTo(_header, at);
            at += SaltLength;
            _prefix.CopyTo(_header, at);
            at += NoncePrefixLength;
            BinaryPrimitives.WriteInt32LittleEndian(_header.AsSpan(at, 4), PieceSize);

            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            _aes = new AesGcm(key, TagLength);
            CryptographicOperations.ZeroMemory(key);

            _output.Write(_header, 0, _header.Length);
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => !_closed;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            ObjectDisposedException.ThrowIf(_closed, this);

            while (data.Length > 0)
            {
                var take = Math.Min(data.Length, PieceSize - _filled);
                data[..take].CopyTo(_buffer.AsSpan(_filled));
                _filled += take;
                _written += take;
                data = data[take..];

                if (_filled == PieceSize)
                {
                    WritePiece(last: false);
                }
            }
        }

        private void WritePiece(bool last)
        {
            var lead = new byte[5];
            BinaryPrimitives.WriteInt32LittleEndian(lead, _filled);
            lead[4] = last ? (byte)1 : (byte)0;

            _aes.Encrypt(NonceFor(_prefix, _number), _buffer.AsSpan(0, _filled), _cipher.AsSpan(0, _filled), _tag, Associated(_header, _number, last));

            _output.Write(lead, 0, lead.Length);
            _output.Write(_cipher, 0, _filled);
            _output.Write(_tag, 0, _tag.Length);

            _number++;
            _filled = 0;
        }

        public override void Flush() => _output.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;

                try
                {
                    WritePiece(last: true);
                    _output.Flush();
                }
                finally
                {
                    _aes.Dispose();
                    CryptographicOperations.ZeroMemory(_buffer);
                    _output.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>The backup password typed doesn't open this backup.</summary>
public sealed class WrongBackupPasswordException : Exception
{
    public WrongBackupPasswordException()
        : base("That password doesn't open this backup.")
    {
    }
}

/// <summary>A locked backup was asked to be restored without a password.</summary>
public sealed class BackupLockedException : Exception
{
    public BackupLockedException()
        : base("This backup is locked with a backup password. Type the password to restore it.")
    {
    }
}
