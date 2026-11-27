// Minimal OpenGL 3D test window (Windows equivalent of glxgears) for verifying RTSS / Afterburner OSD rendering.
// Build: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:build\gltest.exe scripts\gltest.cs

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class GlTestForm : Form
{
    [StructLayout(LayoutKind.Sequential)]
    struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize, nVersion;
        public uint dwFlags;
        public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] static extern bool SetPixelFormat(IntPtr hdc, int iPixelFormat, ref PIXELFORMATDESCRIPTOR ppfd);
    [DllImport("gdi32.dll")] static extern bool SwapBuffers(IntPtr hdc);

    [DllImport("opengl32.dll")] static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport("opengl32.dll")] static extern bool wglDeleteContext(IntPtr hglrc);
    [DllImport("opengl32.dll")] static extern void glViewport(int x, int y, int width, int height);
    [DllImport("opengl32.dll")] static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] static extern void glEnable(uint cap);
    [DllImport("opengl32.dll")] static extern void glMatrixMode(uint mode);
    [DllImport("opengl32.dll")] static extern void glLoadIdentity();
    [DllImport("opengl32.dll")] static extern void glFrustum(double l, double r, double b, double t, double n, double f);
    [DllImport("opengl32.dll")] static extern void glTranslated(double x, double y, double z);
    [DllImport("opengl32.dll")] static extern void glRotatef(float angle, float x, float y, float z);
    [DllImport("opengl32.dll")] static extern void glBegin(uint mode);
    [DllImport("opengl32.dll")] static extern void glEnd();
    [DllImport("opengl32.dll")] static extern void glColor3f(float r, float g, float b);
    [DllImport("opengl32.dll")] static extern void glVertex3f(float x, float y, float z);

    const uint PFD_DRAW_TO_WINDOW = 0x04, PFD_SUPPORT_OPENGL = 0x20, PFD_DOUBLEBUFFER = 0x01;
    const uint GL_COLOR_BUFFER_BIT = 0x4000, GL_DEPTH_BUFFER_BIT = 0x0100;
    const uint GL_DEPTH_TEST = 0x0B71, GL_PROJECTION = 0x1701, GL_MODELVIEW = 0x1700, GL_QUADS = 0x0007;

    IntPtr _hdc, _hglrc;
    float _angle;
    Timer _timer;

    public GlTestForm()
    {
        Text = "OpenGL 3D Test (RTSS OSD Check)";
        ClientSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Shown += (s, e) => Activate();
        SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        _hdc = GetDC(Handle);
        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR)),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = 0,
            cColorBits = 32,
            cDepthBits = 24
        };
        int pf = ChoosePixelFormat(_hdc, ref pfd);
        SetPixelFormat(_hdc, pf, ref pfd);
        _hglrc = wglCreateContext(_hdc);
        wglMakeCurrent(_hdc, _hglrc);
        glEnable(GL_DEPTH_TEST);
        glClearColor(0.1f, 0.12f, 0.16f, 1.0f);
        SetupProjection();

        _timer = new Timer { Interval = 16 };
        _timer.Tick += (s, e) => RenderFrame();
        _timer.Start();
    }

    void SetupProjection()
    {
        if (_hglrc == IntPtr.Zero) return;
        wglMakeCurrent(_hdc, _hglrc);
        int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
        glViewport(0, 0, w, h);
        glMatrixMode(GL_PROJECTION);
        glLoadIdentity();
        double aspect = (double)w / h;
        glFrustum(-aspect * 0.5, aspect * 0.5, -0.5, 0.5, 1.0, 20.0);
        glMatrixMode(GL_MODELVIEW);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SetupProjection();
    }

    void RenderFrame()
    {
        if (_hglrc == IntPtr.Zero) return;
        wglMakeCurrent(_hdc, _hglrc);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        glLoadIdentity();
        glTranslated(0, 0, -4.0);
        glRotatef(_angle, 1.0f, 1.2f, 0.4f);

        glBegin(GL_QUADS);
        glColor3f(0.9f, 0.3f, 0.3f); glVertex3f(-1,-1, 1); glVertex3f( 1,-1, 1); glVertex3f( 1, 1, 1); glVertex3f(-1, 1, 1);
        glColor3f(0.3f, 0.9f, 0.3f); glVertex3f(-1,-1,-1); glVertex3f(-1, 1,-1); glVertex3f( 1, 1,-1); glVertex3f( 1,-1,-1);
        glColor3f(0.3f, 0.5f, 0.9f); glVertex3f(-1, 1,-1); glVertex3f(-1, 1, 1); glVertex3f( 1, 1, 1); glVertex3f( 1, 1,-1);
        glColor3f(0.9f, 0.8f, 0.2f); glVertex3f(-1,-1,-1); glVertex3f( 1,-1,-1); glVertex3f( 1,-1, 1); glVertex3f(-1,-1, 1);
        glColor3f(0.8f, 0.3f, 0.9f); glVertex3f( 1,-1,-1); glVertex3f( 1, 1,-1); glVertex3f( 1, 1, 1); glVertex3f( 1,-1, 1);
        glColor3f(0.2f, 0.8f, 0.9f); glVertex3f(-1,-1,-1); glVertex3f(-1,-1, 1); glVertex3f(-1, 1, 1); glVertex3f(-1, 1,-1);
        glEnd();

        SwapBuffers(_hdc);
        _angle = (_angle + 1.5f) % 360.0f;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_timer != null) _timer.Stop();
        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        if (_hglrc != IntPtr.Zero) wglDeleteContext(_hglrc);
        if (_hdc != IntPtr.Zero) ReleaseDC(Handle, _hdc);
        base.OnFormClosed(e);
    }

    [STAThread]
    static void Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.Run(new GlTestForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "gltest error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
