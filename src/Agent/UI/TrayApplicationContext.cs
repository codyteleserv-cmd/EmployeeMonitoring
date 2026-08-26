using EmployeeMonitoring.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Windows.Forms;

namespace EmployeeMonitoring.Agent.UI;

/// <summary>
/// System tray application context - runs the agent UI in the system tray.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly IPauseManager _pauseManager;
    private readonly IConsentManager _consentManager;
    private readonly IAgentIdentityProvider _identityProvider;
    
    private NotifyIcon? _notifyIcon;
    private MainForm? _mainForm;
    private ContextMenuStrip? _contextMenu;
    private ToolStripMenuItem? _pauseMenuItem;
    private ToolStripMenuItem? _resumeMenuItem;
    private ToolStripMenuItem? _showDashboardMenuItem;
    private ToolStripMenuItem? _consentMenuItem;
    private ToolStripMenuItem? _exitMenuItem;

    public TrayApplicationContext(IServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<TrayApplicationContext>>();
        _pauseManager = services.GetRequiredService<IPauseManager>();
        _consentManager = services.GetRequiredService<IConsentManager>();
        _identityProvider = services.GetRequiredService<IAgentIdentityProvider>();

        InitializeTrayIcon();
        InitializeContextMenu();
        SubscribeToEvents();
        
        _logger.LogInformation("Tray application context initialized");
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(Color.Green), // Green = running
            Text = "Employee Monitoring Agent - Running",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += (s, e) => ShowDashboard();
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
    }

    private void InitializeContextMenu()
    {
        _contextMenu = new ContextMenuStrip();

        // Status header
        var statusItem = new ToolStripMenuItem("Employee Monitoring Agent")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        _contextMenu.Items.Add(statusItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Pause/Resume
        _pauseMenuItem = new ToolStripMenuItem("Pause Monitoring", null, async (s, e) => await PauseMonitoringAsync())
        {
            ShortcutKeys = Keys.Control | Keys.P
        };
        _contextMenu.Items.Add(_pauseMenuItem);

        _resumeMenuItem = new ToolStripMenuItem("Resume Monitoring", null, async (s, e) => await ResumeMonitoringAsync())
        {
            Visible = false,
            ShortcutKeys = Keys.Control | Keys.R
        };
        _contextMenu.Items.Add(_resumeMenuItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Show Dashboard
        _showDashboardMenuItem = new ToolStripMenuItem("Show Dashboard", null, (s, e) => ShowDashboard())
        {
            ShortcutKeys = Keys.Control | Keys.D
        };
        _contextMenu.Items.Add(_showDashboardMenuItem);

        // Consent status
        _consentMenuItem = new ToolStripMenuItem("Consent Status", null, (s, e) => ShowConsentDialog())
        {
            Enabled = false
        };
        _contextMenu.Items.Add(_consentMenuItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Settings (placeholder)
        var settingsItem = new ToolStripMenuItem("Settings", null, (s, e) => ShowSettings())
        {
            Enabled = false // Would open settings dialog
        };
        _contextMenu.Items.Add(settingsItem);

        // About
        var aboutItem = new ToolStripMenuItem("About", null, (s, e) => ShowAbout());
        _contextMenu.Items.Add(aboutItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Exit
        _exitMenuItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication())
        {
            ShortcutKeys = Keys.Alt | Keys.F4
        };
        _contextMenu.Items.Add(_exitMenuItem);

        UpdateMenuState();
    }

    private void SubscribeToEvents()
    {
        _pauseManager.PauseStateChanged += (s, e) => UpdateMenuState();
        _consentManager.ConsentChanged += (s, e) => UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        if (_contextMenu == null) return;

        var pauseState = _pauseManager.GetPauseState();
        var consentStatus = _consentManager.GetConsentStatus();

        if (_pauseMenuItem != null)
        {
            _pauseMenuItem.Visible = !pauseState.IsPaused && _pauseManager.CanPause();
            _pauseMenuItem.Enabled = _pauseManager.CanPause();
            
            if (pauseState.IsPaused)
            {
                var remaining = _pauseManager.GetRemainingPauseTime();
                _pauseMenuItem.Text = $"Pause Monitoring (Remaining: {remaining.TotalMinutes:F0} min today)";
            }
            else
            {
                _pauseMenuItem.Text = "Pause Monitoring";
            }
        }

        if (_resumeMenuItem != null)
        {
            _resumeMenuItem.Visible = pauseState.IsPaused;
        }

        if (_consentMenuItem != null)
        {
            _consentMenuItem.Text = consentStatus.ConsentGiven 
                ? $"✓ Consent Granted v{consentStatus.ConsentVersion}" 
                : "✗ Consent Required";
            _consentMenuItem.ForeColor = consentStatus.ConsentGiven ? Color.Green : Color.Red;
        }

        // Update tray icon
        UpdateTrayIcon(pauseState.IsPaused, consentStatus.ConsentGiven);
    }

    private void UpdateTrayIcon(bool isPaused, bool consentGiven)
    {
        if (_notifyIcon == null) return;

        Color iconColor;
        string statusText;

        if (!consentGiven)
        {
            iconColor = Color.Gray;
            statusText = "Employee Monitoring Agent - Consent Required";
        }
        else if (isPaused)
        {
            iconColor = Color.Orange;
            statusText = "Employee Monitoring Agent - Paused";
        }
        else
        {
            iconColor = Color.Green;
            statusText = "Employee Monitoring Agent - Running";
        }

        _notifyIcon.Icon = CreateTrayIcon(iconColor);
        _notifyIcon.Text = statusText;
    }

    private Icon CreateTrayIcon(Color color)
    {
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 2, 2, 12, 12);
            
            // Add a small indicator
            using var whiteBrush = new SolidBrush(Color.White);
            g.FillEllipse(whiteBrush, 6, 6, 4, 4);
        }
        
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private async void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // Left click shows context menu
            _contextMenu?.Show(Cursor.Position);
        }
    }

    private async Task PauseMonitoringAsync()
    {
        try
        {
            // Show pause reason dialog
            using var dialog = new PauseReasonDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var result = await _pauseManager.RequestPauseAsync(dialog.Reason);
                if (!result.Success)
                {
                    MessageBox.Show($"Failed to pause: {result.Message}", "Pause Failed", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause monitoring");
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ResumeMonitoringAsync()
    {
        try
        {
            var result = await _pauseManager.RequestResumeAsync();
            if (!result.Success)
            {
                MessageBox.Show($"Failed to resume: {result.Message}", "Resume Failed", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume monitoring");
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowDashboard()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = _services.GetRequiredService<MainForm>();
            _mainForm.FormClosed += (s, e) => _mainForm = null;
        }

        if (_mainForm.WindowState == FormWindowState.Minimized)
        {
            _mainForm.WindowState = FormWindowState.Normal;
        }

        _mainForm.Show();
        _mainForm.BringToFront();
    }

    private void ShowConsentDialog()
    {
        var consentStatus = _consentManager.GetConsentStatus();
        var message = consentStatus.ConsentGiven
            ? $"Consent granted v{consentStatus.ConsentVersion}\nModules: {string.Join(", ", consentStatus.GrantedModules)}"
            : "Consent not granted. Monitoring is disabled.";
        
        MessageBox.Show(message, "Consent Status", MessageBoxButtons.OK, 
            consentStatus.ConsentGiven ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ShowSettings()
    {
        MessageBox.Show("Settings dialog not implemented in this build.", "Settings", 
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        MessageBox.Show(
            $"Employee Monitoring Agent v{version}\n\n" +
            "Transparent, Consensual, Auditable Employee Monitoring\n\n" +
            "© 2024 EmployeeMonitoring Team",
            "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExitApplication()
    {
        var result = MessageBox.Show(
            "Are you sure you want to exit the monitoring agent?\n\n" +
            "This will stop all monitoring and notify administrators.",
            "Exit Agent", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _logger.LogInformation("User requested exit");
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();
            _mainForm?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Simple dialog for pause reason.
/// </summary>
internal class PauseReasonDialog : Form
{
    public string Reason { get; private set; } = string.Empty;

    public PauseReasonDialog()
    {
        Text = "Pause Monitoring";
        Size = new Size(400, 200);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var label = new Label
        {
            Text = "Enter reason for pausing monitoring:",
            Location = new Point(20, 20),
            AutoSize = true
        };

        var textBox = new TextBox
        {
            Location = new Point(20, 50),
            Size = new Size(340, 60),
            Multiline = true,
            PlaceholderText = "e.g., Lunch break, Personal time, Meeting..."
        };

        var okButton = new Button
        {
            Text = "Pause",
            Location = new Point(200, 120),
            Size = new Size(80, 30),
            DialogResult = DialogResult.OK
        };
        okButton.Click += (s, e) => Reason = textBox.Text;

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(290, 120),
            Size = new Size(80, 30),
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}