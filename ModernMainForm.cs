using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexDualLauncher;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(14, 16, 18);
    public static readonly Color Surface = Color.FromArgb(24, 27, 30);
    public static readonly Color SurfaceHover = Color.FromArgb(30, 34, 37);
    public static readonly Color Border = Color.FromArgb(49, 54, 58);
    public static readonly Color Text = Color.FromArgb(242, 243, 244);
    public static readonly Color Muted = Color.FromArgb(154, 160, 166);
    public static readonly Color Subtle = Color.FromArgb(109, 116, 122);
    public static readonly Color Accent = Color.FromArgb(98, 218, 177);
    public static readonly Color AccentHover = Color.FromArgb(119, 232, 194);
    public static readonly Color Success = Color.FromArgb(102, 221, 176);
    public static readonly Color Danger = Color.FromArgb(247, 126, 126);
}

internal sealed class ModernMainForm : Form
{
    private readonly ModernAppCard stable;
    private readonly ModernAppCard beta;
    private readonly Label footer = new();

    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public ModernMainForm()
    {
        var settings = LauncherSettings.Load();

        Text = "Dual Codex";
        ClientSize = new Size(860, 570);
        MinimumSize = new Size(876, 609);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        var title = new Label
        {
            Text = "Dual Codex",
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 26, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(42, 30)
        };
        var subtitle = new Label
        {
            Text = "Two versions. Two profiles. Zero account switching.",
            ForeColor = UiTheme.Muted,
            Font = new Font("Segoe UI", 10.5f),
            AutoSize = true,
            Location = new Point(46, 81)
        };
        var badge = new Label
        {
            Text = "●  LOCAL PROFILES",
            ForeColor = UiTheme.Accent,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(650, 49)
        };
        var info = new ModernButton
        {
            Text = "i",
            AccessibleName = "About Dual Codex",
            Location = new Point(786, 38),
            Size = new Size(34, 34),
            Kind = ModernButtonKind.Secondary,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        info.Click += (_, _) => new AboutForm().ShowDialog(this);

        stable = new ModernAppCard(
            "Codex", "Stable release", "OpenAI.Codex", "ChatGPT.exe",
            settings.StableHome ?? Path.Combine(UserProfile, ".codex"),
            "9PLM9XGG6VKS", 9223, new Point(42, 127), LaunchApp, SaveProfilePaths);

        beta = new ModernAppCard(
            "Codex Beta", "Preview release", "OpenAI.CodexBeta", "ChatGPT (Beta).exe",
            settings.BetaHome ?? Path.Combine(UserProfile, ".codex-beta"),
            "9N8CJ4W95TBZ", 9224, new Point(440, 127), LaunchApp, SaveProfilePaths);

        var refresh = new ModernButton
        {
            Text = "↻   Check installations",
            Location = new Point(42, 480),
            Size = new Size(190, 42),
            Kind = ModernButtonKind.Secondary
        };
        refresh.Click += async (_, _) => await RefreshPackages();

        footer.Location = new Point(252, 480);
        footer.Size = new Size(566, 42);
        footer.TextAlign = ContentAlignment.MiddleRight;
        footer.ForeColor = UiTheme.Muted;

        Controls.AddRange([title, subtitle, badge, info, stable, beta, refresh, footer]);
        Shown += async (_, _) => await RefreshPackages();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
    }

    private void ApplyDarkTitleBar()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const int immersiveDarkMode = 20;
        const int borderColor = 34;
        const int captionColor = 35;
        const int textColor = 36;

        var enabled = 1;
        var dark = ToColorRef(UiTheme.Background);
        var light = ToColorRef(UiTheme.Text);

        DwmSetWindowAttribute(Handle, immersiveDarkMode, ref enabled, sizeof(int));
        DwmSetWindowAttribute(Handle, captionColor, ref dark, sizeof(int));
        DwmSetWindowAttribute(Handle, borderColor, ref dark, sizeof(int));
        DwmSetWindowAttribute(Handle, textColor, ref light, sizeof(int));
    }

    private static int ToColorRef(Color color) =>
        color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int valueSize);

    private void SaveProfilePaths()
    {
        LauncherSettings.Save(new LauncherSettings
        {
            StableHome = stable.CodexHome,
            BetaHome = beta.CodexHome
        });
        footer.Text = "Profile folders saved.";
    }

    private async Task RefreshPackages()
    {
        footer.Text = "Checking installed apps…";
        stable.SetLoading();
        beta.SetLoading();
        await Task.WhenAll(stable.DetectAsync(), beta.DetectAsync());
        footer.Text = stable.Installed && beta.Installed
            ? "Both versions are ready to launch."
            : "Install any missing version from the Microsoft Store.";
    }

    private async void LaunchApp(ModernAppCard card)
    {
        try
        {
            card.SetBusy(true, "Launching…");
            footer.Text = $"Preparing {card.Title}…";

            var installLocation = await PackageLocator.GetInstallLocationAsync(card.PackageName);
            if (string.IsNullOrWhiteSpace(installLocation))
                throw new InvalidOperationException(
                    "This version is not installed for the current Windows user.");

            var exe = Path.Combine(installLocation, "app", card.ExecutableName);
            if (!File.Exists(exe))
                throw new FileNotFoundException("The installed app executable could not be found.", exe);

            CloseTargetProcesses(exe);
            Directory.CreateDirectory(card.CodexHome);

            var start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                Arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={card.DebugPort}"
            };
            start.Environment["CODEX_HOME"] = card.CodexHome;
            Process.Start(start);

            footer.Text = $"{card.Title} started. Applying the RTL helper…";
            var injected = await RtlInjector.InjectAsync(card.DebugPort);
            footer.Text = injected
                ? $"{card.Title} launched with its independent profile."
                : $"{card.Title} launched, but the RTL helper could not connect.";

            if (!injected)
                MessageBox.Show(this,
                    "The app started, but the RTL helper could not connect. Open a conversation, then launch this version again.",
                    "RTL helper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            footer.Text = "Launch failed.";
            MessageBox.Show(this, ex.Message, "Launch error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            card.SetBusy(false);
        }
    }

    private static void CloseTargetProcesses(string targetExecutable)
    {
        var processName = Path.GetFileNameWithoutExtension(targetExecutable);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var runningPath = process.MainModule?.FileName;
                if (!string.Equals(Path.GetFullPath(runningPath ?? ""),
                    Path.GetFullPath(targetExecutable), StringComparison.OrdinalIgnoreCase))
                    continue;
                process.Kill(true);
                process.WaitForExit(5000);
            }
            catch { }
            finally { process.Dispose(); }
        }
    }
}

internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About Dual Codex";
        ClientSize = new Size(460, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = UiTheme.Background;
        ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 10);

        var icon = new PictureBox
        {
            Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(32, 30),
            Size = new Size(70, 70)
        };

        var title = new Label
        {
            Text = "Dual Codex",
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            Location = new Point(122, 35),
            AutoSize = true
        };
        var description = new Label
        {
            Text = "Created by",
            ForeColor = UiTheme.Muted,
            Location = new Point(125, 75),
            AutoSize = true
        };
        var creator = new Label
        {
            Text = "Pourya Rajaei",
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            Location = new Point(32, 126),
            AutoSize = true
        };
        var emailCaption = Caption("EMAIL", 166);
        var email = Link("Pourya.Rajaei@gmail.com", 188,
            "mailto:Pourya.Rajaei@gmail.com");
        var phoneCaption = Caption("TELEPHONE", 224);
        var phone = new Label
        {
            Text = "0989309483323",
            ForeColor = UiTheme.Text,
            Location = new Point(32, 246),
            AutoSize = true
        };
        var telegramCaption = Caption("TELEGRAM", 278);
        var telegram = Link("t.me/PouryaRajaei", 300,
            "https://t.me/PouryaRajaei");

        Controls.AddRange([
            icon, title, description, creator, emailCaption, email,
            phoneCaption, phone, telegramCaption, telegram
        ]);
    }

    private static Label Caption(string text, int y) => new()
    {
        Text = text,
        ForeColor = UiTheme.Subtle,
        Font = new Font("Segoe UI", 8, FontStyle.Bold),
        Location = new Point(32, y),
        AutoSize = true
    };

    private static LinkLabel Link(string text, int y, string target)
    {
        var link = new LinkLabel
        {
            Text = text,
            LinkColor = UiTheme.Accent,
            ActiveLinkColor = UiTheme.AccentHover,
            VisitedLinkColor = UiTheme.Accent,
            Location = new Point(32, y),
            AutoSize = true
        };
        link.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch { }
        };
        return link;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        const int immersiveDarkMode = 20;
        const int captionColor = 35;
        const int textColor = 36;
        var enabled = 1;
        var dark = UiTheme.Background.R |
            (UiTheme.Background.G << 8) | (UiTheme.Background.B << 16);
        var light = UiTheme.Text.R |
            (UiTheme.Text.G << 8) | (UiTheme.Text.B << 16);
        DwmSetWindowAttribute(Handle, immersiveDarkMode, ref enabled, sizeof(int));
        DwmSetWindowAttribute(Handle, captionColor, ref dark, sizeof(int));
        DwmSetWindowAttribute(Handle, textColor, ref light, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int valueSize);
}

internal sealed class ModernAppCard : Panel
{
    public string Title { get; }
    public string PackageName { get; }
    public string ExecutableName { get; }
    public string CodexHome { get; private set; }
    public int DebugPort { get; }
    public bool Installed { get; private set; }

    private readonly string productId;
    private readonly Label state = new();
    private readonly Label home = new();
    private readonly ModernButton action = new();
    private readonly Action<ModernAppCard> launch;
    private bool hovered;

    public ModernAppCard(string title, string description, string packageName,
        string executableName, string codexHome, string productId, int debugPort,
        Point location, Action<ModernAppCard> launch, Action saveSettings)
    {
        Title = title;
        PackageName = packageName;
        ExecutableName = executableName;
        CodexHome = codexHome;
        DebugPort = debugPort;
        this.productId = productId;
        this.launch = launch;

        Location = location;
        Size = new Size(378, 320);
        BackColor = UiTheme.Surface;
        Padding = new Padding(1);
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        var icon = new Label
        {
            Text = title == "Codex" ? "C" : "β",
            ForeColor = UiTheme.Background,
            BackColor = UiTheme.Accent,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(25, 24),
            Size = new Size(38, 38)
        };
        var name = new Label
        {
            Text = title,
            ForeColor = UiTheme.Text,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(76, 21)
        };
        var desc = new Label
        {
            Text = description,
            ForeColor = UiTheme.Muted,
            AutoSize = true,
            Location = new Point(78, 52)
        };

        state.Location = new Point(25, 91);
        state.Size = new Size(328, 26);
        state.Font = new Font("Segoe UI", 9, FontStyle.Bold);

        var profileLabel = new Label
        {
            Text = "PROFILE FOLDER",
            ForeColor = UiTheme.Subtle,
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            Location = new Point(25, 132),
            AutoSize = true
        };
        home.ForeColor = UiTheme.Muted;
        home.AutoEllipsis = true;
        home.Location = new Point(25, 154);
        home.Size = new Size(328, 24);

        var chooseHome = new ModernButton
        {
            Text = "Choose profile folder",
            Location = new Point(25, 188),
            Size = new Size(328, 38),
            Kind = ModernButtonKind.Secondary
        };
        chooseHome.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                Description = $"Select an independent profile folder for {Title}",
                SelectedPath = Directory.Exists(CodexHome) ? CodexHome : UserProfilePath(),
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };
            if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
            CodexHome = picker.SelectedPath;
            UpdateHomeLabel();
            saveSettings();
        };

        action.Location = new Point(25, 242);
        action.Size = new Size(328, 50);
        action.Click += (_, _) =>
        {
            if (Installed) launch(this);
            else OpenStore();
        };

        Controls.AddRange([icon, name, desc, state, profileLabel, home, chooseHome, action]);
        foreach (Control control in Controls)
        {
            control.MouseEnter += (_, _) => SetHovered(true);
            control.MouseLeave += (_, _) => SetHovered(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        }
        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) => SetHovered(false);
        UpdateHomeLabel();
        SetLoading();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedRect(ClientRectangle, 18);
        using var fill = new SolidBrush(hovered ? UiTheme.SurfaceHover : UiTheme.Surface);
        using var border = new Pen(hovered ? Color.FromArgb(67, 75, 79) : UiTheme.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = RoundedRect(ClientRectangle, 18);
        Region = new Region(path);
    }

    private void SetHovered(bool value)
    {
        if (hovered == value) return;
        hovered = value;
        Invalidate();
    }

    private static GraphicsPath RoundedRect(Rectangle rectangle, int radius)
    {
        var rect = new Rectangle(rectangle.X, rectangle.Y,
            Math.Max(1, rectangle.Width - 1), Math.Max(1, rectangle.Height - 1));
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string UserProfilePath() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private void UpdateHomeLabel() => home.Text = CodexHome;

    public void SetLoading()
    {
        action.Enabled = false;
        action.Text = "Checking…";
        state.Text = "●  CHECKING INSTALLATION";
        state.ForeColor = UiTheme.Muted;
    }

    public async Task DetectAsync()
    {
        var location = await PackageLocator.GetInstallLocationAsync(PackageName);
        Installed = !string.IsNullOrWhiteSpace(location);
        state.Text = Installed ? "●  INSTALLED" : "●  NOT INSTALLED";
        state.ForeColor = Installed ? UiTheme.Success : UiTheme.Danger;
        action.Enabled = true;
        action.Text = Installed ? $"Launch {Title}   →" : "Get from Microsoft Store   ↗";
    }

    public void SetBusy(bool busy, string? text = null)
    {
        action.Enabled = !busy;
        action.Text = busy && text is not null
            ? text
            : Installed ? $"Launch {Title}   →" : "Get from Microsoft Store   ↗";
    }

    private void OpenStore()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"ms-windows-store://pdp/?ProductId={productId}")
                { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo(
                $"https://apps.microsoft.com/detail/{productId.ToLowerInvariant()}")
                { UseShellExecute = true });
        }
    }
}

internal enum ModernButtonKind { Primary, Secondary }

internal sealed class ModernButton : Button
{
    private bool hovered;
    private ModernButtonKind kind;

    [DefaultValue(ModernButtonKind.Primary)]
    public ModernButtonKind Kind
    {
        get => kind;
        set { kind = value; Invalidate(); }
    }

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        ForeColor = UiTheme.Background;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        MouseEnter += (_, _) => { hovered = true; Invalidate(); };
        MouseLeave += (_, _) => { hovered = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? UiTheme.Background);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 9);
        var primary = kind == ModernButtonKind.Primary;
        var fillColor = primary
            ? (hovered ? UiTheme.AccentHover : UiTheme.Accent)
            : (hovered ? Color.FromArgb(48, 53, 57) : Color.FromArgb(35, 39, 42));
        using var fill = new SolidBrush(Enabled ? fillColor : Color.FromArgb(45, 48, 50));
        e.Graphics.FillPath(fill, path);
        if (!primary)
        {
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawPath(pen, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, rect,
            Enabled ? (primary ? UiTheme.Background : UiTheme.Text) : UiTheme.Subtle,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
