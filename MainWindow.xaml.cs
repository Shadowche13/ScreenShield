using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ScreenShield
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, Dictionary<string, string>> _translations;

        private string _profilePath = string.Empty;
        private bool isFilterActive = false;
        private bool controlsUnlocked = false;

        // Gamma ramp
        [StructLayout(LayoutKind.Sequential)]
        private struct RAMP
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Blue;
        }

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);
        [DllImport("gdi32.dll")]
        private static extern bool GetDeviceGammaRamp(IntPtr hDC, ref RAMP lpRamp);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private RAMP _originalRamp;
        private bool _originalRampSaved = false;
        private bool _gammaSupported = false;

        // overlay fallback
        private OverlayWindow? _overlay;

        // slider throttle
        private DispatcherTimer? _sliderThrottleTimer;
        private double _pendingIntensity = 50.0;

        // Usage stats
        private DispatcherTimer? _usageTimer;
        private DateTime _statsDate;
        private int _secondsToday;
        private string _statsPath = string.Empty;
        private const int IdleThresholdSeconds = 60;

        public MainWindow()
        {
            _translations = BuildTranslations();

            InitializeComponent();

            SaveDefaultGamma();

            _profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenShield", "customProfile.json");
            LoadCustomProfile();

            if (MainTabs != null) MainTabs.SelectedIndex = 0;
            SetActiveMenu(0);

            _sliderThrottleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _sliderThrottleTimer.Tick += (s, e) =>
            {
                _sliderThrottleTimer.Stop();
                if (isFilterActive)
                {
                    if (_gammaSupported)
                        ApplyGamma(_pendingIntensity);
                    else
                        ShowFallbackOverlay(_pendingIntensity);
                }
            };

            if (sliderIntensity != null)
            {
                sliderIntensity.ValueChanged += sliderIntensity_ValueChanged;
                _pendingIntensity = sliderIntensity.Value;
            }

            if (comboLanguage != null)
            {
                // dropdown selects language
                comboLanguage.SelectedIndex = 0;
                if (comboLanguage.SelectedItem is ComboBoxItem ci && ci.Tag is string code)
                    ApplyLanguage(code);
            }

            ApplyTheme("dark");

            var appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenShield");
            if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
            _statsPath = Path.Combine(appDir, "stats.json");
            LoadStats();

            _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _usageTimer.Tick += UsageTimer_Tick;
            _usageTimer.Start();
        }

        private Dictionary<string, Dictionary<string, string>> BuildTranslations()
        {
            var t = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            t["en"] = new Dictionary<string, string>
            {
                ["menuDashboard"] = "Dashboard",
                ["menuStats"] = "Statistics",
                ["menuSettings"] = "Settings",
                ["lblParentalAccess"] = "🔒 PARENTAL ACCESS",
                ["lblEnterPassword"] = "Enter Admin Password",
                ["btnUnlock"] = "UNLOCK",
                ["lblShieldControl"] = "🛡️ SHIELD CONTROL",
                ["lblStatusLocked"] = "Shield control (Locked)",
                ["lblStatusUnlocked"] = "Shield control (Unlocked)",
                ["lblIntensityText"] = "Filter intensity:",
                ["btnToggleOn"] = "Enable Shield",
                ["btnToggleOff"] = "Disable Shield",
                ["lblLockMessage"] = "Enter password to unlock shield controls.",
                ["lblStatsTitle"] = "📊 SCREEN TIME STATISTICS",
                ["lblStatsTime"] = "Screen time today:",
                ["lblStatsSaved"] = "💙 Eye strain saved: Optimal",
                ["lblSettingsTitle"] = "⚙️ APPLICATION SETTINGS",
                ["lblLanguageSelect"] = "Select language:"
            };

            // Bulgarian (fully localized)
            t["bg"] = new Dictionary<string, string>
            {
                ["menuDashboard"] = "Табло",
                ["menuStats"] = "Статистика",
                ["menuSettings"] = "Настройки",
                ["lblParentalAccess"] = "🔒 РОДИТЕЛСКИ ДОСТЪП",
                ["lblEnterPassword"] = "Въведете администраторска парола",
                ["btnUnlock"] = "ОТКЛЮЧИ",
                ["lblShieldControl"] = "🛡️ КОНТРОЛ НА ЩИТА",
                ["lblStatusLocked"] = "Контрол на филтъра (Заключен)",
                ["lblStatusUnlocked"] = "Контрол на филтъра (Отключен)",
                ["lblIntensityText"] = "Интензивност на филтъра:",
                ["btnToggleOn"] = "Активирай щита",
                ["btnToggleOff"] = "Деактивирай щита",
                ["lblLockMessage"] = "Въведете парола, за да отключите контролите на щита.",
                ["lblStatsTitle"] = "📊 СТАТИСТИКА ЗА ВРЕМЕ ПРЕД ЕКРАНА",
                ["lblStatsTime"] = "Време пред екрана днес:",
                ["lblStatsSaved"] = "💙 Намаляване на натоварването на очите: Оптимално",
                ["lblSettingsTitle"] = "⚙️ НАСТРОЙКИ НА ПРИЛОЖЕНИЕТО",
                ["lblLanguageSelect"] = "Изберете език:"
            };

            // Russian
            t["ru"] = new Dictionary<string, string>(t["en"])
            {
                ["menuDashboard"] = "Панель",
                ["menuStats"] = "Статистика",
                ["menuSettings"] = "Настройки",
                ["lblParentalAccess"] = "🔒 РОДИТЕЛЬСКИЙ ДОСТУП",
                ["lblEnterPassword"] = "Введите пароль администратора",
                ["btnUnlock"] = "ОТКРЫТЬ",
                ["lblShieldControl"] = "🛡️ УПРАВЛЕНИЕ ЩИТОМ",
                ["lblStatusLocked"] = "Контроль фильтра (Заблокирован)",
                ["lblStatusUnlocked"] = "Контроль фильтра (Разблокирован)",
                ["lblIntensityText"] = "Интенсивность фильтра:",
                ["btnToggleOn"] = "Включить щит",
                ["btnToggleOff"] = "Отключить щит",
                ["lblLockMessage"] = "Введите пароля чтобы разблокировать управление щитом.",
                ["lblStatsTitle"] = "📊 СТАТИСТИКА ВРЕМЕНИ ЭКРАНА",
                ["lblStatsTime"] = "Время перед экраном днес:",
                ["lblStatsSaved"] = "💙 Снижение нагрузки на глаза: Оптимально",
                ["lblLanguageSelect"] = "Выберите язык:"
            };

            // Turkish
            t["tr"] = new Dictionary<string, string>(t["en"])
            {
                ["menuDashboard"] = "Gösterge",
                ["menuStats"] = "İstatistikler",
                ["menuSettings"] = "Ayarlar",
                ["lblParentalAccess"] = "🔒 VELİ ERİŞİMİ",
                ["lblEnterPassword"] = "Yönetici Parolasını Girin",
                ["btnUnlock"] = "AÇ",
                ["lblShieldControl"] = "🛡️ KORUYUCU KONTROL",
                ["lblStatusLocked"] = "Kalkan kontrolü (Kilitli)",
                ["lblStatusUnlocked"] = "Kalkan kontrolü (Açık)",
                ["lblIntensityText"] = "Filtre yoğunluğu:",
                ["btnToggleOn"] = "Kalkanı Etkinleştir",
                ["btnToggleOff"] = "Kalkanı Devre Dışı Bırak",
                ["lblLockMessage"] = "Kalkan kontrollerinin kilidini açmak için şifre girin.",
                ["lblStatsTitle"] = "📊 EKRAN SÜRESİ İSTATİSTİKLERİ",
                ["lblStatsTime"] = "Bugün ekranda geçirilen süre:",
                ["lblStatsSaved"] = "💙 Göz yorgunluğu azalması: Optimal",
                ["lblLanguageSelect"] = "Dil seçin:"
            };

            // German
            t["de"] = new Dictionary<string, string>(t["en"])
            {
                ["menuDashboard"] = "Übersicht",
                ["menuStats"] = "Statistiken",
                ["menuSettings"] = "Einstellungen",
                ["lblParentalAccess"] = "🔒 ELTERLICHER ZUGRIFF",
                ["lblEnterPassword"] = "Admin-Passwort eingeben",
                ["btnUnlock"] = "ENTSPERREN",
                ["lblShieldControl"] = "🛡️ SCHILDKONTROLLE",
                ["lblStatusLocked"] = "Schildsteuerung (Gesperrt)",
                ["lblStatusUnlocked"] = "Schildsteuerung (Entsperrt)",
                ["lblIntensityText"] = "Filterstärke:",
                ["btnToggleOn"] = "Schild aktivieren",
                ["btnToggleOff"] = "Schild deaktivieren",
                ["lblLockMessage"] = "Geben Sie das Passwort ein, um die Schildsteuerung zu entsperren.",
                ["lblStatsTitle"] = "📊 BILANZ DER BILDSCHIRMZEIT",
                ["lblStatsTime"] = "Bildschirmzeit heute:",
                ["lblStatsSaved"] = "💙 Augenbelastung reduziert: Optimal",
                ["lblLanguageSelect"] = "Sprache auswählen:"
            };

            // Arabic
            t["ar"] = new Dictionary<string, string>(t["en"])
            {
                ["menuDashboard"] = "لوحة القيادة",
                ["menuStats"] = "الإحصائيات",
                ["menuSettings"] = "الإعدادات",
                ["lblParentalAccess"] = "🔒 وصول الوالدين",
                ["lblEnterPassword"] = "أدخل كلمة مرور المسؤول",
                ["btnUnlock"] = "فتح",
                ["lblShieldControl"] = "🛡️ تحكم الدرع",
                ["lblStatusLocked"] = "التحكم (مقفل)",
                ["lblStatusUnlocked"] = "التحكم (مفتوح)",
                ["lblIntensityText"] = "شدة الفلتر:",
                ["btnToggleOn"] = "تمكين الدرع",
                ["btnToggleOff"] = "تعطيل الدرع",
                ["lblLockMessage"] = "أدخل كلمة المرور لفتح عناصر التحكم في الدرع.",
                ["lblStatsTitle"] = "📊 إحصاءات وقت الشاشة",
                ["lblStatsTime"] = "وقت الشاشة اليوم:",
                ["lblStatsSaved"] = "💙 تقليل إجهاد العين: مثالي",
                ["lblLanguageSelect"] = "اختر اللغة:"
            };

            // Chinese (Simplified)
            t["zh"] = new Dictionary<string, string>(t["en"])
            {
                ["menuDashboard"] = "仪表板",
                ["menuStats"] = "统计",
                ["menuSettings"] = "设置",
                ["lblParentalAccess"] = "🔒 家长访问",
                ["lblEnterPassword"] = "输入管理员密码",
                ["btnUnlock"] = "解锁",
                ["lblShieldControl"] = "🛡️ 护盾控制",
                ["lblStatusLocked"] = "护盾控制（已锁定）",
                ["lblStatusUnlocked"] = "护盾控制（已解锁）",
                ["lblIntensityText"] = "过滤强度：",
                ["btnToggleOn"] = "启用护盾",
                ["btnToggleOff"] = "禁用护盾",
                ["lblLockMessage"] = "输入密码以解锁护盾控制。",
                ["lblStatsTitle"] = "📊 屏幕使用统计",
                ["lblStatsTime"] = "今天屏幕时间：",
                ["lblStatsSaved"] = "💙 减轻眼睛疲劳：最佳",
                ["lblLanguageSelect"] = "选择语言："
            };

            // Japanese
            t["ja"] = new Dictionary<string, string>(t["en"])
            {
                ["menuDashboard"] = "ダッシュボード",
                ["menuStats"] = "統計",
                ["menuSettings"] = "設定",
                ["lblParentalAccess"] = "🔒 保護者のアクセス",
                ["lblEnterPassword"] = "管理者パスワードを入力してください",
                ["btnUnlock"] = "解除",
                ["lblShieldControl"] = "🛡️ シールド制御",
                ["lblStatusLocked"] = "シールド制御（ロック済み）",
                ["lblStatusUnlocked"] = "シールド制御（解除済み）",
                ["lblIntensityText"] = "フィルター強度：",
                ["btnToggleOn"] = "シールドを有効にする",
                ["btnToggleOff"] = "シールドを無効にする",
                ["lblLockMessage"] = "シールドコントロールのロックを解除するにはパスワードを入力してください。",
                ["lblStatsTitle"] = "📊 画面使用時間の統計",
                ["lblStatsTime"] = "本日の画面時間：",
                ["lblStatsSaved"] = "💙 目の疲れ軽減：最適",
                ["lblLanguageSelect"] = "言語を選択："
            };

            // Spanish
            t["es"] = new Dictionary<string, string>(t["en"])
            {
                ["menuDashboard"] = "Panel",
                ["menuStats"] = "Estadísticas",
                ["menuSettings"] = "Configuración",
                ["lblParentalAccess"] = "🔒 ACCESO PARENTAL",
                ["lblEnterPassword"] = "Ingrese la contraseña de administrador",
                ["btnUnlock"] = "DESBLOQUEAR",
                ["lblShieldControl"] = "🛡️ CONTROL DEL ESCUDO",
                ["lblStatusLocked"] = "Control (Bloqueado)",
                ["lblStatusUnlocked"] = "Control (Desbloqueado)",
                ["lblIntensityText"] = "Intensidad del filtro:",
                ["btnToggleOn"] = "Activar escudo",
                ["btnToggleOff"] = "Desactivar escudo",
                ["lblLockMessage"] = "Ingrese la contraseña para desbloquear los controles del escudo.",
                ["lblStatsTitle"] = "📊 ESTADÍSTICAS DE TIEMPO DE PANTALLA",
                ["lblStatsTime"] = "Tiempo frente a la pantalla hoy:",
                ["lblStatsSaved"] = "💙 Reducción de fatiga ocular: Óptimo",
                ["lblLanguageSelect"] = "Seleccione el idioma:"
            };

            return t;
        }

        private void ApplyLanguage(string code)
        {
            if (string.IsNullOrEmpty(code) || _translations == null) return;
            if (!_translations.TryGetValue(code, out var m)) return;

            menuDashboard.Content = m["menuDashboard"];
            menuStats.Content = m["menuStats"];
            menuSettings.Content = m["menuSettings"];

            lblParentalAccess.Text = m["lblParentalAccess"];
            lblEnterPassword.Text = m["lblEnterPassword"];
            btnUnlock.Content = m["btnUnlock"];
            lblShieldControl.Text = m["lblShieldControl"];
            lblIntensityText.Text = m["lblIntensityText"];
            btnToggle.Content = isFilterActive ? (m.ContainsKey("btnToggleOff") ? m["btnToggleOff"] : m["btnToggleOn"]) : m["btnToggleOn"];
            lblShieldHint.Text = m.ContainsKey("lblLockMessage") ? m["lblLockMessage"] : m["lblLanguageSelect"];

            lblStatus.Text = controlsUnlocked ? (m.ContainsKey("lblStatusUnlocked") ? m["lblStatusUnlocked"] : m["lblStatusLocked"]) : m["lblStatusLocked"];
            lblStatsTitle.Text = m.ContainsKey("lblStatsTitle") ? m["lblStatsTitle"] : _translations["en"]["lblStatsTitle"];
            lblStatsTime.Text = $"{(m.ContainsKey("lblStatsTime") ? m["lblStatsTime"] : _translations["en"]["lblStatsTime"])} {FormatSeconds(_secondsToday)}";
            lblStatsSaved.Text = m.ContainsKey("lblStatsSaved") ? m["lblStatsSaved"] : _translations["en"]["lblStatsSaved"];
            lblSettingsTitle.Text = m.ContainsKey("lblSettingsTitle") ? m["lblSettingsTitle"] : _translations["en"]["lblSettingsTitle"];
            lblLanguageSelect.Text = m.ContainsKey("lblLanguageSelect") ? m["lblLanguageSelect"] : _translations["en"]["lblLanguageSelect"];
        }

        private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (comboLanguage?.SelectedItem is ComboBoxItem item && item.Tag is string code)
                ApplyLanguage(code);
        }

        private void Menu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && int.TryParse((b.Tag ?? "").ToString(), out var idx))
            {
                if (MainTabs != null && idx >= 0 && idx < MainTabs.Items.Count)
                {
                    MainTabs.SelectedIndex = idx;
                    SetActiveMenu(idx);
                }
            }
        }

        private void btnTheme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.Tag is string tag) ApplyTheme(tag);
        }

        private void ApplyTheme(string themeTag)
        {
            // Update the Window's resources so DynamicResource bindings in this Window react
            var appRes = this.Resources;

            if (string.Equals(themeTag, "light", StringComparison.OrdinalIgnoreCase))
            {
                appRes["WindowBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                appRes["WindowForegroundBrush"] = new SolidColorBrush(Colors.Black);
                appRes["CardBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(250, 250, 250));
                appRes["CardBorderBrush"] = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                appRes["LeftNavBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(245, 245, 245));
                appRes["ControlBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                appRes["PrimaryAccentBrush"] = new SolidColorBrush(Color.FromRgb(58, 154, 217));
                appRes["MutedForegroundBrush"] = new SolidColorBrush(Color.FromRgb(120, 120, 120));
            }
            else
            {
                appRes["WindowBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(15, 15, 16));
                appRes["WindowForegroundBrush"] = new SolidColorBrush(Colors.White);
                appRes["CardBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(19, 19, 19));
                appRes["CardBorderBrush"] = new SolidColorBrush(Color.FromRgb(42, 42, 42));
                appRes["LeftNavBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(17, 18, 20));
                appRes["ControlBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(16, 17, 18));
                appRes["PrimaryAccentBrush"] = new SolidColorBrush(Color.FromRgb(58, 154, 217));
                appRes["MutedForegroundBrush"] = new SolidColorBrush(Color.FromRgb(141, 141, 141));
            }

            var accent = appRes["PrimaryAccentBrush"] as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(58, 154, 217));
            var fg = appRes["WindowForegroundBrush"] as SolidColorBrush ?? new SolidColorBrush(Colors.White);

            if (btnThemeDark != null && btnThemeLight != null)
            {
                if (string.Equals(themeTag, "light", StringComparison.OrdinalIgnoreCase))
                {
                    btnThemeLight.Background = accent;
                    btnThemeLight.Foreground = Brushes.White;
                    btnThemeDark.Background = Brushes.Transparent;
                    btnThemeDark.Foreground = fg;
                }
                else
                {
                    btnThemeDark.Background = accent;
                    btnThemeDark.Foreground = Brushes.White;
                    btnThemeLight.Background = Brushes.Transparent;
                    btnThemeLight.Foreground = fg;
                }
            }
        }

        private void btnUnlock_Click(object sender, RoutedEventArgs e)
        {
            if (txtPassword != null && txtPassword.Text == "1234")
            {
                controlsUnlocked = true;
                if (btnToggle != null) btnToggle.IsEnabled = true;
                if (sliderIntensity != null) sliderIntensity.IsEnabled = true;
                if (btnGamingMode != null) btnGamingMode.IsEnabled = true;
                if (btnArtistMode != null) btnArtistMode.IsEnabled = true;

                lblShieldHint.Text = "Controls unlocked.";
                lblStatus.Text = _translations.ContainsKey("en") ? _translations["en"]["lblStatusUnlocked"] : "Unlocked";
                lblStatus.Foreground = Brushes.Green;
            }
            else
            {
                MessageBox.Show("Wrong password", "Unlock", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPassword?.Clear();
            }
        }

        private void btnToggle_Click(object sender, RoutedEventArgs e)
        {
            isFilterActive = !isFilterActive;

            string onText = _translations["en"]["btnToggleOn"];
            string offText = _translations["en"]["btnToggleOff"];
            if (comboLanguage?.SelectedItem is ComboBoxItem ci && ci.Tag is string code && _translations.TryGetValue(code, out var m))
            {
                onText = m.ContainsKey("btnToggleOn") ? m["btnToggleOn"] : onText;
                offText = m.ContainsKey("btnToggleOff") ? m["btnToggleOff"] : offText;
            }

            if (btnToggle != null) btnToggle.Content = isFilterActive ? offText : onText;

            if (isFilterActive)
            {
                double intensity = sliderIntensity?.Value ?? 50.0;
                if (_gammaSupported) ApplyGamma(intensity); else ShowFallbackOverlay(intensity);
                if (sliderIntensity != null) sliderIntensity.IsEnabled = true;
            }
            else
            {
                if (_gammaSupported) RestoreDefaultGamma(); else HideFallbackOverlay();
                if (sliderIntensity != null) sliderIntensity.IsEnabled = false;
            }
        }

        private void btnGamingMode_Click(object sender, RoutedEventArgs e) => ApplyPreset(70.0);
        private void btnArtistMode_Click(object sender, RoutedEventArgs e) => ApplyPreset(40.0);

        private void ApplyPreset(double value)
        {
            if (sliderIntensity != null) sliderIntensity.Value = value;
            if (txtIntensityValue != null) txtIntensityValue.Text = $"{(int)value}%";
            if (lblActivePercent != null) lblActivePercent.Text = $"{(int)value}%";

            if (!isFilterActive)
            {
                isFilterActive = true;
                if (btnToggle != null) btnToggle.Content = _translations["en"]["btnToggleOff"];
                if (btnToggle != null) btnToggle.IsEnabled = true;
                if (sliderIntensity != null) sliderIntensity.IsEnabled = true;
            }

            if (_gammaSupported) ApplyGamma(value); else ShowFallbackOverlay(value);
        }

        private void SaveDefaultGamma()
        {
            try
            {
                var hdc = GetDC(IntPtr.Zero);
                var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
                if (GetDeviceGammaRamp(hdc, ref ramp))
                {
                    _originalRamp = ramp;
                    _originalRampSaved = true;
                    _gammaSupported = true;
                }
                else _gammaSupported = false;
                ReleaseDC(IntPtr.Zero, hdc);
            }
            catch
            {
                _originalRampSaved = false;
                _gammaSupported = false;
            }
        }

        private void RestoreDefaultGamma()
        {
            try
            {
                if (!_originalRampSaved) return;
                var hdc = GetDC(IntPtr.Zero);
                SetDeviceGammaRamp(hdc, ref _originalRamp);
                ReleaseDC(IntPtr.Zero, hdc);
            }
            catch { }
        }

        private void ApplyGamma(double intensityPercent)
        {
            // gentle mapping: intensity 10..100 -> scale reduction up to ~45%
            double frac = Math.Max(0.0, Math.Min(1.0, (intensityPercent - 10.0) / 90.0));
            double maxBlueReduction = 0.45;
            double blueScale = 1.0 - frac * maxBlueReduction;
            double rgBoost = 1.0 + frac * 0.06;

            var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
            for (int i = 0; i < 256; i++)
            {
                double v = i / 255.0;
                double linear = Math.Pow(v, 1.0);
                int r = (int)Math.Round(Math.Min(65535.0, linear * 65535.0 * rgBoost));
                int g = (int)Math.Round(Math.Min(65535.0, linear * 65535.0 * rgBoost));
                int b = (int)Math.Round(Math.Min(65535.0, linear * 65535.0 * blueScale));
                ramp.Red[i] = (ushort)r;
                ramp.Green[i] = (ushort)g;
                ramp.Blue[i] = (ushort)b;
            }

            try
            {
                var hdc = GetDC(IntPtr.Zero);
                var ok = SetDeviceGammaRamp(hdc, ref ramp);
                ReleaseDC(IntPtr.Zero, hdc);
                if (!ok) ShowFallbackOverlay(intensityPercent); else HideFallbackOverlay();
            }
            catch
            {
                ShowFallbackOverlay(intensityPercent);
            }
        }

        private void ShowFallbackOverlay(double intensityPercent)
        {
            var ov = _overlay ??= new OverlayWindow();
            double frac = Math.Max(0.0, Math.Min(1.0, (intensityPercent - 10.0) / 90.0));
            double alpha = 0.06 + frac * 0.12;
            byte a = (byte)(Math.Max(0.0, Math.Min(1.0, alpha)) * 255);
            ov.SetOverlayColor(Color.FromArgb(a, 255, 170, 100));
            if (!ov.IsVisible) ov.Show();
        }

        private void HideFallbackOverlay()
        {
            if (_overlay != null && _overlay.IsVisible) _overlay.Hide();
        }

        private void sliderIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double val = sliderIntensity?.Value ?? _pendingIntensity;
            if (txtIntensityValue != null) txtIntensityValue.Text = $"{(int)val}%";
            if (lblActivePercent != null) lblActivePercent.Text = $"{(int)val}%";
            _pendingIntensity = val;
            _sliderThrottleTimer?.Stop();
            _sliderThrottleTimer?.Start();
        }

        private void LoadStats()
        {
            try
            {
                if (File.Exists(_statsPath))
                {
                    var json = File.ReadAllText(_statsPath);
                    var doc = JsonSerializer.Deserialize<StatsFile>(json);
                    if (doc != null && DateTime.TryParse(doc.Date, out var d) && d.Date == DateTime.Now.Date)
                    {
                        _statsDate = d.Date;
                        _secondsToday = doc.Seconds;
                    }
                    else { _statsDate = DateTime.Now.Date; _secondsToday = 0; }
                }
                else { _statsDate = DateTime.Now.Date; _secondsToday = 0; }
            }
            catch { _statsDate = DateTime.Now.Date; _secondsToday = 0; }

            if (lblStatsTime != null) lblStatsTime.Text = $"{_translations["en"]["lblStatsTime"]} {FormatSeconds(_secondsToday)}";
        }

        private void SaveStats()
        {
            try
            {
                var doc = new StatsFile { Date = DateTime.Now.Date.ToString("o"), Seconds = _secondsToday };
                File.WriteAllText(_statsPath, JsonSerializer.Serialize(doc));
            }
            catch { }
        }

        private void UsageTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_statsDate.Date != DateTime.Now.Date) { _statsDate = DateTime.Now.Date; _secondsToday = 0; }
                var idle = GetIdleSeconds();
                if (idle < IdleThresholdSeconds) _secondsToday++;
                if (lblStatsTime != null) lblStatsTime.Text = $"{_translations["en"]["lblStatsTime"]} {FormatSeconds(_secondsToday)}";
                if (_secondsToday % 60 == 0) SaveStats();
            }
            catch { }
        }

        private string FormatSeconds(int secs)
        {
            var ts = TimeSpan.FromSeconds(secs);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        private int GetIdleSeconds()
        {
            var last = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref last)) return int.MaxValue;
            var lastTick = last.dwTime;
            var idleMs = (long)GetTickCount64() - (long)lastTick;
            if (idleMs < 0) idleMs = 0;
            return (int)(idleMs / 1000);
        }

        protected override void OnClosed(EventArgs e)
        {
            try { RestoreDefaultGamma(); }
            catch { }
            base.OnClosed(e);
        }

        private void LoadCustomProfile() { /* placeholder */ }
        private void SetActiveMenu(int idx) { /* placeholder */ }

        private class StatsFile { public string Date { get; set; } = string.Empty; public int Seconds { get; set; } }

        private class OverlayWindow : Window
        {
            private readonly Border _root;
            public OverlayWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
                ResizeMode = ResizeMode.NoResize;
                _root = new Border { Background = new SolidColorBrush(Color.FromArgb(40, 255, 170, 100)), IsHitTestVisible = false };
                Content = _root;
                Loaded += OverlayWindow_Loaded;
            }

            private void OverlayWindow_Loaded(object sender, RoutedEventArgs e) => MakeWindowClickThrough();

            public void SetOverlayColor(Color c) { if (_root != null) _root.Background = new SolidColorBrush(c); }

            private void MakeWindowClickThrough()
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                const int GWL_EXSTYLE = -20;
                const uint WS_EX_TRANSPARENT = 0x00000020;
                const uint WS_EX_LAYERED = 0x00080000;
                var ex = GetWindowLongPtrWrapper(hwnd, GWL_EXSTYLE);
                var newEx = new IntPtr(ex.ToInt64() | WS_EX_TRANSPARENT | WS_EX_LAYERED);
                SetWindowLongPtrWrapper(hwnd, GWL_EXSTYLE, newEx);
            }

            [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
            private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
            private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
            [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
            private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
            private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

            private static IntPtr GetWindowLongPtrWrapper(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
            private static IntPtr SetWindowLongPtrWrapper(IntPtr hWnd, int nIndex, IntPtr dwNewLong) => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }
    }
}