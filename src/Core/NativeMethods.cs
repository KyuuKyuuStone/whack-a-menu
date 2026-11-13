using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ContextMenuCleaner.Core
{
    /// <summary>Minimal Win32 surface used by the tool. No third-party P/Invoke library.</summary>
    internal static class NativeMethods
    {
        private const int MaxString = 1024;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int SHLoadIndirectString(
            string pszSource, StringBuilder pszOutBuf, uint cchOutBuf, IntPtr ppvReserved);

        /// <summary>
        /// Expands a MUI indirect string such as "@%SystemRoot%\system32\shell32.dll,-21787"
        /// into the localized text for the current Windows display language.
        /// Returns the original spec when it is not an indirect string or resolution fails.
        /// </summary>
        public static string LoadIndirectString(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return spec;
            string s = spec.Trim();
            if (s.Length == 0 || s[0] != '@') return s;
            try
            {
                StringBuilder sb = new StringBuilder(MaxString);
                int hr = SHLoadIndirectString(s, sb, (uint)sb.Capacity, IntPtr.Zero);
                if (hr >= 0 && sb.Length > 0) return sb.ToString();
            }
            catch (Exception)
            {
                // shlwapi may be unavailable in a stripped environment; fall through.
            }
            return s;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHDefExtractIcon(
            string pszIconFile, int iIndex, uint uFlags,
            ref IntPtr phiconLarge, ref IntPtr phiconSmall, uint nIconSize);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        /// <summary>
        /// Extracts an icon from an EXE/DLL/ICO. Negative index means a resource id,
        /// which is what "shell32.dll,-30" specs use.
        /// </summary>
        public static Icon ExtractIcon(string path, int index, int size)
        {
            IntPtr large = IntPtr.Zero;
            IntPtr small = IntPtr.Zero;
            try
            {
                uint sizes = (uint)(size | (size << 16));
                int hr = SHDefExtractIcon(path, index, 0, ref large, ref small, sizes);
                IntPtr chosen = (large != IntPtr.Zero) ? large : small;
                if (hr >= 0 && chosen != IntPtr.Zero)
                {
                    Icon tmp = Icon.FromHandle(chosen);
                    Icon clone = (Icon)tmp.Clone();   // detach from the native handle
                    return clone;
                }
            }
            catch (Exception)
            {
                // fall through to the caller's fallback
            }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
            return null;
        }

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr item1, IntPtr item2);

        // Windows 10 1607+ / Windows 11: real system DPI, even when the process is
        // per-monitor DPI aware and GetDeviceCaps reports a logical 96.
        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int index);

        public const int LOGPIXELSX = 88;

        public const int SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNF_IDLIST = 0x0000;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessagePtr(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageText(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCAPTION = 0x0002;
        public const int EM_SETCUEBANNER = 0x1501;

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        public const int ATTACH_PARENT_PROCESS = -1;

        public static void AttachOutputConsole()
        {
            try
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
            }
            catch (Exception)
            {
                // No console available -- the self-test still writes a report file.
            }
        }
    }
}
