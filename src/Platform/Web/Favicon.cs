using System.Text;

namespace WinMcp.Platform.Web;

/// <summary>
/// Inline SVG favicon for the platform. Served at <c>/favicon.svg</c> and
/// <c>/favicon.ico</c>. Kept in code (rather than a static asset) so the
/// published binary stays single-file: the installer drops <c>WinMCP.exe</c>
/// and that's enough to serve the dashboard.
/// </summary>
internal static class Favicon
{
    private const string Svg = """
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#7c5cff"/>
      <stop offset="100%" stop-color="#4ad6ff"/>
    </linearGradient>
  </defs>
  <rect width="64" height="64" rx="14" fill="url(#g)"/>
  <text x="32" y="46" font-family="Arial,Helvetica,sans-serif" font-size="40" font-weight="700" text-anchor="middle" fill="#0b1020">W</text>
</svg>
""";

    public static byte[] Bytes { get; } = Encoding.UTF8.GetBytes(Svg);
}
