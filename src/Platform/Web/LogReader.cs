namespace WinMcp.Platform.Web;

/// <summary>
/// Reads the trailing N lines of a (potentially large) text file by seeking
/// backward from EOF in fixed-size blocks and counting newlines. Avoids
/// loading the full file into memory — the daily log file routinely exceeds
/// 100 MB on a busy host and <c>/logs/tail</c> is polled every 3 s by the
/// dashboard.
/// </summary>
/// <remarks>
/// Carried forward from math-mcp v1.0.20 where the original implementation
/// fixed a missing seek-from-EOF that caused the dashboard to allocate the
/// full daily log on each poll.
/// </remarks>
internal static class LogReader
{
    public static string ReadLastLines(string path, int count)
    {
        const int BlockSize = 8192;
        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (fs.Length == 0) return string.Empty;

        var buffer = new byte[BlockSize];
        var position = fs.Length;
        var newlinesRemaining = count;
        var firstIteration = true;
        long foundStart = 0;
        var done = false;

        while (position > 0 && !done)
        {
            var toRead = (int)Math.Min(BlockSize, position);
            position -= toRead;
            fs.Position = position;
            fs.ReadExactly(buffer, 0, toRead);

            var scanFrom = toRead - 1;
            // Skip a trailing newline on the first iteration so it doesn't
            // count as one of the N lines we're asked to return.
            if (firstIteration && buffer[scanFrom] == (byte)'\n') scanFrom--;
            firstIteration = false;

            for (var i = scanFrom; i >= 0; i--)
            {
                if (buffer[i] != (byte)'\n') continue;
                newlinesRemaining--;
                if (newlinesRemaining == 0)
                {
                    foundStart = position + i + 1;
                    done = true;
                    break;
                }
            }
        }

        fs.Position = done ? foundStart : 0;
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
}
