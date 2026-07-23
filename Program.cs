using System.Diagnostics;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CodexDualLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly AppCard stable;
    private readonly AppCard beta;
    private readonly Label footer = new();

    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public MainForm()
    {
        var settings = LauncherSettings.Load();

        Text = "Codex Dual Launcher";
        ClientSize = new Size(700, 455);
        MinimumSize = new Size(716, 494);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 246, 248);
        Font = new Font("Segoe UI", 10);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;

        var title = new Label
        {
            Text = "اجرای Codex با دو حساب مجزا",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(175, 25)
        };
        var subtitle = new Label
        {
            Text = "هر نسخه پوشهٔ ورود مستقل خودش را دارد و حساب دیگری را تغییر نمی‌دهد.",
            ForeColor = Color.FromArgb(85, 85, 90),
            AutoSize = true,
            Location = new Point(125, 72)
        };

        stable = new AppCard(
            "Codex",
            "نسخهٔ معمولی",
            "OpenAI.Codex",
            "ChatGPT.exe",
            settings.StableHome ?? Path.Combine(UserProfile, ".codex"),
            "9PLM9XGG6VKS",
            9223,
            new Point(355, 115),
            LaunchApp,
            SaveProfilePaths);

        beta = new AppCard(
            "Codex Beta",
            "نسخهٔ آزمایشی",
            "OpenAI.CodexBeta",
            "ChatGPT (Beta).exe",
            settings.BetaHome ?? Path.Combine(UserProfile, ".codex-beta"),
            "9N8CJ4W95TBZ",
            9224,
            new Point(25, 115),
            LaunchApp,
            SaveProfilePaths);

        var refresh = new Button
        {
            Text = "بررسی دوبارهٔ نصب",
            Location = new Point(250, 360),
            Size = new Size(200, 38)
        };
        refresh.Click += async (_, _) => await RefreshPackages();

        footer.Location = new Point(40, 410);
        footer.Size = new Size(620, 28);
        footer.TextAlign = ContentAlignment.MiddleCenter;
        footer.ForeColor = Color.DimGray;

        Controls.AddRange([title, subtitle, stable.Panel, beta.Panel, refresh, footer]);
        Shown += async (_, _) => await RefreshPackages();
    }

    private void SaveProfilePaths()
    {
        LauncherSettings.Save(new LauncherSettings
        {
            StableHome = stable.CodexHome,
            BetaHome = beta.CodexHome
        });
        footer.Text = "مسیر پروفایل‌ها ذخیره شد.";
    }

    private async Task RefreshPackages()
    {
        footer.Text = "در حال بررسی برنامه‌های نصب‌شده…";
        stable.SetLoading();
        beta.SetLoading();

        await Task.WhenAll(stable.DetectAsync(), beta.DetectAsync());

        footer.Text = stable.Installed && beta.Installed
            ? "هر دو نسخه آماده‌اند."
            : "برای نسخهٔ نصب‌نشده، دکمهٔ Microsoft Store را بزنید.";
    }

    private async void LaunchApp(AppCard card)
    {
        try
        {
            card.SetBusy(true, "در حال اجرا…");
            footer.Text = $"در حال آماده‌سازی {card.Title}…";

            var installLocation = await PackageLocator.GetInstallLocationAsync(card.PackageName);
            if (string.IsNullOrWhiteSpace(installLocation))
                throw new InvalidOperationException("این نسخه نصب نیست یا برای کاربر فعلی ثبت نشده است.");

            var exe = Path.Combine(installLocation, "app", card.ExecutableName);
            if (!File.Exists(exe))
                throw new FileNotFoundException("فایل اجرایی نسخهٔ نصب‌شده پیدا نشد.", exe);

            CloseTargetProcesses(exe);
            Directory.CreateDirectory(card.CodexHome);

            var start = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                Arguments =
                    $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={card.DebugPort}"
            };
            start.Environment["CODEX_HOME"] = card.CodexHome;
            Process.Start(start);

            footer.Text = $"{card.Title} اجرا شد؛ در حال فعال‌سازی فارسی‌ساز…";
            var injected = await RtlInjector.InjectAsync(card.DebugPort);
            footer.Text = injected
                ? $"{card.Title} با پروفایل مستقل و فارسی‌ساز اجرا شد."
                : $"{card.Title} اجرا شد، اما صفحهٔ قابل تزریق پیدا نشد.";

            if (!injected)
                MessageBox.Show(this,
                    "برنامه اجرا شد، اما فارسی‌ساز نتوانست به رابط متصل شود. " +
                    "یک گفت‌وگو را باز کنید و دوباره دکمهٔ اجرا را بزنید.",
                    "هشدار فارسی‌ساز", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            footer.Text = "اجرا ناموفق بود.";
            MessageBox.Show(this, ex.Message, "خطای اجرا",
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
                if (!string.Equals(
                    Path.GetFullPath(runningPath ?? ""),
                    Path.GetFullPath(targetExecutable),
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                process.Kill(true);
                process.WaitForExit(5000);
            }
            catch { /* A stale or protected helper is harmless; launch may still succeed. */ }
            finally { process.Dispose(); }
        }
    }
}

internal sealed class AppCard
{
    public Panel Panel { get; } = new();
    public string Title { get; }
    public string PackageName { get; }
    public string ExecutableName { get; }
    public string CodexHome { get; private set; }
    public int DebugPort { get; }
    public bool Installed { get; private set; }

    private readonly string productId;
    private readonly Label state = new();
    private readonly Label home = new();
    private readonly Button action = new();
    private readonly Action<AppCard> launch;

    public AppCard(
        string title,
        string description,
        string packageName,
        string executableName,
        string codexHome,
        string productId,
        int debugPort,
        Point location,
        Action<AppCard> launch,
        Action saveSettings)
    {
        Title = title;
        PackageName = packageName;
        ExecutableName = executableName;
        CodexHome = codexHome;
        DebugPort = debugPort;
        this.productId = productId;
        this.launch = launch;

        Panel.Location = location;
        Panel.Size = new Size(320, 225);
        Panel.BackColor = Color.White;
        Panel.BorderStyle = BorderStyle.FixedSingle;

        var name = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(18, 18)
        };
        var desc = new Label
        {
            Text = description,
            ForeColor = Color.DimGray,
            AutoSize = true,
            Location = new Point(20, 55)
        };
        state.Location = new Point(20, 88);
        state.Size = new Size(278, 26);

        home.ForeColor = Color.FromArgb(95, 95, 100);
        home.AutoEllipsis = true;
        home.Location = new Point(20, 116);
        home.Size = new Size(278, 24);

        var chooseHome = new Button
        {
            Text = "تغییر پوشهٔ پروفایل",
            Location = new Point(20, 140),
            Size = new Size(278, 28)
        };
        chooseHome.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                Description = $"پوشهٔ پروفایل مستقل {Title} را انتخاب کنید",
                SelectedPath = Directory.Exists(CodexHome) ? CodexHome : UserProfilePath(),
                ShowNewFolderButton = true,
                UseDescriptionForTitle = true
            };

            if (picker.ShowDialog(Panel.FindForm()) != DialogResult.OK)
                return;

            CodexHome = picker.SelectedPath;
            UpdateHomeLabel();
            saveSettings();
        };

        action.Location = new Point(20, 174);
        action.Size = new Size(278, 32);
        action.Click += (_, _) =>
        {
            if (Installed)
                launch(this);
            else
                OpenStore();
        };

        Panel.Controls.AddRange([name, desc, state, home, chooseHome, action]);
        UpdateHomeLabel();
        SetLoading();
    }

    private static string UserProfilePath() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private void UpdateHomeLabel() => home.Text = $"پروفایل: {CodexHome}";

    public void SetLoading()
    {
        action.Enabled = false;
        action.Text = "در حال بررسی…";
        state.Text = "وضعیت نصب نامشخص";
        state.ForeColor = Color.DimGray;
    }

    public async Task DetectAsync()
    {
        var location = await PackageLocator.GetInstallLocationAsync(PackageName);
        Installed = !string.IsNullOrWhiteSpace(location);

        state.Text = Installed ? "● نصب شده" : "● نصب نشده";
        state.ForeColor = Installed
            ? Color.FromArgb(25, 135, 84)
            : Color.FromArgb(190, 70, 60);
        action.Enabled = true;
        action.Text = Installed ? $"اجرای {Title}" : "دریافت از Microsoft Store";
    }

    public void SetBusy(bool busy, string? text = null)
    {
        action.Enabled = !busy;
        if (busy && text is not null)
            action.Text = text;
        else
            action.Text = Installed ? $"اجرای {Title}" : "دریافت از Microsoft Store";
    }

    private void OpenStore()
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                $"ms-windows-store://pdp/?ProductId={productId}")
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

internal static class RtlInjector
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public static async Task<bool> InjectAsync(int port)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var targets = await GetTargetsAsync(port);
            var candidates = targets.Where(IsCodexTarget).ToArray();

            if (candidates.Length > 0)
            {
                var css = ReadResource("rtl-style.css");
                var script = ReadResource("injected.js");
                var expression =
                    "(() => {" +
                    $"window.__CODEX_RTL_STYLE__ = {JsonSerializer.Serialize(css)};" +
                    $"const source = {JsonSerializer.Serialize(script)};" +
                    "(0, eval)(source);" +
                    "return Boolean(window.__CODEX_RTL_ACTIVE__);" +
                    "})()";

                var success = false;
                foreach (var target in candidates)
                {
                    if (string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl))
                        continue;
                    try
                    {
                        await EvaluateAsync(target.WebSocketDebuggerUrl, expression);
                        success = true;
                    }
                    catch { /* Another renderer target may still be usable. */ }
                }
                if (success) return true;
            }

            await Task.Delay(1000);
        }
        return false;
    }

    private static async Task<CdpTarget[]> GetTargetsAsync(int port)
    {
        foreach (var host in new[] { "127.0.0.1", "localhost" })
        {
            try
            {
                var json = await Http.GetStringAsync($"http://{host}:{port}/json");
                return JsonSerializer.Deserialize<CdpTarget[]>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            catch { }
        }
        return [];
    }

    private static bool IsCodexTarget(CdpTarget target)
    {
        var text = $"{target.Title} {target.Url}".ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl) &&
            (text.Contains("codex") || text.Contains("chatgpt") || text.Contains("app://"));
    }

    private static async Task EvaluateAsync(string webSocketUrl, string expression)
    {
        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await socket.ConnectAsync(new Uri(webSocketUrl), timeout.Token);

        var request = JsonSerializer.Serialize(new
        {
            id = 1,
            method = "Runtime.evaluate",
            @params = new
            {
                expression,
                awaitPromise = false,
                returnByValue = true
            }
        });
        var bytes = Encoding.UTF8.GetBytes(request);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);

        var buffer = new byte[32 * 1024];
        while (!timeout.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("DevTools connection closed.");

            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == 1)
            {
                if (document.RootElement.TryGetProperty("error", out var error))
                    throw new InvalidOperationException(error.ToString());
                return;
            }
        }
    }

    private static string ReadResource(string suffix)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource missing: {suffix}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class CdpTarget
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? WebSocketDebuggerUrl { get; set; }
    }
}

internal sealed class LauncherSettings
{
    public string? StableHome { get; set; }
    public string? BetaHome { get; set; }

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDualLauncher");

    private static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public static LauncherSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new LauncherSettings();

            return JsonSerializer.Deserialize<LauncherSettings>(
                File.ReadAllText(SettingsFile)) ?? new LauncherSettings();
        }
        catch
        {
            return new LauncherSettings();
        }
    }

    public static void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(
            settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class PackageLocator
{
    public static async Task<string?> GetInstallLocationAsync(string packageName)
    {
        var escaped = packageName.Replace("'", "''");
        var command =
            $"(Get-AppxPackage -Name '{escaped}' -ErrorAction SilentlyContinue | " +
            "Sort-Object Version -Descending | Select-Object -First 1 -ExpandProperty InstallLocation)";

        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = $"-NoProfile -NonInteractive -Command \"{command}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        using var process = Process.Start(info);
        if (process is null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
            ? output.Trim()
            : null;
    }
}
