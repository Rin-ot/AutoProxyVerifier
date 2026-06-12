using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProxyAutoAuth
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApplicationContext());
        }
    }

    public class AppConfig
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public int IntervalMinutes { get; set; } = 30;
        public string ProxyType { get; set; } = "System";
        public string ProxyAddress { get; set; } = "";
    }

    public static class ConfigManager
    {
        private static readonly string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProxyAutoAuth",
            "config.json");

        public static AppConfig Load()
        {
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                catch { }
            }
            return new AppConfig();
        }

        public static void Save(AppConfig config)
        {
            try
            {
                string dir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(config);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class SettingsForm : Form
    {
        private TextBox txtUsername;
        private TextBox txtPassword;
        private ComboBox cmbInterval;
        private ComboBox cmbProxyType;
        private TextBox txtProxyAddress;
        private Button btnSave;
        private Button btnCancel;
        public AppConfig Config { get; private set; }

        public SettingsForm(AppConfig currentConfig)
        {
            Config = currentConfig;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "設定 - Proxy Auto Authenticator";
            this.Size = new Size(320, 280);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            Label lblUser = new Label() { Text = "ユーザー名:", Left = 16, Top = 20, Width = 80 };
            txtUsername = new TextBox() { Left = 100, Top = 16, Width = 180, Text = Config.Username };

            Label lblPass = new Label() { Text = "パスワード:", Left = 16, Top = 52, Width = 80 };
            txtPassword = new TextBox() { Left = 100, Top = 48, Width = 180, Text = Config.Password, PasswordChar = '*' };

            Label lblInterval = new Label() { Text = "更新間隔(分):", Left = 16, Top = 84, Width = 80 };
            cmbInterval = new ComboBox() { Left = 100, Top = 80, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            
            int[] intervals = { 1, 2, 3, 5, 10, 15, 30, 60 };
            foreach (int interval in intervals)
            {
                cmbInterval.Items.Add(interval);
            }
            cmbInterval.SelectedItem = cmbInterval.Items.Contains(Config.IntervalMinutes) ? Config.IntervalMinutes : 30;

            Label lblType = new Label() { Text = "プロキシ種類:", Left = 16, Top = 116, Width = 80 };
            cmbProxyType = new ComboBox() { Left = 100, Top = 112, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbProxyType.Items.AddRange(new object[] { "System", "HTTP", "HTTPS", "SOCKS4", "SOCKS5" });
            cmbProxyType.SelectedItem = string.IsNullOrEmpty(Config.ProxyType) ? "System" : Config.ProxyType;

            Label lblAddr = new Label() { Text = "アドレス(IP:Port):", Left = 16, Top = 148, Width = 85 };
            txtProxyAddress = new TextBox() { Left = 100, Top = 144, Width = 180, Text = Config.ProxyAddress };

            cmbProxyType.SelectedIndexChanged += (s, e) =>
            {
                txtProxyAddress.Enabled = cmbProxyType.SelectedItem.ToString() != "System";
            };
            txtProxyAddress.Enabled = cmbProxyType.SelectedItem.ToString() != "System";

            btnSave = new Button() { Text = "保存", Left = 100, Top = 188, Width = 80 };
            btnSave.Click += (s, e) =>
            {
                if (cmbProxyType.SelectedItem.ToString() != "System" && string.IsNullOrWhiteSpace(txtProxyAddress.Text))
                {
                    MessageBox.Show("プロキシのアドレスを入力してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Config.Username = txtUsername.Text;
                Config.Password = txtPassword.Text;
                Config.IntervalMinutes = (int)cmbInterval.SelectedItem;
                Config.ProxyType = cmbProxyType.SelectedItem.ToString();
                Config.ProxyAddress = txtProxyAddress.Text;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            btnCancel = new Button() { Text = "キャンセル", Left = 196, Top = 188, Width = 80 };
            btnCancel.Click += (s, e) =>
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };

            this.Controls.Add(lblUser);
            this.Controls.Add(txtUsername);
            this.Controls.Add(lblPass);
            this.Controls.Add(txtPassword);
            this.Controls.Add(lblInterval);
            this.Controls.Add(cmbInterval);
            this.Controls.Add(lblType);
            this.Controls.Add(cmbProxyType);
            this.Controls.Add(lblAddr);
            this.Controls.Add(txtProxyAddress);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);
            this.AcceptButton = btnSave;
            this.CancelButton = btnCancel;
        }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private System.Threading.Timer authTimer;
        private HttpClient httpClient;
        private AppConfig config;

        private readonly string targetUrl = "http://captive.apple.com/hotspot-detect.html";

        public TrayApplicationContext()
        {
            config = ConfigManager.Load();

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = "Proxy Auto Authenticator"
            };

            trayIcon.ContextMenuStrip.Items.Add("認証を今すぐ実行", null, OnAuthenticateNow);
            trayIcon.ContextMenuStrip.Items.Add("設定", null, OnSettings);
            trayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            trayIcon.ContextMenuStrip.Items.Add("終了", null, OnExit);

            if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
            {
                ShowSettings();
            }
            else
            {
                ApplyConfiguration();
            }
        }

        private void ShowSettings()
        {
            using (var form = new SettingsForm(config))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    config = form.Config;
                    ConfigManager.Save(config);
                    ApplyConfiguration();
                }
            }
        }

        private void ApplyConfiguration()
        {
            if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
            {
                return;
            }

            InitializeHttpClient();
            StartTimer();
        }

        private void InitializeHttpClient()
        {
            httpClient?.Dispose();

            IWebProxy proxy = null;

            if (config.ProxyType == "System" || string.IsNullOrWhiteSpace(config.ProxyType))
            {
                proxy = WebRequest.GetSystemWebProxy();
            }
            else
            {
                try
                {
                    string scheme = config.ProxyType.ToLower();
                    string address = config.ProxyAddress;
                    
                    if (!address.Contains("://"))
                    {
                        address = $"{scheme}://{address}";
                    }
                    
                    proxy = new WebProxy(address);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Proxy URI Parsing Error: {ex.Message}");
                    proxy = WebRequest.GetSystemWebProxy();
                }
            }

            proxy.Credentials = new NetworkCredential(config.Username, config.Password);

            HttpClientHandler handler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true,
                PreAuthenticate = true,
                UseDefaultCredentials = false
            };

            httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private void StartTimer()
        {
            authTimer?.Dispose();
            authTimer = new System.Threading.Timer(
                _ => _ = PerformAuthenticationAsync(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(config.IntervalMinutes)
            );
        }

        private async Task PerformAuthenticationAsync()
        {
            if (httpClient == null) return;

            try
            {
                using (HttpResponseMessage response = await httpClient.GetAsync(targetUrl))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"StatusCode: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            }
        }

        private async void OnAuthenticateNow(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
            {
                MessageBox.Show("設定画面からユーザー名とパスワードを入力してください。", "未設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await PerformAuthenticationAsync();
            trayIcon.ShowBalloonTip(3000, "Proxy Auto Authenticator", "認証リクエストを送信しました。", ToolTipIcon.Info);
        }

        private void OnSettings(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private void OnExit(object sender, EventArgs e)
        {
            authTimer?.Dispose();
            httpClient?.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }
    }
}