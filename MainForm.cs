using System.Diagnostics;

namespace SklandAutoSign;

internal sealed class MainForm : Form
{
    private readonly TextBox _tokenBox = new();
    private readonly DateTimePicker _timePicker = new();
    private readonly CheckBox _arknightsCheckBox = new();
    private readonly CheckBox _endfieldCheckBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _saveButton = new();
    private readonly Button _testButton = new();
    private readonly Button _installButton = new();
    private readonly Button _removeButton = new();
    private AppSettings _settings = new();

    public MainForm()
    {
        Text = "森空岛 · 游戏自动签到";
        ClientSize = new Size(660, 510);
        MinimumSize = new Size(676, 549);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        Load += (_, _) => LoadSettings();
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "森空岛每日签到",
            Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(24, 22)
        };
        var description = new Label
        {
            Text = "直接调用森空岛接口，不启动模拟器。Token 仅以当前 Windows 用户可解密的形式保存在本机。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(27, 63)
        };

        var tokenLabel = new Label { Text = "账号 Token", AutoSize = true, Location = new Point(27, 104) };
        _tokenBox.Location = new Point(30, 129);
        _tokenBox.Size = new Size(595, 30);
        _tokenBox.UseSystemPasswordChar = true;
        _tokenBox.PlaceholderText = "粘贴 data.content 的内容，也可以直接粘贴整段 JSON";

        var showToken = new CheckBox { Text = "显示", AutoSize = true, Location = new Point(558, 105) };
        showToken.CheckedChanged += (_, _) => _tokenBox.UseSystemPasswordChar = !showToken.Checked;

        var openLogin = new Button { Text = "1. 打开森空岛登录页", Location = new Point(30, 174), Size = new Size(174, 34) };
        var openToken = new Button { Text = "2. 打开 Token 页面", Location = new Point(214, 174), Size = new Size(174, 34) };
        openLogin.Click += (_, _) => OpenUrl("https://www.skland.com/");
        openToken.Click += (_, _) => OpenUrl("https://web-api.skland.com/account/info/hg");

        var hint = new Label
        {
            Text = "先在浏览器登录森空岛，再打开 Token 页面；复制页面内容并粘贴到上方。请勿把 Token 发给任何人。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(30, 218)
        };

        var gamesLabel = new Label { Text = "签到游戏", AutoSize = true, Location = new Point(27, 257) };
        _arknightsCheckBox.Text = "明日方舟";
        _arknightsCheckBox.AutoSize = true;
        _arknightsCheckBox.Location = new Point(142, 254);
        _endfieldCheckBox.Text = "终末地";
        _endfieldCheckBox.AutoSize = true;
        _endfieldCheckBox.Location = new Point(250, 254);

        var timeLabel = new Label { Text = "每日签到时间", AutoSize = true, Location = new Point(27, 298) };
        _timePicker.Location = new Point(142, 293);
        _timePicker.Size = new Size(110, 30);
        _timePicker.Format = DateTimePickerFormat.Custom;
        _timePicker.CustomFormat = "HH:mm";
        _timePicker.ShowUpDown = true;

        _saveButton.Text = "保存设置";
        _saveButton.Location = new Point(30, 342);
        _saveButton.Size = new Size(115, 38);
        _saveButton.Click += (_, _) => SaveSettings(showDialog: true);

        _testButton.Text = "立即测试签到";
        _testButton.Location = new Point(155, 342);
        _testButton.Size = new Size(135, 38);
        _testButton.Click += async (_, _) => await TestSignAsync();

        _installButton.Text = "启用每日任务";
        _installButton.Location = new Point(300, 342);
        _installButton.Size = new Size(135, 38);
        _installButton.Click += (_, _) => InstallTask();

        _removeButton.Text = "停用每日任务";
        _removeButton.Location = new Point(445, 342);
        _removeButton.Size = new Size(135, 38);
        _removeButton.Click += (_, _) => RemoveTask();

        var openLog = new LinkLabel { Text = "打开签到日志", AutoSize = true, Location = new Point(524, 406) };
        openLog.Click += (_, _) => OpenLog();

        _statusLabel.Text = "状态：正在读取设置…";
        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(30, 402);
        _statusLabel.Size = new Size(480, 58);

        var footer = new Label
        {
            Text = "说明：程序无需常驻；若电脑在设定时间休眠或关机，会在恢复且用户登录后补跑一次。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(30, 476)
        };

        Controls.AddRange(new Control[]
        {
            title, description, tokenLabel, _tokenBox, showToken, openLogin, openToken, hint,
            gamesLabel, _arknightsCheckBox, _endfieldCheckBox, timeLabel, _timePicker,
            _saveButton, _testButton, _installButton, _removeButton,
            _statusLabel, openLog, footer
        });
    }

    private void LoadSettings()
    {
        try
        {
            _settings = SettingsStore.Load();
            _arknightsCheckBox.Checked = _settings.EnableArknights;
            _endfieldCheckBox.Checked = _settings.EnableEndfield;
            if (TimeSpan.TryParse(_settings.DailyTime, out TimeSpan time))
                _timePicker.Value = DateTime.Today.Add(time);
            bool scheduled = TaskSchedulerManager.Exists();
            if (!scheduled)
            {
                _statusLabel.Text = "状态：每日任务尚未启用。";
            }
            else
            {
                TimeSpan? actualTime = TaskSchedulerManager.GetScheduledTime();
                TimeSpan configuredTime = new(_timePicker.Value.Hour, _timePicker.Value.Minute, 0);
                if (actualTime is TimeSpan taskTime)
                {
                    TimeSpan actualMinute = new(taskTime.Hours, taskTime.Minutes, 0);
                    _statusLabel.Text = actualMinute == configuredTime
                        ? $"状态：每日任务已启用，将在 {actualMinute.Hours:00}:{actualMinute.Minutes:00} 运行。"
                        : $"状态：每日任务实际为 {actualMinute.Hours:00}:{actualMinute.Minutes:00}；保存设置可同步为 {configuredTime.Hours:00}:{configuredTime.Minutes:00}。";
                }
                else
                {
                    _statusLabel.Text = "状态：每日任务已启用。";
                }
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"状态：读取设置失败：{ex.Message}";
        }
    }

    private bool SaveSettings(bool showDialog, bool syncExistingTask = true)
    {
        try
        {
            string pastedToken = _tokenBox.Text.Trim();
            if (!string.IsNullOrEmpty(pastedToken))
            {
                string normalized = SklandClient.NormalizeToken(pastedToken);
                _settings.EncryptedToken = SecretProtector.Protect(normalized);
                _tokenBox.Clear();
            }
            if (string.IsNullOrWhiteSpace(_settings.EncryptedToken))
                throw new InvalidOperationException("请先粘贴 Token。");
            if (!_arknightsCheckBox.Checked && !_endfieldCheckBox.Checked)
                throw new InvalidOperationException("请至少选择一款游戏。");

            _settings.DailyTime = _timePicker.Value.ToString("HH:mm");
            _settings.EnableArknights = _arknightsCheckBox.Checked;
            _settings.EnableEndfield = _endfieldCheckBox.Checked;
            SettingsStore.Save(_settings);

            bool taskUpdated = false;
            if (syncExistingTask && TaskSchedulerManager.Exists())
            {
                var updateResult = TaskSchedulerManager.Install(_timePicker.Value.TimeOfDay);
                if (!updateResult.Success)
                {
                    string detail = string.IsNullOrWhiteSpace(updateResult.Message) ? "任务计划程序拒绝了请求。" : updateResult.Message;
                    _statusLabel.Text = "状态：设置已保存，但每日任务更新时间失败。";
                    if (showDialog)
                    {
                        MessageBox.Show(this,
                            $"设置已保存，但现有每日任务未能同步更新时间：{detail}\n\n请点击“启用每日任务”重试。",
                            "部分完成",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                    return true;
                }
                taskUpdated = true;
            }

            _statusLabel.Text = taskUpdated
                ? $"状态：设置已保存，每日任务已同步为 {_timePicker.Value:HH:mm}。"
                : "状态：设置已加密保存。";
            if (showDialog)
            {
                string message = taskUpdated
                    ? $"设置已保存，并已将每日任务更新时间同步为 {_timePicker.Value:HH:mm}。"
                    : "设置已保存。";
                MessageBox.Show(this, message, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task TestSignAsync()
    {
        if (!SaveSettings(showDialog: false))
            return;

        SetBusy(true, "状态：正在连接森空岛并签到…");
        try
        {
            string token = SecretProtector.Unprotect(_settings.EncryptedToken);
            using var client = new SklandClient();
            IReadOnlyList<SignResult> results = await client.SignSelectedAsync(
                token, _settings.EnableArknights, _settings.EnableEndfield);
            foreach (SignResult result in results)
                LogWriter.Write(result.Message);

            int successCount = results.Count(x => x.Success);
            bool allSucceeded = successCount == results.Count;
            string message = string.Join(Environment.NewLine, results.Select(x => x.Message));
            _statusLabel.Text = $"状态：已处理 {results.Count} 个角色，成功或已签到 {successCount} 个。";
            MessageBox.Show(this, message, "签到结果", MessageBoxButtons.OK,
                allSucceeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            string message = $"测试签到失败：{ex.Message}";
            LogWriter.Write(message);
            _statusLabel.Text = $"状态：{message}";
            MessageBox.Show(this, message, "签到失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private void InstallTask()
    {
        if (!SaveSettings(showDialog: false, syncExistingTask: false))
            return;
        try
        {
            TimeSpan time = _timePicker.Value.TimeOfDay;
            var result = TaskSchedulerManager.Install(time);
            if (!result.Success)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Message) ? "任务计划程序拒绝了请求。" : result.Message);
            _statusLabel.Text = $"状态：每日任务已启用，将在 {time:hh\\:mm} 运行。";
            MessageBox.Show(this, "每日任务已启用。程序无需开机自启动或保持窗口打开。", "完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启用失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RemoveTask()
    {
        var result = TaskSchedulerManager.Remove();
        if (result.Success)
        {
            _statusLabel.Text = "状态：每日任务已停用；账号 Token 仍保留在本机。";
            MessageBox.Show(this, "每日任务已停用。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, string.IsNullOrWhiteSpace(result.Message) ? "任务不存在或无法删除。" : result.Message,
                "停用结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _saveButton.Enabled = !busy;
        _testButton.Enabled = !busy;
        _installButton.Enabled = !busy;
        _removeButton.Enabled = !busy;
        _arknightsCheckBox.Enabled = !busy;
        _endfieldCheckBox.Enabled = !busy;
        _timePicker.Enabled = !busy;
        _statusLabel.Text = status;
        UseWaitCursor = busy;
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void OpenLog()
    {
        AppPaths.EnsureDataDirectory();
        if (!File.Exists(AppPaths.LogFile))
            File.WriteAllText(AppPaths.LogFile, "", new System.Text.UTF8Encoding(false));
        Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
    }
}
