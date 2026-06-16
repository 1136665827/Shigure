using System.Diagnostics;
using System.Security.Cryptography;

namespace Shigure;

internal static class RandomizedExecutableLauncher
{
    private const string RelaunchedEnvironmentKey = "SHIGURE_RANDOMIZED_PROCESS";
    private const string RandomSuffixMarker = "SHIGURE-RANDOM-HASH-MARKER";
    private const string TemporaryDirectoryName = "tmp";
    private const int RandomNameLength = 16;
    private const int RandomHashBytes = 64;
    private static readonly string[] RuntimeFileExtensions = [".dll", ".pdb"];
    private static readonly byte[] RandomSuffixMarkerBytes = System.Text.Encoding.UTF8.GetBytes(RandomSuffixMarker);
    private static readonly char[] FileNameChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    public static RandomizedRelaunchResult TryRelaunch(string[] args)
    {
        if (Environment.GetEnvironmentVariable(RelaunchedEnvironmentKey) == "1")
        {
            return RandomizedRelaunchResult.AlreadyRelaunched;
        }

        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
        {
            return RandomizedRelaunchResult.Failed;
        }

        var extension = Path.GetExtension(currentExecutable);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return RandomizedRelaunchResult.Failed;
        }

        try
        {
            CleanupTemporaryRoot(currentExecutable);
            var randomizedExecutable = CreateRandomizedExecutable(currentExecutable);
            var randomizedDirectory = Path.GetDirectoryName(randomizedExecutable);
            var startInfo = new ProcessStartInfo(randomizedExecutable)
            {
                UseShellExecute = false,
                WorkingDirectory = randomizedDirectory ?? Environment.CurrentDirectory
            };
            startInfo.Environment[RelaunchedEnvironmentKey] = "1";
            startInfo.Environment[AppPaths.OriginalBaseDirectoryEnvironmentKey] = Path.GetDirectoryName(currentExecutable) ?? AppContext.BaseDirectory;
            startInfo.Environment[AppPaths.RandomizedDisplayNameEnvironmentKey] = Path.GetFileNameWithoutExtension(randomizedExecutable);
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            return Process.Start(startInfo) is null
                ? RandomizedRelaunchResult.Failed
                : RandomizedRelaunchResult.Started;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"无法创建随机运行文件: {ex.Message}",
                "Shigure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return RandomizedRelaunchResult.Failed;
        }
    }

    private static void CleanupTemporaryRoot(string sourcePath)
    {
        var tempRoot = GetTemporaryRoot(sourcePath);
        if (!Directory.Exists(tempRoot))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(tempRoot))
        {
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        foreach (var directory in Directory.EnumerateDirectories(tempRoot))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                continue;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            // 整个目录删不掉时，逐个文件清理能删的部分
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            try { Directory.Delete(directory, recursive: false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        try { Directory.Delete(tempRoot, recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string CreateRandomizedExecutable(string sourcePath)
    {
        var sourceDirectory = Path.GetDirectoryName(sourcePath) ?? AppContext.BaseDirectory;
        var tempRoot = GetTemporaryRoot(sourcePath);
        Directory.CreateDirectory(tempRoot);

        string runDirectory;
        do
        {
            runDirectory = Path.Combine(tempRoot, CreateRandomFileName());
        }
        while (Directory.Exists(runDirectory));

        Directory.CreateDirectory(runDirectory);
        CopyRuntimeFiles(sourceDirectory, runDirectory);

        string targetPath;
        do
        {
            targetPath = Path.Combine(runDirectory, $"{CreateRandomFileName()}.exe");
        }
        while (File.Exists(targetPath));

        File.Copy(sourcePath, targetPath, overwrite: false);
        AppendRandomHashMarker(targetPath);
        return targetPath;
    }

    private static string GetTemporaryRoot(string sourcePath)
    {
        var normalized = sourcePath.ToUpperInvariant();
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var folderName = Convert.ToHexString(hashBytes)[..RandomNameLength];
        return Path.Combine(Path.GetTempPath(), folderName, TemporaryDirectoryName);
    }

    private static void CopyRuntimeFiles(string sourceDirectory, string targetDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (!ShouldCopyRuntimeFile(file))
            {
                continue;
            }

            File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: true);
        }

    }

    private static bool ShouldCopyRuntimeFile(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return RuntimeFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateRandomFileName()
    {
        Span<char> buffer = stackalloc char[RandomNameLength];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = FileNameChars[RandomNumberGenerator.GetInt32(FileNameChars.Length)];
        }

        return new string(buffer);
    }

    private static bool IsRandomizedExecutableName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Length == RandomNameLength
            && name.All(char.IsAsciiLetterOrDigit);
    }

    private static bool IsRandomizedDirectoryName(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == RandomNameLength
            && name.All(char.IsAsciiLetterOrDigit);
    }

    private static bool ContainsRandomizedExecutable(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .Any(file => IsRandomizedExecutableName(file) && HasRandomHashMarker(file));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasRandomHashMarker(string path)
    {
        try
        {
            var markerLength = RandomSuffixMarkerBytes.Length;
            var minimumLength = markerLength + RandomHashBytes;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < minimumLength)
            {
                return false;
            }

            stream.Position = stream.Length - minimumLength;
            Span<byte> markerBuffer = stackalloc byte[markerLength];
            return stream.Read(markerBuffer) == markerLength
                && markerBuffer.SequenceEqual(RandomSuffixMarkerBytes);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void AppendRandomHashMarker(string path)
    {
        Span<byte> randomBytes = stackalloc byte[RandomHashBytes];
        RandomNumberGenerator.Fill(randomBytes);

        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream);
        writer.Write(RandomSuffixMarker);
        writer.Write(randomBytes);
    }
}

internal enum RandomizedRelaunchResult
{
    Started,
    AlreadyRelaunched,
    Failed
}
