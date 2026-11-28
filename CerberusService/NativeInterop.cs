using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace RoomAgent
{
    internal static class NativeInterop
    {
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_COMMAND = 0x0111;
        private const int MIN_ALL = 419;
        private const uint ABM_GETSTATE = 0x00000004;
        private const uint ABM_SETSTATE = 0x0000000A;
        private const uint ABS_AUTOHIDE = 0x00000001;
        private const int FCIDM_SHVIEW_TOGGLE_ICONS = 0x7402;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x1000;
        private const ushort VK_BACK = 0x08;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_LEFT = 0x25;
        private const ushort VK_RIGHT = 0x27;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_TAB = 0x09;
        private const ushort VK_D = 0x44;
        private const ushort VK_F4 = 0x73;
        private const ushort VK_F11 = 0x7A;
        private const string VirtualDesktopsRegPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPLACEMENT
        {
            public uint length;
            public uint flags;
            public uint showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        private const uint SW_SHOWNORMAL = 1;
        private const uint SW_SHOWMINIMIZED = 2;
        private const uint SW_SHOWMAXIMIZED = 3;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int SW_MAXIMIZE = 3;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int DWMWA_CLOAKED = 14;
        internal const int DWM_CLOAKED_APP = 0x00000001;
        internal const int DWM_CLOAKED_SHELL = 0x00000002;
        internal const int DWM_CLOAKED_INHERITED = 0x00000004;

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState_1_4(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SYSICONINDEX = 0x000004000;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint ILD_TRANSPARENT = 0x00000001;

        public static string ExtractShortcutIconDataUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                string urlIcon = TryExtractUrlShortcutIconDataUrl(path);
                if (!string.IsNullOrEmpty(urlIcon))
                {
                    return urlIcon;
                }
            }

            return ExtractShellFileIconDataUrl(path);
        }

        private static string TryExtractUrlShortcutIconDataUrl(string urlPath)
        {
            try
            {
                string iconFile = null;
                int iconIndex = 0;

                foreach (string rawLine in File.ReadAllLines(urlPath))
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                    {
                        iconFile = Environment.ExpandEnvironmentVariables(line.Substring("IconFile=".Length).Trim().Trim('"'));
                    }
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (int.TryParse(line.Substring("IconIndex=".Length).Trim(), out parsed))
                        {
                            iconIndex = parsed;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(iconFile) || !File.Exists(iconFile))
                {
                    return null;
                }

                if (iconFile.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    using (Icon icon = new Icon(iconFile, 32, 32))
                    {
                        return IconToPngDataUrl(icon);
                    }
                }

                if (iconFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    iconFile.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    iconFile.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    iconFile.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    using (Image img = Image.FromFile(iconFile))
                    using (Bitmap bmp = new Bitmap(img, new Size(32, 32)))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }

                IntPtr[] largeIcons = new IntPtr[1];
                uint extracted = ExtractIconEx(iconFile, iconIndex, largeIcons, null, 1);
                if (extracted > 0 && largeIcons[0] != IntPtr.Zero)
                {
                    try
                    {
                        using (Icon icon = Icon.FromHandle(largeIcons[0]))
                        {
                            return IconToPngDataUrl(icon);
                        }
                    }
                    finally
                    {
                        DestroyIcon(largeIcons[0]);
                    }
                }

                return ExtractShellFileIconDataUrl(iconFile);
            }
            catch
            {
                return null;
            }
        }

        internal static string ExtractShellFileIconDataUrl(string path)
        {
            SHFILEINFO shfi = new SHFILEINFO();
            IntPtr himl = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_SYSICONINDEX | SHGFI_LARGEICON);
            if (himl != IntPtr.Zero)
            {
                // Extracting via the system image list omits shell icon overlays (such as the shortcut arrow).
                IntPtr hIcon = ImageList_GetIcon(himl, shfi.iIcon, ILD_TRANSPARENT);
                if (hIcon != IntPtr.Zero)
                {
                    try
                    {
                        using (Icon icon = Icon.FromHandle(hIcon))
                        {
                            return IconToPngDataUrl(icon);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }

            shfi = new SHFILEINFO();
            IntPtr hImg = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_ICON | SHGFI_LARGEICON);
            if (hImg == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                using (Icon icon = Icon.FromHandle(shfi.hIcon))
                {
                    return IconToPngDataUrl(icon);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }

        private static string IconToPngDataUrl(Icon icon)
        {
            using (Bitmap bmp = icon.ToBitmap())
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }

        private const int CR_SUCCESS = 0;
        private const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
        private const uint CM_GETIDLIST_FILTER_ENUMERATOR = 0x00000001;
        private const uint CM_GETIDLIST_FILTER_PRESENT = 0x00000100;
        private const uint DN_STARTED = 0x00000008;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_ID_List_SizeW(out uint pulLen, string pszFilter, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_ID_ListW(string pszFilter, char[] Buffer, uint BufferLen, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_Child(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_Sibling(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern int CM_Get_Device_IDW(uint dnDevInst, StringBuilder Buffer, int BufferLen, uint ulFlags);

        [DllImport("cfgmgr32.dll")]
        private static extern int CM_Get_DevNode_Status(out uint pulStatus, out uint pulProblemNumber, uint dnDevInst, uint ulFlags);

        public static bool IsTaskbarAutoHide()
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = FindWindow("Shell_TrayWnd", null);
            uint state = (uint)SHAppBarMessage(ABM_GETSTATE, ref abd);
            return (state & ABS_AUTOHIDE) != 0;
        }

        public static bool SetTaskbarAutoHide(bool autoHide)
        {
            APPBARDATA abd = new APPBARDATA();
            abd.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            abd.hWnd = FindWindow("Shell_TrayWnd", null);
            uint currentState = (uint)SHAppBarMessage(ABM_GETSTATE, ref abd);
            uint newState = autoHide ? (currentState | ABS_AUTOHIDE) : (currentState & ~ABS_AUTOHIDE);
            abd.lParam = new IntPtr(newState);
            SHAppBarMessage(ABM_SETSTATE, ref abd);
            return IsTaskbarAutoHide() == autoHide;
        }

        private static bool FindDesktopShellViewAndListView(out IntPtr shellView, out IntPtr listView)
        {
            shellView = IntPtr.Zero;
            listView = IntPtr.Zero;

            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            }

            if (shellView == IntPtr.Zero)
            {
                IntPtr found = IntPtr.Zero;
                EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    IntPtr child = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (child != IntPtr.Zero)
                    {
                        found = child;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
                shellView = found;
            }

            if (shellView != IntPtr.Zero)
            {
                listView = FindWindowEx(shellView, IntPtr.Zero, "SysListView32", null);
            }

            return shellView != IntPtr.Zero && listView != IntPtr.Zero;
        }

        public static bool AreDesktopIconsVisible(out bool visible)
        {
            visible = false;
            IntPtr shellView;
            IntPtr listView;
            if (!FindDesktopShellViewAndListView(out shellView, out listView))
            {
                return false;
            }

            visible = IsWindowVisible(listView);
            return true;
        }

        public static bool SetDesktopIconsVisible(bool visible)
        {
            IntPtr shellView;
            IntPtr listView;
            if (!FindDesktopShellViewAndListView(out shellView, out listView))
            {
                return false;
            }

            if (IsWindowVisible(listView) == visible)
            {
                return true;
            }

            IntPtr result;
            SendMessageTimeout(shellView, WM_COMMAND, new IntPtr(FCIDM_SHVIEW_TOGGLE_ICONS), IntPtr.Zero,
                SMTO_ABORTIFHUNG, 1000, out result);
            return IsWindowVisible(listView) == visible;
        }

        public static Guid GetCurrentVirtualDesktopId()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsRegPath, false))
                {
                    if (key == null)
                    {
                        return Guid.Empty;
                    }
                    byte[] bytes = key.GetValue("CurrentVirtualDesktop") as byte[];
                    if (bytes == null || bytes.Length != 16)
                    {
                        return Guid.Empty;
                    }
                    return new Guid(bytes);
                }
            }
            catch
            {
                return Guid.Empty;
            }
        }

        public static Guid[] GetVirtualDesktopIds()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(VirtualDesktopsRegPath, false))
                {
                    if (key == null)
                    {
                        return new Guid[0];
                    }
                    byte[] bytes = key.GetValue("VirtualDesktopIDs") as byte[];
                    if (bytes == null || bytes.Length < 16 || bytes.Length % 16 != 0)
                    {
                        return new Guid[0];
                    }
                    int count = bytes.Length / 16;
                    Guid[] ids = new Guid[count];
                    byte[] chunk = new byte[16];
                    for (int i = 0; i < count; i++)
                    {
                        Buffer.BlockCopy(bytes, i * 16, chunk, 0, 16);
                        ids[i] = new Guid(chunk);
                    }
                    return ids;
                }
            }
            catch
            {
                return new Guid[0];
            }
        }

        public static bool SwitchToNewVirtualDesktop(out Guid newDesktopId)
        {
            Guid beforeId = GetCurrentVirtualDesktopId();
            int beforeCount = GetVirtualDesktopIds().Length;

            if (!SendWinCtrlChord(VK_D))
            {
                newDesktopId = Guid.Empty;
                return false;
            }

            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(50);
                Guid currentId = GetCurrentVirtualDesktopId();
                int currentCount = GetVirtualDesktopIds().Length;
                if ((currentId != Guid.Empty && currentId != beforeId) || currentCount > beforeCount)
                {
                    newDesktopId = currentId;
                    return true;
                }
            }

            newDesktopId = GetCurrentVirtualDesktopId();
            return false;
        }

        public static bool SwitchToVirtualDesktop(Guid targetDesktopId)
        {
            if (targetDesktopId == Guid.Empty)
            {
                return false;
            }

            Guid currentId = GetCurrentVirtualDesktopId();
            if (currentId == targetDesktopId)
            {
                return true;
            }

            Guid[] ids = GetVirtualDesktopIds();
            int maxSteps = ids.Length + 2;
            for (int step = 0; step < maxSteps; step++)
            {
                ids = GetVirtualDesktopIds();
                currentId = GetCurrentVirtualDesktopId();
                if (currentId == targetDesktopId)
                {
                    return true;
                }

                int currentIndex = Array.IndexOf(ids, currentId);
                int targetIndex = Array.IndexOf(ids, targetDesktopId);
                if (currentIndex < 0 || targetIndex < 0)
                {
                    return false;
                }

                ushort arrowKey = targetIndex > currentIndex ? VK_RIGHT : VK_LEFT;
                if (!SendWinCtrlChord(arrowKey, true))
                {
                    return false;
                }

                Guid nextId = currentId;
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(30);
                    nextId = GetCurrentVirtualDesktopId();
                    if (nextId != Guid.Empty && nextId != currentId)
                    {
                        break;
                    }
                }

                if (nextId == currentId)
                {
                    return false;
                }
            }

            return GetCurrentVirtualDesktopId() == targetDesktopId;
        }

        public static bool CloseCurrentVirtualDesktop(Guid expectedDesktopId)
        {
            Guid[] ids = GetVirtualDesktopIds();
            if (expectedDesktopId != Guid.Empty && Array.IndexOf(ids, expectedDesktopId) < 0)
            {
                return true;
            }

            if (ids.Length <= 1)
            {
                return false;
            }

            Guid originalId = GetCurrentVirtualDesktopId();
            if (expectedDesktopId != Guid.Empty && originalId != Guid.Empty && originalId != expectedDesktopId)
            {
                if (!SwitchToVirtualDesktop(expectedDesktopId))
                {
                    return false;
                }

                ids = GetVirtualDesktopIds();
                if (ids.Length <= 1)
                {
                    return false;
                }
            }

            Guid currentId = GetCurrentVirtualDesktopId();
            if (!SendWinCtrlChord(VK_F4))
            {
                return false;
            }

            bool closed = false;
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(50);
                if (GetCurrentVirtualDesktopId() != currentId || GetVirtualDesktopIds().Length < ids.Length)
                {
                    closed = true;
                    break;
                }
            }

            if (closed &&
                originalId != Guid.Empty &&
                originalId != expectedDesktopId &&
                GetCurrentVirtualDesktopId() != originalId &&
                Array.IndexOf(GetVirtualDesktopIds(), originalId) >= 0)
            {
                SwitchToVirtualDesktop(originalId);
            }

            return closed;
        }

        public static bool MinimizeAllWindows()
        {
            IntPtr trayWnd = FindWindow("Shell_TrayWnd", null);
            if (trayWnd == IntPtr.Zero)
            {
                return false;
            }
            return PostMessage(trayWnd, WM_COMMAND, new IntPtr(MIN_ALL), IntPtr.Zero);
        }

        public static List<long> GetNonMinimizedWindowHandles()
        {
            List<long> handles = new List<long>();
            foreach (OpenWindowInfo win in EnumerateOpenWindows())
            {
                IntPtr hWnd = new IntPtr(win.Hwnd);
                if (IsWindow(hWnd) && !IsIconic(hWnd))
                {
                    handles.Add(win.Hwnd);
                }
            }
            return handles;
        }

        public static int RestoreWindows(IList<long> windowHandles)
        {
            if (windowHandles == null || windowHandles.Count == 0)
            {
                return 0;
            }

            int restored = 0;
            // Restore in reverse Z-order so the previously top-most window ends up on top.
            for (int i = windowHandles.Count - 1; i >= 0; i--)
            {
                long hwndValue = windowHandles[i];
                if (hwndValue == 0)
                {
                    continue;
                }

                IntPtr hWnd = new IntPtr(hwndValue);
                if (!IsWindow(hWnd) || !IsIconic(hWnd))
                {
                    continue;
                }

                if (ShowWindow(hWnd, SW_RESTORE))
                {
                    restored++;
                }
            }
            return restored;
        }

        /// <summary>
        /// Returns true if a window's DWMWA_CLOAKED bitmask should exclude it from enumeration.
        /// App-cloaked and inherited-cloaked windows (e.g. suspended UWP apps) are always skipped;
        /// shell-cloaked windows (Xbox mode, inactive virtual desktops) are only kept when requested.
        /// </summary>
        internal static bool ShouldSkipCloakedWindow(int cloaked, bool includeShellCloaked)
        {
            if (cloaked == 0)
            {
                return false;
            }

            if (!includeShellCloaked)
            {
                return true;
            }

            return (cloaked & ~DWM_CLOAKED_SHELL) != 0;
        }

        private static bool TryGetTrackableWindowInfo(IntPtr hWnd, int selfPid, bool includeShellCloaked, out int pid, out string title)
        {
            pid = 0;
            title = null;

            if (!IsWindowVisible(hWnd))
            {
                return false;
            }

            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0)
            {
                return false;
            }

            int cloaked = 0;
            try
            {
                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, Marshal.SizeOf(typeof(int))) == 0 &&
                    ShouldSkipCloakedWindow(cloaked, includeShellCloaked))
                {
                    return false;
                }
            }
            catch
            {
            }

            int titleLen = GetWindowTextLength(hWnd);
            if (titleLen <= 0)
            {
                return false;
            }

            StringBuilder classSb = new StringBuilder(128);
            GetClassName(hWnd, classSb, classSb.Capacity);
            string className = classSb.ToString();
            if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            uint rawPid;
            GetWindowThreadProcessId(hWnd, out rawPid);
            if (rawPid == 0 || (int)rawPid == selfPid)
            {
                return false;
            }

            StringBuilder titleSb = new StringBuilder(titleLen + 1);
            GetWindowText(hWnd, titleSb, titleSb.Capacity);
            string trimmedTitle = titleSb.ToString().Trim();
            if (string.IsNullOrEmpty(trimmedTitle))
            {
                return false;
            }

            pid = (int)rawPid;
            title = trimmedTitle;
            return true;
        }

        public static List<SavedWindowPlacement> CaptureOpenWindowPlacements()
        {
            List<SavedWindowPlacement> placements = new List<SavedWindowPlacement>();
            int selfPid = Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                int pid;
                string unusedTitle;
                if (!TryGetTrackableWindowInfo(hWnd, selfPid, false, out pid, out unusedTitle))
                {
                    return true;
                }

                WINDOWPLACEMENT wp = new WINDOWPLACEMENT
                {
                    length = (uint)Marshal.SizeOf(typeof(WINDOWPLACEMENT))
                };
                if (!GetWindowPlacement(hWnd, ref wp))
                {
                    return true;
                }

                SavedWindowPlacement entry = new SavedWindowPlacement
                {
                    Hwnd = hWnd.ToInt64(),
                    Pid = pid,
                    Flags = wp.flags,
                    ShowCmd = wp.showCmd,
                    MinX = wp.ptMinPosition.x,
                    MinY = wp.ptMinPosition.y,
                    MaxX = wp.ptMaxPosition.x,
                    MaxY = wp.ptMaxPosition.y,
                    NormalLeft = wp.rcNormalPosition.left,
                    NormalTop = wp.rcNormalPosition.top,
                    NormalRight = wp.rcNormalPosition.right,
                    NormalBottom = wp.rcNormalPosition.bottom
                };

                if (wp.showCmd == SW_SHOWNORMAL)
                {
                    RECT rect;
                    if (GetWindowRect(hWnd, out rect))
                    {
                        entry.HasWindowRect = true;
                        entry.RectLeft = rect.left;
                        entry.RectTop = rect.top;
                        entry.RectRight = rect.right;
                        entry.RectBottom = rect.bottom;
                    }
                }

                placements.Add(entry);
                return true;
            }, IntPtr.Zero);

            return placements;
        }

        public static int RestoreWindowPlacements(IList<SavedWindowPlacement> placements)
        {
            if (placements == null || placements.Count == 0)
            {
                return 0;
            }

            int restored = 0;
            // Restore in reverse Z-order so the previously top-most window stays on top.
            for (int i = placements.Count - 1; i >= 0; i--)
            {
                SavedWindowPlacement p = placements[i];
                if (p == null || p.Hwnd == 0)
                {
                    continue;
                }

                IntPtr hWnd = new IntPtr(p.Hwnd);
                if (!IsWindow(hWnd))
                {
                    continue;
                }

                if (p.Pid > 0)
                {
                    uint currentPid;
                    GetWindowThreadProcessId(hWnd, out currentPid);
                    if (currentPid != (uint)p.Pid)
                    {
                        continue;
                    }
                }

                WINDOWPLACEMENT wp = new WINDOWPLACEMENT
                {
                    length = (uint)Marshal.SizeOf(typeof(WINDOWPLACEMENT)),
                    flags = p.Flags,
                    showCmd = p.ShowCmd,
                    ptMinPosition = new POINT { x = p.MinX, y = p.MinY },
                    ptMaxPosition = new POINT { x = p.MaxX, y = p.MaxY },
                    rcNormalPosition = new RECT
                    {
                        left = p.NormalLeft,
                        top = p.NormalTop,
                        right = p.NormalRight,
                        bottom = p.NormalBottom
                    }
                };

                bool ok;
                if (p.ShowCmd == SW_SHOWMAXIMIZED)
                {
                    // Place normal bounds first so Windows maximizes the window on its original monitor.
                    wp.showCmd = SW_SHOWNORMAL;
                    SetWindowPlacement(hWnd, ref wp);
                    wp.showCmd = SW_SHOWMAXIMIZED;
                    ok = SetWindowPlacement(hWnd, ref wp);
                }
                else
                {
                    ok = SetWindowPlacement(hWnd, ref wp);
                    if (p.ShowCmd == SW_SHOWNORMAL && p.HasWindowRect)
                    {
                        int width = p.RectRight - p.RectLeft;
                        int height = p.RectBottom - p.RectTop;
                        if (width > 0 && height > 0)
                        {
                            SetWindowPos(hWnd, IntPtr.Zero, p.RectLeft, p.RectTop, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
                        }
                    }
                }

                if (ok)
                {
                    restored++;
                }
            }

            return restored;
        }

        public static List<OpenWindowInfo> EnumerateOpenWindows(bool includeShellCloaked = false)
        {
            List<OpenWindowInfo> windows = new List<OpenWindowInfo>();
            Dictionary<string, string> iconByExePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int selfPid = Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                int pid;
                string title;
                if (!TryGetTrackableWindowInfo(hWnd, selfPid, includeShellCloaked, out pid, out title))
                {
                    return true;
                }

                string processName = "";
                string iconDataUrl = null;
                try
                {
                    using (Process proc = Process.GetProcessById(pid))
                    {
                        processName = proc.ProcessName;
                        try
                        {
                            string exePath = proc.MainModule != null ? proc.MainModule.FileName : null;
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                if (!iconByExePath.TryGetValue(exePath, out iconDataUrl) && File.Exists(exePath))
                                {
                                    iconDataUrl = ExtractShellFileIconDataUrl(exePath);
                                    iconByExePath[exePath] = iconDataUrl;
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }

                windows.Add(new OpenWindowInfo
                {
                    Hwnd = hWnd.ToInt64(),
                    Pid = pid,
                    ProcessName = processName,
                    Title = title,
                    IconDataUrl = iconDataUrl
                });

                return true;
            }, IntPtr.Zero);

            return windows;
        }

        public static bool CloseWindowByHandle(long hwndValue)
        {
            if (hwndValue == 0)
            {
                return false;
            }

            IntPtr hWnd = new IntPtr(hwndValue);
            if (!IsWindow(hWnd))
            {
                return false;
            }

            return PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static bool FocusWindowByHandle(long hwndValue)
        {
            if (hwndValue == 0)
            {
                return false;
            }

            IntPtr hWnd = new IntPtr(hwndValue);
            if (!IsWindow(hWnd))
            {
                return false;
            }

            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            return BringWindowToForeground(hWnd);
        }

        public static bool MaximizeWindowByHandle(long hwndValue)
        {
            if (hwndValue == 0)
            {
                return false;
            }

            IntPtr hWnd = new IntPtr(hwndValue);
            if (!IsWindow(hWnd))
            {
                return false;
            }

            ShowWindow(hWnd, SW_MAXIMIZE);
            return BringWindowToForeground(hWnd);
        }

        private static bool BringWindowToForeground(IntPtr hWnd)
        {
            IntPtr fgWnd = GetForegroundWindow();
            uint unusedPid;
            uint fgThread = fgWnd != IntPtr.Zero ? GetWindowThreadProcessId(fgWnd, out unusedPid) : 0;
            uint curThread = GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != curThread)
            {
                attached = AttachThreadInput(curThread, fgThread, true);
            }

            try
            {
                bool ok = SetForegroundWindow(hWnd);
                return ok || GetForegroundWindow() == hWnd;
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(curThread, fgThread, false);
                }
            }
        }

        public static long GetForegroundWindowHandle()
        {
            return GetForegroundWindow().ToInt64();
        }

        public static bool ToggleXboxMode()
        {
            return SendWinChord(VK_F11);
        }

        public static bool OpenTaskView()
        {
            return SendWinChord(VK_TAB);
        }

        private static bool SendWinChord(ushort key)
        {
            INPUT[] inputs = new INPUT[]
            {
                CreateKeyInput(VK_LWIN, KEYEVENTF_EXTENDEDKEY),
                CreateKeyInput(key, 0),
                CreateKeyInput(key, KEYEVENTF_KEYUP),
                CreateKeyInput(VK_LWIN, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP)
            };

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Length;
        }

        private static bool SendWinCtrlChord(ushort key, bool extendedKey = false)
        {
            uint downFlags = extendedKey ? KEYEVENTF_EXTENDEDKEY : 0;
            uint upFlags = (extendedKey ? KEYEVENTF_EXTENDEDKEY : 0) | KEYEVENTF_KEYUP;
            INPUT[] inputs = new INPUT[]
            {
                CreateKeyInput(VK_LWIN, KEYEVENTF_EXTENDEDKEY),
                CreateKeyInput(VK_CONTROL, 0),
                CreateKeyInput(key, downFlags),
                CreateKeyInput(key, upFlags),
                CreateKeyInput(VK_CONTROL, KEYEVENTF_KEYUP),
                CreateKeyInput(VK_LWIN, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP)
            };

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Length;
        }

        private const uint SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPI_GETDESKWALLPAPER = 0x0073;
        private const uint SPIF_UPDATEINIFILE = 0x01;
        private const uint SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

        public static string GetCurrentDesktopWallpaper()
        {
            StringBuilder sb = new StringBuilder(520);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
            {
                return sb.ToString();
            }
            return null;
        }

        public static bool SetDesktopWallpaper(string imagePath)
        {
            return SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath ?? "", SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        public static bool SetCurrentDesktopSolidColorWallpaper(string hexColor)
        {
            byte r, g, b;
            if (!TryParseHexColor(hexColor, out r, out g, out b))
            {
                return false;
            }

            try
            {
                const int size = 16;
                const int rowBytes = size * 3;
                const int pixelBytes = rowBytes * size;
                const int fileSize = 54 + pixelBytes;

                string bmpPath = Path.Combine(Path.GetTempPath(), "room-agent-bg.bmp");
                byte[] bmp = new byte[fileSize];
                bmp[0] = (byte)'B';
                bmp[1] = (byte)'M';
                bmp[2] = (byte)(fileSize & 0xFF);
                bmp[3] = (byte)((fileSize >> 8) & 0xFF);
                bmp[4] = (byte)((fileSize >> 16) & 0xFF);
                bmp[5] = (byte)((fileSize >> 24) & 0xFF);
                bmp[10] = 54;
                bmp[14] = 40;
                bmp[18] = size;
                bmp[22] = size;
                bmp[26] = 1;
                bmp[28] = 24;
                bmp[34] = (byte)(pixelBytes & 0xFF);
                bmp[35] = (byte)((pixelBytes >> 8) & 0xFF);

                for (int i = 54; i < fileSize; i += 3)
                {
                    bmp[i] = b;
                    bmp[i + 1] = g;
                    bmp[i + 2] = r;
                }

                File.WriteAllBytes(bmpPath, bmp);
                return SetDesktopWallpaper(bmpPath);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseHexColor(string hexColor, out byte r, out byte g, out byte b)
        {
            r = 0;
            g = 0;
            b = 0;
            if (string.IsNullOrWhiteSpace(hexColor))
            {
                return false;
            }

            string s = hexColor.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal))
            {
                s = s.Substring(1);
            }
            if (s.Length != 6)
            {
                return false;
            }

            return byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                   byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                   byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        private static INPUT CreateKeyInput(ushort vk, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.dwFlags = flags;
            return input;
        }

        public static int CloseSteamMainWindow()
        {
            return CloseVisibleWindows(new[] { "steam", "steamwebhelper" }, "Steam");
        }

        public static int CloseHueSyncWindow(string processName)
        {
            return CloseProcessWindows(processName, "Philips Hue Sync");
        }

        public static int CloseProcessWindows(string processName, string exactTitle = null)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return 0;
            }
            return CloseVisibleWindows(new[] { processName }, exactTitle);
        }

        private static int CloseVisibleWindows(string[] processNames, string exactTitle)
        {
            int closed = 0;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                int len = GetWindowTextLength(hWnd);
                if (len <= 0)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(exactTitle))
                {
                    StringBuilder sb = new StringBuilder(len + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    if (!string.Equals(sb.ToString(), exactTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                uint pid;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid == 0)
                {
                    return true;
                }

                try
                {
                    using (Process proc = Process.GetProcessById((int)pid))
                    {
                        string name = proc.ProcessName;
                        for (int i = 0; i < processNames.Length; i++)
                        {
                            if (string.Equals(name, processNames[i], StringComparison.OrdinalIgnoreCase))
                            {
                                if (PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                                {
                                    closed++;
                                }
                                break;
                            }
                        }
                    }
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);
            return closed;
        }

        public static TimeSpan GetMouseKeyboardIdleTime()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref lii))
            {
                return TimeSpan.Zero;
            }

            uint now = unchecked((uint)Environment.TickCount);
            uint elapsedMs = unchecked(now - lii.dwTime);
            return TimeSpan.FromMilliseconds(elapsedMs);
        }

        public static uint[] GetXInputPacketNumbers()
        {
            uint[] packets = new uint[4];
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                try
                {
                    uint result = XInputGetState_1_4(i, ref state);
                    packets[i] = (result == 0) ? state.dwPacketNumber : 0;
                }
                catch (DllNotFoundException)
                {
                    return packets;
                }
            }
            return packets;
        }

        public static bool SuspendToSleep()
        {
            return SetSuspendState(false, false, false);
        }

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            [MarshalAs(UnmanagedType.Bool)]
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public byte[] data;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public int type;
            public int size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string monitorDevicePath;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);

        private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
        private const int DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value;
            public uint colorEncoding;
            public uint bitsPerColorChannel;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint enableAdvancedColor;
        }

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setPacket);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(
            uint numPathArrayElements,
            [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
            uint numModeInfoArrayElements,
            [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(
            string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        private const uint QDC_ALL_PATHS = 0x00000001;
        private const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
        private const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;
        private const uint SDC_TOPOLOGY_SUPPLIED = 0x00000010;
        private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
        private const uint SDC_APPLY = 0x00000080;
        private const uint SDC_ALLOW_CHANGES = 0x00000400;
        private const uint SDC_SAVE_TO_DATABASE = 0x00000200;
        private const uint SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000;
        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int DM_PELSWIDTH = 0x00080000;
        private const int DM_PELSHEIGHT = 0x00100000;
        private const int DM_DISPLAYFREQUENCY = 0x00400000;
        private const uint CDS_UPDATEREGISTRY = 0x00000001;

        private static bool TryFindAvailableTargetPath(string expectedMonitor, out DISPLAYCONFIG_PATH_INFO chosen)
        {
            chosen = new DISPLAYCONFIG_PATH_INFO();
            if (string.IsNullOrWhiteSpace(expectedMonitor))
            {
                return false;
            }

            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out pathCount, out modeCount) != 0 || pathCount == 0)
            {
                return false;
            }

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return false;
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < pathCount; i++)
                {
                    if (!paths[i].targetInfo.targetAvailable)
                    {
                        continue;
                    }
                    if (pass == 0 && paths[i].sourceInfo.id != 0)
                    {
                        continue;
                    }

                    DISPLAYCONFIG_TARGET_DEVICE_NAME target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    target.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    target.header.adapterId = paths[i].targetInfo.adapterId;
                    target.header.id = paths[i].targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref target) == 0 &&
                        !string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName) &&
                        target.monitorFriendlyDeviceName.IndexOf(expectedMonitor, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        chosen = paths[i];
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool IsMonitorAvailable(string expectedMonitor)
        {
            if (string.IsNullOrWhiteSpace(expectedMonitor))
            {
                return true;
            }

            DISPLAYCONFIG_PATH_INFO unused;
            return TryFindAvailableTargetPath(expectedMonitor, out unused);
        }

        public static void SwitchToSingleMonitor(string expectedMonitor, int width, int height, int refreshRate, bool? hdr)
        {
            if (string.IsNullOrWhiteSpace(expectedMonitor))
            {
                throw new ArgumentException("expectedMonitor must not be empty.");
            }

            DISPLAYCONFIG_PATH_INFO chosen;
            if (!TryFindAvailableTargetPath(expectedMonitor, out chosen))
            {
                throw new InvalidOperationException("No connected monitor found matching \"" + expectedMonitor + "\".");
            }

            chosen.flags = DISPLAYCONFIG_PATH_ACTIVE;
            chosen.sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            chosen.targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;

            DISPLAYCONFIG_PATH_INFO[] singlePath = new DISPLAYCONFIG_PATH_INFO[] { chosen };
            int res = SetDisplayConfig(1, singlePath, 0, null,
                SDC_APPLY | SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES);
            if (res != 0)
            {
                res = SetDisplayConfig(1, singlePath, 0, null,
                    SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);
            }

            if (res != 0)
            {
                throw new InvalidOperationException("SetDisplayConfig failed with error code " + res);
            }

            if (width > 0 && height > 0 && refreshRate > 0)
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    if (dm.dmPelsWidth != width || dm.dmPelsHeight != height || dm.dmDisplayFrequency != refreshRate)
                    {
                        dm.dmPelsWidth = width;
                        dm.dmPelsHeight = height;
                        dm.dmDisplayFrequency = refreshRate;
                        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
                        ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                    }
                }
            }

            if (hdr.HasValue)
            {
                SetActiveMonitorHdr(expectedMonitor, hdr.Value);
            }
        }

        private static bool TryGetActiveTargetForMonitor(string expectedMonitor, out LUID adapterId, out uint targetId)
        {
            adapterId = new LUID();
            targetId = 0;

            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0 || pathCount == 0)
            {
                return false;
            }

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return false;
            }

            for (int i = 0; i < pathCount; i++)
            {
                DISPLAYCONFIG_TARGET_DEVICE_NAME target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                target.header.adapterId = paths[i].targetInfo.adapterId;
                target.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref target) == 0 && !string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName))
                {
                    if (string.IsNullOrWhiteSpace(expectedMonitor) ||
                        target.monitorFriendlyDeviceName.IndexOf(expectedMonitor, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        adapterId = paths[i].targetInfo.adapterId;
                        targetId = paths[i].targetInfo.id;
                        return true;
                    }
                }
            }

            return false;
        }

        public static bool GetActiveMonitorHdrState(string expectedMonitor, out bool supported, out bool enabled)
        {
            supported = false;
            enabled = false;
            LUID adapterId;
            uint targetId;
            if (!TryGetActiveTargetForMonitor(expectedMonitor, out adapterId, out targetId))
            {
                return false;
            }

            DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
            info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
            info.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
            info.header.adapterId = adapterId;
            info.header.id = targetId;
            if (DisplayConfigGetDeviceInfo(ref info) != 0)
            {
                return false;
            }

            supported = (info.value & 1) != 0;
            enabled = (info.value & 2) != 0;
            return true;
        }

        public static bool SetActiveMonitorHdr(string expectedMonitor, bool enableHdr)
        {
            LUID adapterId;
            uint targetId;
            if (!TryGetActiveTargetForMonitor(expectedMonitor, out adapterId, out targetId))
            {
                return false;
            }

            DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
            info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
            info.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
            info.header.adapterId = adapterId;
            info.header.id = targetId;
            if (DisplayConfigGetDeviceInfo(ref info) != 0)
            {
                return false;
            }

            bool supported = (info.value & 1) != 0;
            bool enabled = (info.value & 2) != 0;
            if (!supported)
            {
                return false;
            }
            if (enabled == enableHdr)
            {
                return true;
            }

            DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
            setState.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
            setState.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
            setState.header.adapterId = adapterId;
            setState.header.id = targetId;
            setState.enableAdvancedColor = enableHdr ? 1u : 0u;
            return DisplayConfigSetDeviceInfo(ref setState) == 0;
        }

        public class ActiveDisplayInfo
        {
            public string FriendlyName { get; set; }
            public string DevicePath { get; set; }
        }

        public class DisplayDeviceInfo
        {
            public string FriendlyName { get; set; }
            public string DevicePath { get; set; }
            public bool IsActive { get; set; }
            public bool IsAvailable { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int RefreshRate { get; set; }
            public bool HdrSupported { get; set; }
            public bool HdrEnabled { get; set; }
        }

        public class AudioDeviceInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool IsDefault { get; set; }
        }

        public class DisplayModeInfo
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int RefreshRate { get; set; }
        }

        public static List<DisplayModeInfo> GetDisplayModes(string expectedMonitor)
        {
            List<DisplayModeInfo> result = new List<DisplayModeInfo>();
            if (string.IsNullOrWhiteSpace(expectedMonitor))
            {
                return result;
            }

            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0 || pathCount == 0)
            {
                return result;
            }

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return result;
            }

            DISPLAYCONFIG_PATH_INFO? matchedPath = null;
            for (int pass = 0; pass < 2 && !matchedPath.HasValue; pass++)
            {
                for (int i = 0; i < pathCount; i++)
                {
                    DISPLAYCONFIG_TARGET_DEVICE_NAME target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                    target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    target.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                    target.header.adapterId = paths[i].targetInfo.adapterId;
                    target.header.id = paths[i].targetInfo.id;
                    if (DisplayConfigGetDeviceInfo(ref target) != 0 || string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName))
                    {
                        continue;
                    }

                    bool isMatch = pass == 0
                        ? string.Equals(target.monitorFriendlyDeviceName, expectedMonitor, StringComparison.OrdinalIgnoreCase)
                        : target.monitorFriendlyDeviceName.IndexOf(expectedMonitor, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isMatch)
                    {
                        matchedPath = paths[i];
                        break;
                    }
                }
            }

            if (!matchedPath.HasValue)
            {
                return result;
            }

            DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            sourceName.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
            sourceName.header.adapterId = matchedPath.Value.sourceInfo.adapterId;
            sourceName.header.id = matchedPath.Value.sourceInfo.id;
            if (DisplayConfigGetDeviceInfo(ref sourceName) != 0 || string.IsNullOrWhiteSpace(sourceName.viewGdiDeviceName))
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>();
            int modeNum = 0;
            while (true)
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                if (!EnumDisplaySettings(sourceName.viewGdiDeviceName, modeNum, ref dm))
                {
                    break;
                }
                modeNum++;

                if (dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0 && dm.dmDisplayFrequency > 1)
                {
                    string key = dm.dmPelsWidth + "x" + dm.dmPelsHeight + "@" + dm.dmDisplayFrequency;
                    if (seen.Add(key))
                    {
                        result.Add(new DisplayModeInfo
                        {
                            Width = dm.dmPelsWidth,
                            Height = dm.dmPelsHeight,
                            RefreshRate = dm.dmDisplayFrequency
                        });
                    }
                }
            }

            result.Sort((a, b) =>
            {
                int cmp = b.Width.CompareTo(a.Width);
                if (cmp != 0) return cmp;
                cmp = b.Height.CompareTo(a.Height);
                if (cmp != 0) return cmp;
                return b.RefreshRate.CompareTo(a.RefreshRate);
            });

            return result;
        }

        public static List<DisplayDeviceInfo> GetAllDisplayInfos()
        {
            List<DisplayDeviceInfo> result = new List<DisplayDeviceInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out pathCount, out modeCount) != 0 || pathCount == 0)
            {
                return result;
            }

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ALL_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return result;
            }

            ActiveDisplayInfo[] activeDisplays = GetActiveDisplayInfos();
            HashSet<string> activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int a = 0; a < activeDisplays.Length; a++)
            {
                if (!string.IsNullOrWhiteSpace(activeDisplays[a].DevicePath))
                {
                    activePaths.Add(activeDisplays[a].DevicePath);
                }
            }

            for (int i = 0; i < pathCount; i++)
            {
                if (!paths[i].targetInfo.targetAvailable)
                {
                    continue;
                }

                DISPLAYCONFIG_TARGET_DEVICE_NAME target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                target.header.adapterId = paths[i].targetInfo.adapterId;
                target.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref target) != 0)
                {
                    continue;
                }

                string friendly = target.monitorFriendlyDeviceName ?? "";
                string devPath = target.monitorDevicePath ?? "";
                if (string.IsNullOrWhiteSpace(friendly) && string.IsNullOrWhiteSpace(devPath))
                {
                    continue;
                }

                string dedupKey = !string.IsNullOrWhiteSpace(devPath) ? devPath : friendly;
                if (seen.Contains(dedupKey))
                {
                    continue;
                }
                seen.Add(dedupKey);

                bool isActive = activePaths.Contains(devPath) || ((paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE) != 0);
                bool hdrSupported = false;
                bool hdrEnabled = false;
                if (!string.IsNullOrWhiteSpace(friendly) && isActive)
                {
                    GetActiveMonitorHdrState(friendly, out hdrSupported, out hdrEnabled);
                }

                int width = 0;
                int height = 0;
                int refresh = 0;
                if (isActive)
                {
                    DEVMODE dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                    if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
                    {
                        width = dm.dmPelsWidth;
                        height = dm.dmPelsHeight;
                        refresh = dm.dmDisplayFrequency;
                    }
                }

                result.Add(new DisplayDeviceInfo
                {
                    FriendlyName = friendly,
                    DevicePath = devPath,
                    IsActive = isActive,
                    IsAvailable = true,
                    Width = width,
                    Height = height,
                    RefreshRate = refresh,
                    HdrSupported = hdrSupported,
                    HdrEnabled = hdrEnabled
                });
            }

            return result;
        }

        public static List<AudioDeviceInfo> GetAudioPlaybackDevices()
        {
            List<AudioDeviceInfo> result = new List<AudioDeviceInfo>();
            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            IMMDevice defaultDev = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                string defaultId = "";
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out defaultDev) == 0 && defaultDev != null)
                {
                    defaultDev.GetId(out defaultId);
                }

                if (enumerator.EnumAudioEndpoints(0, 1, out collection) != 0 || collection == null)
                {
                    return result;
                }

                uint count;
                if (collection.GetCount(out count) != 0)
                {
                    return result;
                }

                for (uint i = 0; i < count; i++)
                {
                    IMMDevice dev = null;
                    try
                    {
                        if (collection.Item(i, out dev) != 0 || dev == null)
                        {
                            continue;
                        }
                        string name = GetDeviceFriendlyName(dev);
                        string id;
                        dev.GetId(out id);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            result.Add(new AudioDeviceInfo
                            {
                                Id = id ?? "",
                                Name = name,
                                IsDefault = !string.IsNullOrEmpty(defaultId) && string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)
                            });
                        }
                    }
                    finally
                    {
                        if (dev != null) Marshal.ReleaseComObject(dev);
                    }
                }

                return result;
            }
            catch
            {
                return result;
            }
            finally
            {
                if (defaultDev != null) Marshal.ReleaseComObject(defaultDev);
                if (collection != null) Marshal.ReleaseComObject(collection);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        public static ActiveDisplayInfo[] GetActiveDisplayInfos()
        {
            List<ActiveDisplayInfo> displays = new List<ActiveDisplayInfo>();
            uint pathCount, modeCount;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != 0 || pathCount == 0)
            {
                return displays.ToArray();
            }

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return displays.ToArray();
            }

            for (int i = 0; i < pathCount; i++)
            {
                DISPLAYCONFIG_TARGET_DEVICE_NAME target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = Marshal.SizeOf(typeof(DISPLAYCONFIG_TARGET_DEVICE_NAME));
                target.header.adapterId = paths[i].targetInfo.adapterId;
                target.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref target) == 0)
                {
                    string friendly = target.monitorFriendlyDeviceName ?? "";
                    string devPath = target.monitorDevicePath ?? "";
                    if (!string.IsNullOrWhiteSpace(friendly) || !string.IsNullOrWhiteSpace(devPath))
                    {
                        displays.Add(new ActiveDisplayInfo
                        {
                            FriendlyName = friendly,
                            DevicePath = devPath
                        });
                    }
                }
            }

            return displays.ToArray();
        }

        public static string[] GetActiveMonitorNames()
        {
            List<string> names = new List<string>();
            foreach (ActiveDisplayInfo info in GetActiveDisplayInfos())
            {
                if (!string.IsNullOrWhiteSpace(info.FriendlyName) && !names.Contains(info.FriendlyName))
                {
                    names.Add(info.FriendlyName);
                }
            }
            return names.ToArray();
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        }

        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            int GetCount(out uint pcDevices);
            int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class PolicyConfigClient
        {
        }

        [Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
            int GetDeviceFormat(string pszDeviceName, int bDefault, IntPtr ppFormat);
            int ResetDeviceFormat(string pszDeviceName);
            int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
            int GetProcessingPeriod(string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
            int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
            int GetShareMode(string pszDeviceName, IntPtr pMode);
            int SetShareMode(string pszDeviceName, IntPtr mode);
            int GetPropertyValue(string pszDeviceName, int bStore, IntPtr key, IntPtr pv);
            int SetPropertyValue(string pszDeviceName, int bStore, IntPtr key, IntPtr pv);
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int role);
            int SetEndpointVisibility(string pszDeviceName, int bVisible);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
            int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        }

        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            int GetCount(out uint cProps);
            int GetAt(uint iProp, out PROPERTYKEY pkey);
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPVARIANT
        {
            public ushort vt;
            private ushort r1;
            private ushort r2;
            private ushort r3;
            public IntPtr p1;
            public IntPtr p2;
        }

        private static string GetDeviceFriendlyName(IMMDevice device)
        {
            IPropertyStore store = null;
            try
            {
                if (device == null || device.OpenPropertyStore(0, out store) != 0 || store == null)
                {
                    return "";
                }

                PROPERTYKEY key = new PROPERTYKEY
                {
                    fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
                    pid = 14
                };
                PROPVARIANT pv;
                if (store.GetValue(ref key, out pv) != 0 || pv.p1 == IntPtr.Zero)
                {
                    return "";
                }

                string name = Marshal.PtrToStringUni(pv.p1);
                Marshal.FreeCoTaskMem(pv.p1);
                return name ?? "";
            }
            finally
            {
                if (store != null) Marshal.ReleaseComObject(store);
            }
        }

        public static string GetDefaultAudioPlaybackDeviceName()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null)
                {
                    return "";
                }
                return GetDeviceFriendlyName(device);
            }
            catch
            {
                return "";
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        public static bool SetDefaultAudioPlaybackDeviceByName(string expectedNameSubstring, out string matchedName)
        {
            matchedName = "";
            if (string.IsNullOrWhiteSpace(expectedNameSubstring))
            {
                return false;
            }

            IMMDeviceEnumerator enumerator = null;
            IMMDeviceCollection collection = null;
            IPolicyConfig policyConfig = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(new MMDeviceEnumerator());
                if (enumerator.EnumAudioEndpoints(0, 1, out collection) != 0 || collection == null)
                {
                    return false;
                }

                uint count;
                if (collection.GetCount(out count) != 0)
                {
                    return false;
                }

                for (uint i = 0; i < count; i++)
                {
                    IMMDevice dev = null;
                    try
                    {
                        if (collection.Item(i, out dev) != 0 || dev == null)
                        {
                            continue;
                        }

                        string name = GetDeviceFriendlyName(dev);
                        if (string.IsNullOrEmpty(name) ||
                            name.IndexOf(expectedNameSubstring, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        string deviceId;
                        if (dev.GetId(out deviceId) != 0 || string.IsNullOrEmpty(deviceId))
                        {
                            continue;
                        }

                        policyConfig = (IPolicyConfig)(new PolicyConfigClient());
                        int hr0 = policyConfig.SetDefaultEndpoint(deviceId, 0);
                        int hr1 = policyConfig.SetDefaultEndpoint(deviceId, 1);
                        int hr2 = policyConfig.SetDefaultEndpoint(deviceId, 2);
                        if (hr0 == 0 && hr1 == 0 && hr2 == 0)
                        {
                            matchedName = name;
                            return true;
                        }
                    }
                    finally
                    {
                        if (dev != null) Marshal.ReleaseComObject(dev);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (policyConfig != null) Marshal.ReleaseComObject(policyConfig);
                if (collection != null) Marshal.ReleaseComObject(collection);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        public static List<string> GetConnectedXboxReceiverControllerIds(string[] receiverPrefixes)
        {
            List<string> connected = new List<string>();
            if (receiverPrefixes == null || receiverPrefixes.Length == 0)
            {
                return connected;
            }

            uint flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;
            uint len;
            if (CM_Get_Device_ID_List_SizeW(out len, "USB", flags) != CR_SUCCESS || len == 0)
            {
                return connected;
            }

            char[] buf = new char[len];
            if (CM_Get_Device_ID_ListW("USB", buf, len, flags) != CR_SUCCESS)
            {
                return connected;
            }

            string[] usbDeviceIds = new string(buf).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < usbDeviceIds.Length; i++)
            {
                string deviceId = usbDeviceIds[i];
                if (!MatchesAnyPrefix(deviceId, receiverPrefixes))
                {
                    continue;
                }

                uint receiverDevInst;
                if (CM_Locate_DevNodeW(out receiverDevInst, deviceId, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                {
                    continue;
                }

                uint childDevInst;
                int cr = CM_Get_Child(out childDevInst, receiverDevInst, 0);
                while (cr == CR_SUCCESS)
                {
                    uint status;
                    uint problem;
                    if (CM_Get_DevNode_Status(out status, out problem, childDevInst, 0) == CR_SUCCESS &&
                        (status & DN_STARTED) != 0 &&
                        problem == 0)
                    {
                        StringBuilder sb = new StringBuilder(512);
                        if (CM_Get_Device_IDW(childDevInst, sb, sb.Capacity, 0) == CR_SUCCESS)
                        {
                            string childId = sb.ToString();
                            if (childId.IndexOf("&IG_", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                connected.Add(childId);
                            }
                        }
                    }

                    uint nextChild;
                    cr = CM_Get_Sibling(out nextChild, childDevInst, 0);
                    childDevInst = nextChild;
                }
            }

            return connected;
        }

        private static bool MatchesAnyPrefix(string deviceId, string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (!string.IsNullOrWhiteSpace(prefix) &&
                    deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        #region RTSS OSD Control

        private const uint RTSSHOOKSFLAG_OSD_VISIBLE = 1;
        private const uint WM_RTSS_UPDATESETTINGS = 0x8000 + 100; // WM_APP + 100

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibraryA(string lpLibFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint RtssGetFlagsDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint RtssSetFlagsDelegate(uint dwAND, uint dwXOR);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate void RtssLoadProfileDelegate(string lpProfile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate void RtssSaveProfileDelegate(string lpProfile);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate bool RtssGetProfilePropertyDelegate(string lpPropertyName, out int lpPropertyData, uint dwPropertySize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate bool RtssSetProfilePropertyDelegate(string lpPropertyName, ref int lpPropertyData, uint dwPropertySize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void RtssUpdateProfilesDelegate();

        private static readonly object _rtssLock = new object();
        private static bool _rtssInitAttempted;
        private static IntPtr _rtssHooksModule = IntPtr.Zero;
        private static RtssGetFlagsDelegate _rtssGetFlags;
        private static RtssSetFlagsDelegate _rtssSetFlags;
        private static RtssLoadProfileDelegate _rtssLoadProfile;
        private static RtssSaveProfileDelegate _rtssSaveProfile;
        private static RtssGetProfilePropertyDelegate _rtssGetProfileProperty;
        private static RtssSetProfilePropertyDelegate _rtssSetProfileProperty;
        private static RtssUpdateProfilesDelegate _rtssUpdateProfiles;

        private static string GetRtssInstallDir()
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Unwinder\RTSS"))
                {
                    if (key != null)
                    {
                        string installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrWhiteSpace(installPath))
                        {
                            string dir = File.Exists(installPath) ? Path.GetDirectoryName(installPath) : installPath;
                            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                            {
                                return dir;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            const string defaultDir = @"C:\Program Files (x86)\RivaTuner Statistics Server";
            return Directory.Exists(defaultDir) ? defaultDir : null;
        }

        private static bool EnsureRtssHooksLocked()
        {
            if (_rtssHooksModule != IntPtr.Zero &&
                _rtssGetFlags != null &&
                _rtssSetFlags != null &&
                _rtssLoadProfile != null &&
                _rtssSaveProfile != null &&
                _rtssGetProfileProperty != null &&
                _rtssSetProfileProperty != null &&
                _rtssUpdateProfiles != null)
            {
                return true;
            }

            if (_rtssInitAttempted)
            {
                return false;
            }
            _rtssInitAttempted = true;

            string dir = GetRtssInstallDir();
            if (string.IsNullOrWhiteSpace(dir))
            {
                return false;
            }

            string dllName = Environment.Is64BitProcess ? "RTSSHooks64.dll" : "RTSSHooks.dll";
            string dllPath = Path.Combine(dir, dllName);
            if (!File.Exists(dllPath))
            {
                return false;
            }

            IntPtr mod = LoadLibraryA(dllPath);
            if (mod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr pGetFlags = GetProcAddress(mod, "GetFlags");
            IntPtr pSetFlags = GetProcAddress(mod, "SetFlags");
            IntPtr pLoad = GetProcAddress(mod, "LoadProfile");
            IntPtr pSave = GetProcAddress(mod, "SaveProfile");
            IntPtr pGet = GetProcAddress(mod, "GetProfileProperty");
            IntPtr pSet = GetProcAddress(mod, "SetProfileProperty");
            IntPtr pUpdate = GetProcAddress(mod, "UpdateProfiles");
            if (pGetFlags == IntPtr.Zero || pSetFlags == IntPtr.Zero ||
                pLoad == IntPtr.Zero || pSave == IntPtr.Zero || pGet == IntPtr.Zero || pSet == IntPtr.Zero || pUpdate == IntPtr.Zero)
            {
                return false;
            }

            _rtssGetFlags = (RtssGetFlagsDelegate)Marshal.GetDelegateForFunctionPointer(pGetFlags, typeof(RtssGetFlagsDelegate));
            _rtssSetFlags = (RtssSetFlagsDelegate)Marshal.GetDelegateForFunctionPointer(pSetFlags, typeof(RtssSetFlagsDelegate));
            _rtssLoadProfile = (RtssLoadProfileDelegate)Marshal.GetDelegateForFunctionPointer(pLoad, typeof(RtssLoadProfileDelegate));
            _rtssSaveProfile = (RtssSaveProfileDelegate)Marshal.GetDelegateForFunctionPointer(pSave, typeof(RtssSaveProfileDelegate));
            _rtssGetProfileProperty = (RtssGetProfilePropertyDelegate)Marshal.GetDelegateForFunctionPointer(pGet, typeof(RtssGetProfilePropertyDelegate));
            _rtssSetProfileProperty = (RtssSetProfilePropertyDelegate)Marshal.GetDelegateForFunctionPointer(pSet, typeof(RtssSetProfilePropertyDelegate));
            _rtssUpdateProfiles = (RtssUpdateProfilesDelegate)Marshal.GetDelegateForFunctionPointer(pUpdate, typeof(RtssUpdateProfilesDelegate));
            _rtssHooksModule = mod;
            return true;
        }

        private static void NotifyRtssSettingsUpdated()
        {
            IntPtr hWnd = FindWindow(null, "RTSS");
            if (hWnd == IntPtr.Zero)
            {
                hWnd = FindWindow(null, "RivaTuner Statistics Server");
            }
            if (hWnd != IntPtr.Zero)
            {
                PostMessage(hWnd, WM_RTSS_UPDATESETTINGS, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public static bool? GetRtssOsdEnabled()
        {
            lock (_rtssLock)
            {
                try
                {
                    if (!EnsureRtssHooksLocked())
                    {
                        return null;
                    }

                    uint flags = _rtssGetFlags();
                    return (flags & RTSSHOOKSFLAG_OSD_VISIBLE) != 0;
                }
                catch
                {
                }
                return null;
            }
        }

        public static bool TrySetRtssOsdEnabled(bool enabled, out bool currentState)
        {
            currentState = enabled;
            lock (_rtssLock)
            {
                try
                {
                    if (!EnsureRtssHooksLocked())
                    {
                        return false;
                    }

                    // Toggle the runtime RTSSHOOKSFLAG_OSD_VISIBLE flag (same mechanism as Afterburner/RTSS OSD hotkeys).
                    uint newFlags = _rtssSetFlags(~RTSSHOOKSFLAG_OSD_VISIBLE, enabled ? RTSSHOOKSFLAG_OSD_VISIBLE : 0u);

                    if (enabled)
                    {
                        // Recover if Global profile EnableOSD was previously turned off.
                        _rtssLoadProfile("");
                        int profileVal;
                        if (_rtssGetProfileProperty("EnableOSD", out profileVal, 4) && profileVal == 0)
                        {
                            int one = 1;
                            if (_rtssSetProfileProperty("EnableOSD", ref one, 4))
                            {
                                _rtssSaveProfile("");
                                _rtssUpdateProfiles();
                            }
                        }
                    }

                    NotifyRtssSettingsUpdated();

                    currentState = (newFlags & RTSSHOOKSFLAG_OSD_VISIBLE) != 0;
                    return currentState == enabled;
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion

        public static bool MoveCursorToLowerRightCorner(out int x, out int y)
        {
            int width = GetSystemMetrics(SM_CXSCREEN);
            int height = GetSystemMetrics(SM_CYSCREEN);
            if (width <= 0 || height <= 0)
            {
                x = 0;
                y = 0;
                return false;
            }

            x = width - 1;
            y = height - 1;
            return SetCursorPos(x, y);
        }

        private static INPUT CreateMouseInput(int dx, int dy, uint mouseData, uint dwFlags)
        {
            return new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = mouseData,
                        dwFlags = dwFlags,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };
        }

        private static INPUT CreateUnicodeKeyInput(ushort scanCode, uint flags)
        {
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = scanCode,
                        dwFlags = KEYEVENTF_UNICODE | flags,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };
        }

        private static bool IsExtendedVirtualKey(ushort vk)
        {
            switch (vk)
            {
                case 0x21: // VK_PRIOR (Page Up)
                case 0x22: // VK_NEXT (Page Down)
                case 0x23: // VK_END
                case 0x24: // VK_HOME
                case 0x25: // VK_LEFT
                case 0x26: // VK_UP
                case 0x27: // VK_RIGHT
                case 0x28: // VK_DOWN
                case 0x2C: // VK_SNAPSHOT
                case 0x2D: // VK_INSERT
                case 0x2E: // VK_DELETE
                case 0x5B: // VK_LWIN
                case 0x5C: // VK_RWIN
                case 0x5D: // VK_APPS
                case 0x6F: // VK_DIVIDE
                case 0x90: // VK_NUMLOCK
                case 0xA3: // VK_RCONTROL
                case 0xA5: // VK_RMENU
                    return true;
                default:
                    return false;
            }
        }

        public static bool MoveMouseRelative(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return true;
            INPUT[] inputs = new INPUT[]
            {
                CreateMouseInput(dx, dy, 0, MOUSEEVENTF_MOVE)
            };
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Length;
        }

        public static bool SendMouseButton(string button, string action)
        {
            uint downFlag;
            uint upFlag;
            if (string.Equals(button, "right", StringComparison.OrdinalIgnoreCase))
            {
                downFlag = MOUSEEVENTF_RIGHTDOWN;
                upFlag = MOUSEEVENTF_RIGHTUP;
            }
            else if (string.Equals(button, "middle", StringComparison.OrdinalIgnoreCase))
            {
                downFlag = MOUSEEVENTF_MIDDLEDOWN;
                upFlag = MOUSEEVENTF_MIDDLEUP;
            }
            else
            {
                downFlag = MOUSEEVENTF_LEFTDOWN;
                upFlag = MOUSEEVENTF_LEFTUP;
            }

            INPUT[] inputs;
            if (string.Equals(action, "down", StringComparison.OrdinalIgnoreCase))
            {
                inputs = new INPUT[] { CreateMouseInput(0, 0, 0, downFlag) };
            }
            else if (string.Equals(action, "up", StringComparison.OrdinalIgnoreCase))
            {
                inputs = new INPUT[] { CreateMouseInput(0, 0, 0, upFlag) };
            }
            else
            {
                inputs = new INPUT[]
                {
                    CreateMouseInput(0, 0, 0, downFlag),
                    CreateMouseInput(0, 0, 0, upFlag)
                };
            }

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Length;
        }

        public static bool SendMouseScroll(int deltaX, int deltaY)
        {
            List<INPUT> inputs = new List<INPUT>(2);
            if (deltaY != 0)
            {
                inputs.Add(CreateMouseInput(0, 0, unchecked((uint)deltaY), MOUSEEVENTF_WHEEL));
            }
            if (deltaX != 0)
            {
                inputs.Add(CreateMouseInput(0, 0, unchecked((uint)deltaX), MOUSEEVENTF_HWHEEL));
            }
            if (inputs.Count == 0) return true;
            uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Count;
        }

        public static bool SendKey(ushort vk, string action)
        {
            uint ext = IsExtendedVirtualKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0;
            INPUT[] inputs;
            if (string.Equals(action, "down", StringComparison.OrdinalIgnoreCase))
            {
                inputs = new INPUT[] { CreateKeyInput(vk, ext) };
            }
            else if (string.Equals(action, "up", StringComparison.OrdinalIgnoreCase))
            {
                inputs = new INPUT[] { CreateKeyInput(vk, ext | KEYEVENTF_KEYUP) };
            }
            else
            {
                inputs = new INPUT[]
                {
                    CreateKeyInput(vk, ext),
                    CreateKeyInput(vk, ext | KEYEVENTF_KEYUP)
                };
            }

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Length;
        }

        public static bool SendKeyCombo(ushort[] vks)
        {
            if (vks == null || vks.Length == 0) return false;
            INPUT[] inputs = new INPUT[vks.Length * 2];
            for (int i = 0; i < vks.Length; i++)
            {
                uint ext = IsExtendedVirtualKey(vks[i]) ? KEYEVENTF_EXTENDEDKEY : 0;
                inputs[i] = CreateKeyInput(vks[i], ext);
            }
            for (int i = 0; i < vks.Length; i++)
            {
                int reverseIdx = vks.Length - 1 - i;
                uint ext = IsExtendedVirtualKey(vks[reverseIdx]) ? KEYEVENTF_EXTENDEDKEY : 0;
                inputs[vks.Length + i] = CreateKeyInput(vks[reverseIdx], ext | KEYEVENTF_KEYUP);
            }

            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Length;
        }

        public static bool SendUnicodeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            List<INPUT> inputs = new List<INPUT>(text.Length * 2);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    inputs.Add(CreateKeyInput(VK_RETURN, 0));
                    inputs.Add(CreateKeyInput(VK_RETURN, KEYEVENTF_KEYUP));
                }
                else if (ch == '\n')
                {
                    inputs.Add(CreateKeyInput(VK_RETURN, 0));
                    inputs.Add(CreateKeyInput(VK_RETURN, KEYEVENTF_KEYUP));
                }
                else if (ch == '\b')
                {
                    inputs.Add(CreateKeyInput(VK_BACK, 0));
                    inputs.Add(CreateKeyInput(VK_BACK, KEYEVENTF_KEYUP));
                }
                else if (ch == '\t')
                {
                    inputs.Add(CreateKeyInput(VK_TAB, 0));
                    inputs.Add(CreateKeyInput(VK_TAB, KEYEVENTF_KEYUP));
                }
                else
                {
                    inputs.Add(CreateUnicodeKeyInput(ch, 0));
                    inputs.Add(CreateUnicodeKeyInput(ch, KEYEVENTF_KEYUP));
                }
            }

            if (inputs.Count == 0) return true;
            uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            return sent == inputs.Count;
        }
    }
}
