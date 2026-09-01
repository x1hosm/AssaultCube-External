using AssaultCubeHack.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Permissions;
using System.Threading;
using System.Windows.Forms;
using Utilities;

namespace AssaultCubeHack
{
    public partial class AssaultHack : Form
    {
        private const string processName = "ac_client";

        private Process process;
        private Thread healthHackThread;
        private Thread overlayThread;
        private Thread windowPosThread;
        private volatile bool isRunning = false;

        private Player self;
        private readonly List<Player> players = new List<Player>();
        private int numPlayers;
        private Matrix viewMatrix;
        private int gameWidth;
        private int gameHeight;

        private bool freezeZEnabled = false;
        private float frozenZ = 0f;
        private Thread freezeZThread;

        private BufferedGraphics bufferedGraphics;
        private readonly Font font = new Font(FontFamily.GenericMonospace, 10, FontStyle.Bold);
        private readonly Font fontSmall = new Font(FontFamily.GenericMonospace, 8, FontStyle.Regular);
        private readonly Color colorTransparencyKey = Color.Black;

        private GlobalKeyboardHook gkh = new GlobalKeyboardHook();
        private bool espEnabled = true;

        public AssaultHack()
        {
            InitializeComponent();

            try
            {
                var sp = new SecurityPermission(SecurityPermissionFlag.UnmanagedCode);
                sp.Demand();
            }
            catch (Exception ex)
            {
                Console.WriteLine("SecurityPermission failed: " + ex.Message);
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                const int WS_EX_TRANSPARENT = 0x20;
                cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        private void FreezeZ()
        {
            while (isRunning)
            {
                try
                {
                    if (freezeZEnabled && self != null)
                    {
                        // keep writing the same Z — gravity can't pull you down
                        Memory.Write<float>(self.Address + Offsets.PositionZ, frozenZ);
                    }
                }
                catch { }

                Thread.Sleep(10);
            }
        }

        private void HealthHack()
        {
            while (isRunning)
            {
                try
                {
                    if (self != null)
                    {
                        Memory.Write<int>(self.Address + Offsets.Health, 999);
                        Memory.Write<int>(self.Address + Offsets.Armour, 50);
                    }
                }
                catch { }

                Thread.Sleep(10); 
            }
        }

        private void AssaultHack_Load(object sender, EventArgs e)
        {
            Visible = false;
            AttachToGameProcess();
        }

        private void InitializeOverlayWindowAttributes()
        {
            Visible = true;
            picBoxOverlay.Visible = true;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.None;
            picBoxOverlay.Dock = DockStyle.Fill;
            picBoxOverlay.BackColor = colorTransparencyKey;
            TransparencyKey = colorTransparencyKey;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);

            var context = new BufferedGraphicsContext();
            bufferedGraphics = context.Allocate(picBoxOverlay.CreateGraphics(), ClientRectangle);
            bufferedGraphics.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bufferedGraphics.Graphics.CompositingQuality = CompositingQuality.HighQuality;
        }

        private void AttachToGameProcess()
        {
            Visible = false;
            int count = 0;
            bool success = false;

            do
            {
                if (Memory.GetProcessesByName(processName, out process))
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("Process found: " + process.Id + " : " + process.ProcessName);
                    try
                    {
                        IntPtr handle = Memory.OpenProcess(process.Id);
                        if (handle != IntPtr.Zero)
                        {
                            success = true;
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("Attached Handle: " + handle);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Could not attach.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Attach failed: " + ex.Message);
                        Thread.Sleep(1000);
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    if (count++ == 0) Console.Write("Waiting for " + processName);
                    else if (count < 10) Console.Write(".");
                    else count = 0;
                    Thread.Sleep(1000);
                }
            }
            while (!success);

            InitializeOverlayWindowAttributes();
            StartThreads();
        }

        private void StartThreads()
        {
            isRunning = true;

            overlayThread = new Thread(UpdateHack) { IsBackground = true };
            overlayThread.Start();

            windowPosThread = new Thread(UpdateWindow) { IsBackground = true };
            windowPosThread.Start(Handle);

            healthHackThread = new Thread(HealthHack) { IsBackground = true };
            healthHackThread.Start();

            gkh.HookedKeys.Add(Keys.X);

            freezeZThread = new Thread(FreezeZ) { IsBackground = true };
            freezeZThread.Start();

            gkh.HookedKeys.Add(Keys.Insert);
            gkh.KeyDown += KeyDownEvent;
        }

        private void UpdateWindow(object handle)
        {
            while (isRunning)
            {
                if (process == null || !Memory.IsProcessRunning(process))
                {
                    isRunning = false;
                    break;
                }
                SetOverlayPosition((IntPtr)handle);
                Thread.Sleep(200);
            }

            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke(new Action(() => { if (!isRunning) AttachToGameProcess(); })); }
                catch { }
            }
        }

        private void SetOverlayPosition(IntPtr overlayHandle)
        {
            if (process == null) return;
            IntPtr gameHandle = process.MainWindowHandle;
            if (gameHandle == IntPtr.Zero) return;

            NativeMethods.RECT windowRect, clientRect;
            if (!NativeMethods.GetWindowRect(gameHandle, out windowRect)) return;
            if (!NativeMethods.GetClientRect(gameHandle, out clientRect)) return;

            int outerWidth = windowRect.Right - windowRect.Left;
            int outerHeight = windowRect.Bottom - windowRect.Top;
            int width = outerWidth;
            int height = outerHeight;

            int style = NativeMethods.GetWindowLong(gameHandle, NativeMethods.GWL_STYLE);
            if ((style & NativeMethods.WS_BORDER) != 0)
            {
                width = clientRect.Right - clientRect.Left;
                height = clientRect.Bottom - clientRect.Top;
                int borderWidth = (outerWidth - clientRect.Right) / 2;
                int borderHeight = outerHeight - clientRect.Bottom - borderWidth;
                windowRect.Left += borderWidth;
                windowRect.Top += borderHeight;
            }

            NativeMethods.MoveWindow(overlayHandle, windowRect.Left, windowRect.Top, width, height, true);
            NativeMethods.SetWindowPos(gameHandle, overlayHandle, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);

            gameWidth = width;
            gameHeight = height;
        }

        private void KeyDownEvent(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Insert)
            {
                espEnabled = !espEnabled;
                Console.WriteLine("ESP: " + (espEnabled ? "ON" : "OFF"));
            }
            if (e.KeyCode == Keys.X)
            {
                freezeZEnabled = !freezeZEnabled;

                if (freezeZEnabled && self != null)
                {
                    frozenZ = Memory.Read<float>(self.Address + Offsets.PositionZ);
                    Console.WriteLine("Z Frozen at: " + frozenZ);
                }
                else
                {
                    Console.WriteLine("Z Freeze: OFF");
                }
            }
            e.Handled = true;
        }

        private void UpdateHack()
        {
            while (isRunning)
            {
                try
                {
                    ReadGameMemory();
                    Draw(bufferedGraphics.Graphics);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Update error: " + ex.Message);
                }
                Thread.Sleep(5);
            }

            Memory.CloseProcess();
            bufferedGraphics?.Dispose();
            bufferedGraphics = null;
        }

        private void ReadGameMemory()
        {
            if (!isRunning || process == null) return;

            long moduleBase = process.MainModule.BaseAddress.ToInt64();
            long localPlayerAddr = moduleBase + Offsets.LocalPlayer;
            int localPlayerPtr = Memory.Read<int>(localPlayerAddr);

            if (localPlayerPtr == 0)
            {
                self = null;
                players.Clear();
                numPlayers = 0;
                return;
            }

            self = new Player(localPlayerPtr);

            int entityArrayPtr = Memory.Read<int>(moduleBase + Offsets.EntityList);
            numPlayers = Memory.Read<int>(moduleBase + Offsets.EntityCount);

            players.Clear();
            if (entityArrayPtr == 0 || numPlayers <= 0) return;
            if (numPlayers > 64) numPlayers = 64;

            for (int i = 0; i < numPlayers; i++)
            {
                int playerPtr = Memory.Read<int>((long)entityArrayPtr + (i * 4));
                if (playerPtr == 0) continue;
                if (playerPtr == localPlayerPtr) continue;  
                players.Add(new Player(playerPtr));
            }

            viewMatrix = Memory.ReadMatrix(moduleBase + Offsets.ViewMatrix);
        }

        private void Draw(Graphics g)
        {
            if (bufferedGraphics == null) return;

            ClearScreen(g);

            if (!espEnabled || self == null || viewMatrix == null)
            {
                bufferedGraphics.Render();
                return;
            }

            using (var brush = new SolidBrush(Color.White))
            {
                g.DrawString(
                    string.Format("Players: {0}  |  HP: {1}  |  ESP: ON  [Insert = toggle]",
                        players.Count, self.Health),
                    font, brush, 8, 8);
            }

            foreach (Player p in players)
            {
                try
                {
                    if (p.Health <= 0) continue;  

                    Vector2 headScreen, footScreen;

                    bool headVisible = viewMatrix.WorldToScreen(
                        p.PositionHead, gameWidth, gameHeight, out headScreen);
                    bool footVisible = viewMatrix.WorldToScreen(
                        p.PositionFoot, gameWidth, gameHeight, out footScreen);

                    if (!headVisible || !footVisible) continue;

                    if (headScreen.x < -500 || headScreen.x > gameWidth + 500 ||
                        headScreen.y < -500 || headScreen.y > gameHeight + 500) continue;

                    float boxHeight = Math.Abs(footScreen.y - headScreen.y);
                    if (boxHeight < 2f) continue;  

                    float boxWidth = boxHeight * 0.4f;
                    float left = headScreen.x - boxWidth / 2f;
                    float top = headScreen.y;

                    bool isEnemy = (p.Team != self.Team);
                    Color boxColor = isEnemy ? Settings.Default.EnemyColor : Settings.Default.TeamColor;

                    using (var pen = new Pen(boxColor, 2f))
                        g.DrawRectangle(pen, left, top, boxWidth, boxHeight);

                    float barW = 4f;
                    float barLeft = left - barW - 3f;
                    float hpRatio = Math.Min(1f, Math.Max(0f, p.Health / 100f));
                    float greenH = boxHeight * hpRatio;

                    using (var bgBrush = new SolidBrush(Color.FromArgb(160, 60, 0, 0)))
                        g.FillRectangle(bgBrush, barLeft, top, barW, boxHeight);

                    Color hpColor = p.Health > 60
                        ? Color.FromArgb(220, 0, 210, 0)
                        : p.Health > 30
                            ? Color.FromArgb(220, 210, 210, 0)
                            : Color.FromArgb(220, 210, 0, 0);

                    using (var hpBrush = new SolidBrush(hpColor))
                        g.FillRectangle(hpBrush, barLeft, top + boxHeight - greenH, barW, greenH);

                    using (var textBrush = new SolidBrush(Color.White))
                        g.DrawString(p.Health + " HP", fontSmall, textBrush, left, top - 14f);

                    if (self != null)
                    {
                        float dist = self.PositionFoot.Distance(p.PositionFoot);
                        using (var textBrush = new SolidBrush(Color.LightGray))
                            g.DrawString(
                                ((int)dist) + "u",
                                fontSmall, textBrush,
                                left + boxWidth / 2f - 10f,
                                top + boxHeight + 2f);
                    }
                }
                catch {}
            }

            bufferedGraphics.Render();
        }

        private void ClearScreen(Graphics g)
        {
            using (var brush = new SolidBrush(colorTransparencyKey))
                g.FillRectangle(brush, ClientRectangle);
        }

        private void AssaultHack_FormClosing(object sender, FormClosingEventArgs e)
        {
            isRunning = false;

            try { if (windowPosThread?.IsAlive == true) windowPosThread.Join(1000); } catch { }
            try { if (healthHackThread?.IsAlive == true) healthHackThread.Join(1000); } catch { }
            try { if (overlayThread?.IsAlive == true) overlayThread.Join(1000); } catch { }
            try { if (freezeZThread?.IsAlive == true) freezeZThread.Join(1000); } catch { }
            try { Memory.CloseProcess(); } catch { }
            try { gkh.Unhook(); } catch { }

            Environment.Exit(0);
        }
    }
}