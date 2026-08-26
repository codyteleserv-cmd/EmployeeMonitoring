using EmployeeMonitoring.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows.Forms;

namespace EmployeeMonitoring.Agent.UI;

/// <summary>
/// Main dashboard form showing agent status, activity, and controls.
/// </summary>
public class MainForm : Form
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MainForm> _logger;
    private readonly IPauseManager _pauseManager;
    private readonly IConsentManager _consentManager;
    private readonly IAgentIdentityProvider _identityProvider;
    
    private TabControl _tabControl = null!;
    private TabPage _overviewTab = null!;
    private TabPage _activityTab = null!;
    private TabPage _screenshotsTab = null!;
    private TabPage _dlpTab = null!;
    private TabPage _settingsTab = null!;
    
    // Overview tab controls
    private Label _statusLabel = null!;
    private Label _agentIdLabel = null!;
    private Label _userLabel = null!;
    private Label _departmentLabel = null!;
    private Label _pauseStatusLabel = null!;
    private Label _pauseTimeLabel = null!;
    private ProgressBar _pauseProgressBar = null!;
    private Button _pauseButton = null!;
    private Button _resumeButton = null!;
    
    // Activity tab controls
    private DataGridView _activityGrid = null!;
    private Label _productivityLabel = null!;
    
    // Screenshots tab controls
    private ListView _screenshotsList = null!;
    private PictureBox _screenshotPreview = null!;
    
    // DLP tab controls
    private DataGridView _dlpGrid = null!;
    
    // Settings tab controls
    private PropertyGrid _settingsGrid = null!;
    
    private System.Windows.Forms.Timer _uiUpdateTimer = null!;

    public MainForm(IServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<MainForm>>();
        _pauseManager = services.GetRequiredService<IPauseManager>();
        _consentManager = services.GetRequiredService<IConsentManager>();
        _identityProvider = services.GetRequiredService<IAgentIdentityProvider>();

        InitializeComponent();
        InitializeData();
        StartUiTimer();
        
        _logger.LogInformation("Main form initialized");
    }

    private void InitializeComponent()
    {
        Text = "Employee Monitoring Agent - Dashboard";
        Size = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 600);
        Icon = CreateIcon(Color.Green);

        _tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9)
        };

        CreateOverviewTab();
        CreateActivityTab();
        CreateScreenshotsTab();
        CreateDlpTab();
        CreateSettingsTab();

        Controls.Add(_tabControl);
    }

    private void CreateOverviewTab()
    {
        _overviewTab = new TabPage("Overview");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(20),
            ColumnStyles =
            {
                new ColumnStyle(SizeType.Absolute, 150),
                new ColumnStyle(SizeType.Percent, 100)
            }
        };

        // Status
        layout.Controls.Add(new Label { Text = "Status:", Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true }, 0, 0);
        _statusLabel = new Label { Text = "Running", ForeColor = Color.Green, Font = new Font("Segoe UI", 10), AutoSize = true };
        layout.Controls.Add(_statusLabel, 1, 0);

        // Agent ID
        layout.Controls.Add(new Label { Text = "Agent ID:", AutoSize = true }, 0, 1);
        _agentIdLabel = new Label { AutoSize = true };
        layout.Controls.Add(_agentIdLabel, 1, 1);

        // User
        layout.Controls.Add(new Label { Text = "User:", AutoSize = true }, 0, 2);
        _userLabel = new Label { AutoSize = true };
        layout.Controls.Add(_userLabel, 1, 2);

        // Department
        layout.Controls.Add(new Label { Text = "Department:", AutoSize = true }, 0, 3);
        _departmentLabel = new Label { AutoSize = true };
        layout.Controls.Add(_departmentLabel, 1, 3);

        // Pause Status
        layout.Controls.Add(new Label { Text = "Monitoring:", Font = new Font("Segoe UI", 10, FontStyle.Bold), AutoSize = true }, 0, 4);
        _pauseStatusLabel = new Label { Text = "Active", ForeColor = Color.Green, Font = new Font("Segoe UI", 10), AutoSize = true };
        layout.Controls.Add(_pauseStatusLabel, 1, 4);

        // Pause Time Used
        layout.Controls.Add(new Label { Text = "Pause Time Used Today:", AutoSize = true }, 0, 5);
        _pauseTimeLabel = new Label { AutoSize = true };
        layout.Controls.Add(_pauseTimeLabel, 1, 5);

        // Pause Progress Bar
        layout.Controls.Add(new Label { Text = "Daily Limit:", AutoSize = true }, 0, 6);
        _pauseProgressBar = new ProgressBar { Dock = DockStyle.Fill, Maximum = 100 };
        layout.Controls.Add(_pauseProgressBar, 1, 6);

        // Buttons
        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _pauseButton = new Button { Text = "Pause Monitoring", Size = new Size(150, 35), BackColor = Color.Orange };
        _pauseButton.Click += async (s, e) => await PauseAsync();
        buttonPanel.Controls.Add(_pauseButton);

        _resumeButton = new Button { Text = "Resume Monitoring", Size = new Size(150, 35), BackColor = Color.Green, Visible = false };
        _resumeButton.Click += async (s, e) => await ResumeAsync();
        buttonPanel.Controls.Add(_resumeButton);

        layout.Controls.Add(buttonPanel, 0, 7);
        layout.SetColumnSpan(buttonPanel, 2);

        _overviewTab.Controls.Add(layout);
        _tabControl.TabPages.Add(_overviewTab);
    }

    private void CreateActivityTab()
    {
        _activityTab = new TabPage("Activity");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, RowStyles = { new RowStyle(SizeType.Absolute, 40), new RowStyle(SizeType.Percent, 100) } };

        _productivityLabel = new Label { Text = "Productivity Score: --", Font = new Font("Segoe UI", 10, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(_productivityLabel, 0, 0);

        _activityGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        _activityGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", DataPropertyName = "Timestamp" },
            new DataGridViewTextBoxColumn { Name = "Process", HeaderText = "Process", DataPropertyName = "ProcessName" },
            new DataGridViewTextBoxColumn { Name = "Window", HeaderText = "Window Title", DataPropertyName = "WindowTitle" },
            new DataGridViewTextBoxColumn { Name = "Domain", HeaderText = "Domain", DataPropertyName = "Domain" },
            new DataGridViewTextBoxColumn { Name = "Productivity", HeaderText = "Productivity", DataPropertyName = "Productivity" },
            new DataGridViewTextBoxColumn { Name = "Idle", HeaderText = "Idle (s)", DataPropertyName = "IdleSeconds" },
            new DataGridViewTextBoxColumn { Name = "Input", HeaderText = "Input Level", DataPropertyName = "InputLevel" }
        );
        layout.Controls.Add(_activityGrid, 0, 1);

        _activityTab.Controls.Add(layout);
        _tabControl.TabPages.Add(_activityTab);
    }

    private void CreateScreenshotsTab()
    {
        _screenshotsTab = new TabPage("Screenshots");
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 200
        };

        _screenshotsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _screenshotsList.Columns.AddRange(
            new ColumnHeader { Text = "Time", Width = 120 },
            new ColumnHeader { Text = "Monitor", Width = 80 },
            new ColumnHeader { Text = "Resolution", Width = 120 },
            new ColumnHeader { Text = "Active Window", Width = 250 },
            new ColumnHeader { Text = "Process", Width = 150 },
            new ColumnHeader { Text = "Blurred", Width = 80 }
        );
        _screenshotsList.SelectedIndexChanged += OnScreenshotSelected;
        splitContainer.Panel1.Controls.Add(_screenshotsList);

        _screenshotPreview = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Black
        };
        splitContainer.Panel2.Controls.Add(_screenshotPreview);

        _screenshotsTab.Controls.Add(splitContainer);
        _tabControl.TabPages.Add(_screenshotsTab);
    }

    private void CreateDlpTab()
    {
        _dlpTab = new TabPage("DLP Events");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };

        _dlpGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        _dlpGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", DataPropertyName = "Timestamp" },
            new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Event Type", DataPropertyName = "Type" },
            new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity", DataPropertyName = "Severity" },
            new DataGridViewTextBoxColumn { Name = "Process", HeaderText = "Process", DataPropertyName = "ProcessName" },
            new DataGridViewTextBoxColumn { Name = "File", HeaderText = "File Path", DataPropertyName = "FilePath" },
            new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Details", DataPropertyName = "Details" },
            new DataGridViewCheckBoxColumn { Name = "Blocked", HeaderText = "Blocked", DataPropertyName = "Blocked" }
        );
        layout.Controls.Add(_dlpGrid, 0, 0);

        _dlpTab.Controls.Add(layout);
        _tabControl.TabPages.Add(_dlpTab);
    }

    private void CreateSettingsTab()
    {
        _settingsTab = new TabPage("Settings");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1 };

        _settingsGrid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            HelpVisible = true,
            ToolbarVisible = false,
            PropertySort = PropertySort.Categorized
        };
        layout.Controls.Add(_settingsGrid, 0, 0);

        _settingsTab.Controls.Add(layout);
        _tabControl.TabPages.Add(_settingsTab);
    }

    private void InitializeData()
    {
        _agentIdLabel.Text = _identityProvider.AgentId;
        _userLabel.Text = _identityProvider.UserDisplayName;
        _departmentLabel.Text = _identityProvider.Department;
        
        UpdatePauseUI();
    }

    private void StartUiTimer()
    {
        _uiUpdateTimer = new System.Windows.Forms.Timer { Interval = 5000 }; // 5 seconds
        _uiUpdateTimer.Tick += (s, e) => UpdateUI();
        _uiUpdateTimer.Start();
    }

    private void UpdateUI()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateUI));
            return;
        }

        UpdatePauseUI();
    }

    private void UpdatePauseUI()
    {
        var pauseState = _pauseManager.GetPauseState();
        var consentStatus = _consentManager.GetConsentStatus();

        if (pauseState.IsPaused)
        {
            _statusLabel.Text = "Paused";
            _statusLabel.ForeColor = Color.Orange;
            _pauseStatusLabel.Text = $"Paused: {pauseState.PauseReason}";
            _pauseStatusLabel.ForeColor = Color.Orange;
            _pauseButton.Visible = false;
            _resumeButton.Visible = true;
        }
        else
        {
            _statusLabel.Text = consentStatus.ConsentGiven ? "Running" : "Consent Required";
            _statusLabel.ForeColor = consentStatus.ConsentGiven ? Color.Green : Color.Gray;
            _pauseStatusLabel.Text = "Active";
            _pauseStatusLabel.ForeColor = Color.Green;
            _pauseButton.Visible = _pauseManager.CanPause();
            _resumeButton.Visible = false;
        }

        // Pause time
        var totalUsed = pauseState.TotalPauseDuration + pauseState.CurrentPauseDuration;
        var maxPause = pauseState.MaxPausePerDay;
        _pauseTimeLabel.Text = $"{totalUsed.TotalMinutes:F1} / {maxPause.TotalMinutes:F0} minutes";
        
        var progressPercent = maxPause > TimeSpan.Zero 
            ? (int)(totalUsed.TotalMinutes / maxPause.TotalMinutes * 100) 
            : 0;
        _pauseProgressBar.Value = Math.Clamp(progressPercent, 0, 100);
        _pauseProgressBar.ForeColor = progressPercent >= 90 ? Color.Red : 
            progressPercent >= 70 ? Color.Orange : Color.Green;
    }

    private async Task PauseAsync()
    {
        using var dialog = new PauseReasonDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var result = await _pauseManager.RequestPauseAsync(dialog.Reason);
            if (!result.Success)
            {
                MessageBox.Show(this, $"Failed to pause: {result.Message}", "Pause Failed", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdatePauseUI();
        }
    }

    private async Task ResumeAsync()
    {
        var result = await _pauseManager.RequestResumeAsync();
        if (!result.Success)
        {
            MessageBox.Show(this, $"Failed to resume: {result.Message}", "Resume Failed", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdatePauseUI();
    }

    private void OnScreenshotSelected(object? sender, EventArgs e)
    {
        if (_screenshotsList.SelectedItems.Count > 0)
        {
            var item = _screenshotsList.SelectedItems[0];
            // In real implementation, would load the screenshot image
            // _screenshotPreview.Image = LoadScreenshot(item.Tag as string);
        }
    }

    private Icon CreateIcon(Color color)
    {
        var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 4, 4, 24, 24);
            using var whiteBrush = new SolidBrush(Color.White);
            g.FillEllipse(whiteBrush, 12, 12, 8, 8);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide(); // Hide instead of close
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiUpdateTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}