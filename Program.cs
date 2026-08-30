using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PCPI_Minimalist
{
    // Estructura para definir cada tema de color de la consola
    public class ColorTheme
    {
        public string Name { get; set; }
        public Color Background { get; set; }
        public Color Foreground { get; set; }
        public Color Accent { get; set; }
        public Color Border { get; set; }
        public Color ButtonBg { get; set; }
    }

    // Estructura para los programas
    public class SoftwareItem
    {
        public string Category { get; set; }
        public string Name { get; set; }
        public string Command { get; set; }
        public bool IsChecked { get; set; }

        // La CheckedListBox muestra esto como texto del elemento
        public override string ToString() => Name;
    }

    // ================================================================
    //  OVERLAY CRT  (ventana superpuesta, transparente al clic)
    //  - scanlines finas y suaves
    //  - viñeta / curvatura del tubo en bordes y esquinas
    //  - parpadeo / vibración analógica de frecuencia MUY baja
    // ================================================================
    public class CrtOverlay : Form
    {
        private readonly System.Windows.Forms.Timer timer;
        private readonly Random rng = new Random();
        private double phase;      // fase del parpadeo lento
        private int flick;         // frames de micro-parpadeo
        private int jitter;        // desplazamiento de 1 px (vibración de sincronía)

        public CrtOverlay(Form owner)
        {
            Owner = owner;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;      // seguimos al form pixel a pixel
            AllowTransparency = true;
            BackColor = Color.Black;
            TransparencyKey = Color.Black;           // negro puro = transparente / click-through
            DoubleBuffered = true;

            timer = new System.Windows.Forms.Timer { Interval = 120 };
            timer.Tick += (s, e) =>
            {
                phase += 0.12;                               // ciclo de parpadeo ~6 s
                if (flick > 0) flick--;
                else if (rng.Next(0, 45) == 0) flick = 2;    // micro-parpadeo esporádico
                jitter = (rng.Next(0, 60) == 0) ? 1 : 0;     // vibración de sincronía muy ocasional
                Invalidate();
            };
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x80;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void SyncTo(Rectangle r)
        {
            if (Bounds != r) SetBounds(r.X, r.Y, r.Width, r.Height, BoundsSpecified.All);
        }

        public void StartFx() { if (!Visible) Show(); timer.Start(); Invalidate(); }
        public void StopFx() { timer.Stop(); if (Visible) Hide(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width <= 2 || Height <= 2) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // ÚNICAMENTE scanlines horizontales muy sutiles. Sin viñeta, sin óvalo,
            // sin gradiente: cubre el 100% de la ventana sin recortar nada.
            // (El fondo es negro puro = transparente por TransparencyKey; las líneas
            //  son un gris casi negro -> equivalen a un blanco de alpha ~10 sobre negro.)
            int pulse = (int)Math.Round(1.2 * Math.Sin(phase));       // respiración lentísima
            int baseShade = 7 + pulse + (flick > 0 ? 2 : 0);         // ~5..10
            if (baseShade < 4) baseShade = 4;

            for (int y = jitter; y < Height; y += 4)
            {
                using (var pen = new Pen(Color.FromArgb(baseShade, baseShade, baseShade), 1f))
                    g.DrawLine(pen, 0, y, Width, y);
            }
        }
    }

    public class MainForm : Form
    {
        // Banner ASCII de la cabecera
        private const string BANNER =
@" ____   ____ ____ ___
|  _ \ / ___|  _ \_ _|
| |_) | |   | |_) | |
|  __/| |___|  __/| |
|_|    \____|_|  |___|";

        private const string TAGLINE = "v2.0  ENGINE";
        private const string CREDIT = "hecho con ❤️ by_Chuyo31";

        // Fichero de preferencias (recuerda el tema/color de énfasis)
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCPI", "pcpi.cfg");

        // Categoría especial (módulo de drivers, no es una lista de software)
        private const string DRIVERS_BACKUP = "Drivers Backup";

        // Controles de interfaz
        private Panel headerPanel;
        private Label asciiLabel;
        private Label taglineLabel;
        private Label titleLabel;
        private Label creditLabel;
        private Label badgeLabel;
        private Label subtitleLabel;
        private Panel chipEstado;
        private Label lblEstadoCaption, lblEstadoValue;
        private Panel chipSeleccionados;
        private Label lblSelCaption, lblSelValue;
        private Panel panelCatBar;
        private CheckedListBox catListBox;
        private Panel panelDrivers;
        private Label lblDriversInfo;
        private Button btnDriversBackup, btnDriversRestore;
        private readonly List<Button> catButtons = new List<Button>();
        private readonly List<Button> driverButtons = new List<Button>();
        private string currentCategory;
        private readonly Font tabFont = new Font("Consolas", 8F, FontStyle.Bold);
        private RichTextBox logTextBox;
        private Button btnInstallAll;
        private Button btnSelectAll;
        private Button btnDeselectAll;
        private Button btnClearLog;
        private Button btnCrtToggle;
        private Panel themePanel;

        // Colores de estado (fijos, independientes del tema)
        private readonly Color statusIdleColor = Color.FromArgb(51, 255, 119);
        private readonly Color statusBusyColor = Color.FromArgb(255, 176, 0);
        private readonly Font chipFont = new Font("Consolas", 8.5F, FontStyle.Bold);

        // Lista de temas disponibles
        private List<ColorTheme> themes;
        private ColorTheme currentTheme;
        private bool crtEnabled = true;
        private CrtOverlay crtOverlay;

        // Lista de programas
        private List<SoftwareItem> softwareList;

        public MainForm()
        {
            InitializeThemes();
            InitializeSoftwareList();
            InitializeComponentLayout();

            // Recupera preferencias de la última sesión (color de énfasis + CRT)
            string savedName = LoadSavedThemeName();
            ColorTheme start = themes.FirstOrDefault(t => t.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase)) ?? themes[0];
            ApplyTheme(start);

            crtEnabled = LoadSavedCrt();
            btnCrtToggle.Text = crtEnabled ? "[ CRT: ON ]" : "[ CRT: OFF ]";
        }

        private bool LoadSavedCrt()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    foreach (var line in File.ReadAllLines(ConfigPath))
                        if (line.StartsWith("crt=", StringComparison.OrdinalIgnoreCase))
                            return !line.Substring(4).Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            return true;   // por defecto: activado
        }

        private string LoadSavedThemeName()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    foreach (var line in File.ReadAllLines(ConfigPath))
                        if (line.StartsWith("theme=", StringComparison.OrdinalIgnoreCase))
                            return line.Substring(6).Trim();
            }
            catch { }
            return null;
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                File.WriteAllText(ConfigPath,
                    "theme=" + currentTheme.Name + Environment.NewLine +
                    "crt=" + (crtEnabled ? "on" : "off"));
            }
            catch { }
        }

        private void InitializeThemes()
        {
            themes = new List<ColorTheme>
            {
                new ColorTheme { Name = "RED",    Background = Color.FromArgb(12, 12, 12), Foreground = Color.FromArgb(240, 240, 240), Accent = Color.FromArgb(255, 51, 51),  Border = Color.FromArgb(150, 0, 0),   ButtonBg = Color.FromArgb(30, 10, 10) },
                new ColorTheme { Name = "GREEN",  Background = Color.FromArgb(10, 15, 10), Foreground = Color.FromArgb(240, 240, 240), Accent = Color.FromArgb(51, 255, 119), Border = Color.FromArgb(0, 150, 50),   ButtonBg = Color.FromArgb(10, 30, 15) },
                new ColorTheme { Name = "AMBER",  Background = Color.FromArgb(15, 12, 8),  Foreground = Color.FromArgb(240, 240, 240), Accent = Color.FromArgb(255, 176, 0), Border = Color.FromArgb(180, 100, 0), ButtonBg = Color.FromArgb(35, 20, 5) },
                new ColorTheme { Name = "CYAN",   Background = Color.FromArgb(8, 14, 18),  Foreground = Color.FromArgb(240, 240, 240), Accent = Color.FromArgb(0, 229, 255),  Border = Color.FromArgb(0, 140, 180), ButtonBg = Color.FromArgb(5, 25, 35) },
                new ColorTheme { Name = "WHITE",  Background = Color.FromArgb(15, 15, 15), Foreground = Color.FromArgb(240, 240, 240), Accent = Color.FromArgb(220, 220, 220),Border = Color.FromArgb(100, 100, 100),ButtonBg = Color.FromArgb(35, 35, 35) }
            };

            currentTheme = themes[0]; // disponible antes del primer ApplyTheme()
        }

        // Orden y nombre de las pestañas
        private static readonly string[] CategoryOrder =
        {
            "Antivirus", "Navegadores", "Descompresores", "Ofimática", "IA",
            "Multimedia", "Editar Fotos", "Editar Videos", "Drivers", DRIVERS_BACKUP,
            "Streaming", "Gaming", "Música", "Chat"
        };

        private void InitializeSoftwareList()
        {
            softwareList = new List<SoftwareItem>();

            void Add(string cat, string name, string id) => softwareList.Add(new SoftwareItem
            {
                Category = cat,
                Name = name,
                Command = "winget install --id " + id + " -e --accept-source-agreements --accept-package-agreements"
            });

            // ---- Antivirus ----
            Add("Antivirus", "Malwarebytes", "Malwarebytes.Malwarebytes");
            Add("Antivirus", "Avast Free Antivirus", "Avast.AvastFreeAntivirus");
            Add("Antivirus", "AVG AntiVirus Free", "AVG.AntiVirus");
            Add("Antivirus", "Bitdefender Antivirus Free", "Bitdefender.Bitdefender");
            Add("Antivirus", "Kaspersky", "Kaspersky.Kaspersky");
            Add("Antivirus", "AdwCleaner", "Malwarebytes.AdwCleaner");
            Add("Antivirus", "ClamWin", "ClamWin.ClamWin");
            Add("Antivirus", "Spybot Search & Destroy", "Safer-Networking.Spybot.3");
            Add("Antivirus", "SUPERAntiSpyware", "SUPERAntiSpyware.SUPERAntiSpyware");

            // ---- Navegadores ----
            Add("Navegadores", "Google Chrome", "Google.Chrome");
            Add("Navegadores", "Mozilla Firefox", "Mozilla.Firefox");
            Add("Navegadores", "Brave", "Brave.Brave");
            Add("Navegadores", "Microsoft Edge", "Microsoft.Edge");
            Add("Navegadores", "Opera", "Opera.Opera");
            Add("Navegadores", "Opera GX", "Opera.OperaGX");
            Add("Navegadores", "Vivaldi", "Vivaldi.Vivaldi");
            Add("Navegadores", "Tor Browser", "TorProject.TorBrowser");
            Add("Navegadores", "LibreWolf", "LibreWolf.LibreWolf");
            Add("Navegadores", "Ungoogled Chromium", "eloston.ungoogled-chromium");

            // ---- Descompresores ----
            Add("Descompresores", "7-Zip", "7zip.7zip");
            Add("Descompresores", "WinRAR", "RARLab.WinRAR");
            Add("Descompresores", "NanaZip", "M2Team.NanaZip");
            Add("Descompresores", "PeaZip", "Giorgiotani.Peazip");
            Add("Descompresores", "Bandizip", "Bandisoft.Bandizip");
            Add("Descompresores", "WinZip", "Corel.WinZip");
            Add("Descompresores", "ExtractNow", "Nathan Moinvaziri.ExtractNow");

            // ---- Ofimática ----
            Add("Ofimática", "LibreOffice", "TheDocumentFoundation.LibreOffice");
            Add("Ofimática", "ONLYOFFICE Desktop Editors", "ONLYOFFICE.DesktopEditors");
            Add("Ofimática", "Apache OpenOffice", "Apache.OpenOffice");
            Add("Ofimática", "WPS Office", "Kingsoft.WPSOffice");
            Add("Ofimática", "Microsoft 365", "Microsoft.Office");
            Add("Ofimática", "Adobe Acrobat Reader", "Adobe.Acrobat.Reader.64-bit");
            Add("Ofimática", "Foxit PDF Reader", "Foxit.FoxitReader");
            Add("Ofimática", "SumatraPDF", "SumatraPDF.SumatraPDF");
            Add("Ofimática", "PDF24 Creator", "PDF24.PDF24");
            Add("Ofimática", "Notion", "Notion.Notion");
            Add("Ofimática", "Obsidian", "Obsidian.Obsidian");

            // ---- IA ----
            Add("IA", "ChatGPT (Desktop)", "OpenAI.ChatGPT");
            Add("IA", "Claude (Desktop)", "Anthropic.Claude");
            Add("IA", "Ollama", "Ollama.Ollama");
            Add("IA", "LM Studio", "ElementLabs.LMStudio");
            Add("IA", "GPT4All", "Nomic.GPT4All");
            Add("IA", "Jan", "Jan.Jan");
            Add("IA", "Msty", "Msty.Msty");
            Add("IA", "AnythingLLM", "Mintplex-Labs.AnythingLLM");
            Add("IA", "Cursor", "Anysphere.Cursor");
            Add("IA", "Perplexity", "Perplexity.Perplexity");

            // ---- Multimedia ----
            Add("Multimedia", "VLC Media Player", "VideoLAN.VLC");
            Add("Multimedia", "MPC-HC", "clsid2.mpc-hc");
            Add("Multimedia", "K-Lite Codec Pack", "CodecGuide.K-LiteCodecPack.Standard");
            Add("Multimedia", "OBS Studio", "OBSProject.OBSStudio");
            Add("Multimedia", "Audacity", "Audacity.Audacity");
            Add("Multimedia", "HandBrake", "HandBrake.HandBrake");
            Add("Multimedia", "GIMP", "GIMP.GIMP");
            Add("Multimedia", "Krita", "KDE.Krita");
            Add("Multimedia", "Inkscape", "Inkscape.Inkscape");
            Add("Multimedia", "paint.net", "dotPDN.paintdotnet");
            Add("Multimedia", "IrfanView", "IrfanSkiljan.IrfanView");
            Add("Multimedia", "Blender", "BlenderFoundation.Blender");
            Add("Multimedia", "Kdenlive", "KDE.Kdenlive");
            Add("Multimedia", "Shotcut", "Meltytech.Shotcut");

            // ---- Editar Fotos ----
            Add("Editar Fotos", "GIMP", "GIMP.GIMP");
            Add("Editar Fotos", "Krita", "KDE.Krita");
            Add("Editar Fotos", "paint.net", "dotPDN.paintdotnet");
            Add("Editar Fotos", "darktable", "darktable.darktable");
            Add("Editar Fotos", "RawTherapee", "RawTherapee.RawTherapee");
            Add("Editar Fotos", "XnView MP", "XnSoft.XnViewMP");
            Add("Editar Fotos", "Inkscape", "Inkscape.Inkscape");
            Add("Editar Fotos", "PhotoScape X", "PhotoScape.PhotoScapeX");
            Add("Editar Fotos", "Adobe Creative Cloud", "Adobe.CreativeCloud");

            // ---- Editar Videos ----
            Add("Editar Videos", "DaVinci Resolve", "BlackmagicDesign.DaVinciResolve");
            Add("Editar Videos", "Shotcut", "Meltytech.Shotcut");
            Add("Editar Videos", "Kdenlive", "KDE.Kdenlive");
            Add("Editar Videos", "OpenShot", "OpenShot.OpenShot");
            Add("Editar Videos", "Avidemux", "Avidemux.Avidemux");
            Add("Editar Videos", "LosslessCut", "mifi.lossless-cut");
            Add("Editar Videos", "HandBrake", "HandBrake.HandBrake");
            Add("Editar Videos", "CapCut", "Bytedance.CapCut");
            Add("Editar Videos", "OBS Studio", "OBSProject.OBSStudio");

            // ---- Drivers (aplicaciones) ----
            Add("Drivers", "Intel Driver & Support Assistant", "Intel.IntelDriverAndSupportAssistant");
            Add("Drivers", "NVIDIA App", "Nvidia.NvidiaApp");
            Add("Drivers", "NVIDIA GeForce Experience", "Nvidia.GeForceExperience");
            Add("Drivers", "Display Driver Uninstaller (DDU)", "Wagnardsoft.DisplayDriverUninstaller");
            Add("Drivers", "Snappy Driver Installer Origin", "GlennDelahoy.SnappyDriverInstallerOrigin");
            Add("Drivers", "CPU-Z", "CPUID.CPU-Z");
            Add("Drivers", "GPU-Z", "TechPowerUp.GPU-Z");
            Add("Drivers", "HWMonitor", "CPUID.HWMonitor");
            Add("Drivers", "HWiNFO", "REALiX.HWiNFO");
            Add("Drivers", "CrystalDiskInfo", "CrystalDewWorld.CrystalDiskInfo");
            Add("Drivers", "MSI Afterburner", "Guru3D.Afterburner");

            // ---- Streaming ----
            Add("Streaming", "Netflix", "Netflix.Netflix");
            Add("Streaming", "Prime Video", "Amazon.PrimeVideo");
            Add("Streaming", "Disney+", "Disney.DisneyPlus");
            Add("Streaming", "Twitch", "Twitch.Twitch");
            Add("Streaming", "Plex", "Plex.Plex");
            Add("Streaming", "Jellyfin Media Player", "Jellyfin.JellyfinMediaPlayer");
            Add("Streaming", "Kodi", "XBMCFoundation.Kodi");
            Add("Streaming", "Stremio", "Stremio.Stremio");
            Add("Streaming", "Streamlabs", "Streamlabs.Streamlabs");

            // ---- Gaming ----
            Add("Gaming", "Steam", "Valve.Steam");
            Add("Gaming", "Epic Games Launcher", "EpicGames.EpicGamesLauncher");
            Add("Gaming", "EA app", "ElectronicArts.EADesktop");
            Add("Gaming", "Ubisoft Connect", "Ubisoft.Connect");
            Add("Gaming", "GOG Galaxy", "GOG.Galaxy");
            Add("Gaming", "Battle.net", "Blizzard.BattleNet");
            Add("Gaming", "Rockstar Games Launcher", "RockstarGames.RockstarGamesLauncher");
            Add("Gaming", "itch.io", "ItchIo.Itch");
            Add("Gaming", "Playnite", "Playnite.Playnite");
            Add("Gaming", "Heroic Games Launcher", "HeroicGamesLauncher.HeroicGamesLauncher");
            Add("Gaming", "Parsec", "Parsec.Parsec");

            // ---- Música ----
            Add("Música", "Spotify", "Spotify.Spotify");
            Add("Música", "iTunes", "Apple.iTunes");
            Add("Música", "TIDAL", "TIDAL.TIDAL");
            Add("Música", "Deezer", "Deezer.Deezer");
            Add("Música", "YouTube Music", "th-ch.YouTubeMusic");
            Add("Música", "AIMP", "AIMP.AIMP");
            Add("Música", "foobar2000", "PeterPawlowski.foobar2000");
            Add("Música", "MusicBee", "MusicBee.MusicBee");
            Add("Música", "Winamp", "Winamp.Winamp");
            Add("Música", "MusicBrainz Picard", "MetaBrainz.Picard");

            // ---- Chat ----
            Add("Chat", "WhatsApp", "WhatsApp.WhatsApp");
            Add("Chat", "Telegram Desktop", "Telegram.TelegramDesktop");
            Add("Chat", "Discord", "Discord.Discord");
            Add("Chat", "Signal", "OpenWhisperSystems.Signal");
            Add("Chat", "Slack", "SlackTechnologies.Slack");
            Add("Chat", "Microsoft Teams", "Microsoft.Teams");
            Add("Chat", "Zoom", "Zoom.Zoom");
            Add("Chat", "Skype", "Microsoft.Skype");
            Add("Chat", "Element", "Element.Element");
            Add("Chat", "Mozilla Thunderbird", "Mozilla.Thunderbird");
            Add("Chat", "Viber", "Viber.Viber");
        }

        private void InitializeComponentLayout()
        {
            // Ventana Principal
            this.AutoScaleMode = AutoScaleMode.None;   // layout a coordenadas fijas
            this.Text = "PCPI v2.0 - Standalone System Installer";
            this.Size = new Size(904, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Icon = SystemIcons.Application;

            // ================= CABECERA =================
            headerPanel = new Panel { Location = new Point(14, 10), Size = new Size(872, 152) };

            asciiLabel = new Label
            {
                Text = BANNER,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Location = new Point(0, 0),
                AutoSize = true
            };

            // "v2.0 ENGINE" pequeño, junto al final del banner
            taglineLabel = new Label
            {
                Text = TAGLINE,
                Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                Location = new Point(168, 56),
                AutoSize = true
            };

            // "Retro Minimals" alargado para que cuadre con el ancho del banner PCPI
            titleLabel = new Label
            {
                Text = "Retro Minimals",
                Font = new Font("Consolas", 13F, FontStyle.Bold),
                Location = new Point(3, 72),
                AutoSize = true
            };

            // Ajusta el tamaño de "Retro Minimals" al ancho exacto del banner "PCPI"
            int bannerW = asciiLabel.PreferredSize.Width;
            for (float fs = 9F; fs <= 26F; fs += 0.5F)
            {
                var f = new Font("Consolas", fs, FontStyle.Bold);
                if (TextRenderer.MeasureText(titleLabel.Text, f).Width <= bannerW)
                {
                    titleLabel.Font.Dispose();
                    titleLabel.Font = f;
                }
                else { f.Dispose(); break; }
            }

            // Crédito debajo de "Retro Minimals", un poco más grande
            creditLabel = new Label
            {
                Text = CREDIT,
                Font = new Font("Segoe UI Emoji", 10F, FontStyle.Bold),
                Location = new Point(5, titleLabel.Location.Y + titleLabel.PreferredSize.Height + 2),
                AutoSize = true
            };

            badgeLabel = new Label
            {
                Text = "STANDALONE" + Environment.NewLine + "LIGHTWEIGHT",
                Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.None,
                AutoSize = false,
                Size = new Size(150, 42),
                Location = new Point(300, 80)
            };
            badgeLabel.Paint += (s, e) =>
            {
                var r = badgeLabel.ClientRectangle;
                r.Width -= 1; r.Height -= 1;
                using (var pen = new Pen(currentTheme.Accent))
                    e.Graphics.DrawRectangle(pen, r);
            };

            subtitleLabel = new Label
            {
                Text = "Plataforma de instalación de paquetes sin dependencias pesadas. Modo nativo de bajo consumo.",
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                Location = new Point(2, 124),
                AutoSize = false,
                Size = new Size(470, 34)
            };

            // Chips de estado (derecha, arriba)
            chipEstado = MakeChip(new Point(470, 4), 188, "ESTADO: ", out lblEstadoCaption, out lblEstadoValue, "EN ESPERA");
            chipSeleccionados = MakeChip(new Point(672, 4), 200, "SELECCIONADOS: ", out lblSelCaption, out lblSelValue, "0 / " + softwareList.Count);

            // Botón CRT (derecha, en medio)
            btnCrtToggle = new Button
            {
                Text = "[ CRT: ON ]",
                Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                Size = new Size(120, 30),
                Location = new Point(470, 42),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCrtToggle.Click += (s, e) => ToggleCrt();

            // Botones de tema (derecha, abajo)
            themePanel = new Panel { Location = new Point(470, 82), Size = new Size(400, 32) };
            int btnX = 0;
            foreach (var t in themes)
            {
                Button btnTheme = new Button
                {
                    Text = t.Name,
                    Font = new Font("Consolas", 8F, FontStyle.Bold),
                    Size = new Size(54, 26),
                    Location = new Point(btnX, 3),
                    FlatStyle = FlatStyle.Flat,
                    Tag = t,
                    Cursor = Cursors.Hand
                };
                btnTheme.Click += (s, e) => ApplyTheme((ColorTheme)((Button)s).Tag);
                themePanel.Controls.Add(btnTheme);
                btnX += 58;
            }

            headerPanel.Controls.Add(asciiLabel);
            headerPanel.Controls.Add(taglineLabel);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(creditLabel);
            headerPanel.Controls.Add(badgeLabel);
            headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(chipEstado);
            headerPanel.Controls.Add(chipSeleccionados);
            headerPanel.Controls.Add(btnCrtToggle);
            headerPanel.Controls.Add(themePanel);

            // ============ BARRA DE CATEGORÍAS (pestañas propias) ============
            panelCatBar = new Panel { Location = new Point(15, 172), Size = new Size(872, 50) };
            int cols = 7, cbW = 122, cbH = 22, gap = 2;
            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                string cat = CategoryOrder[i];
                var cb = new Button
                {
                    Text = cat.ToUpperInvariant(),
                    Tag = cat,
                    Font = tabFont,
                    Size = new Size(cbW, cbH),
                    Location = new Point((i % cols) * (cbW + gap), (i / cols) * (cbH + gap)),
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                cb.FlatAppearance.BorderSize = 1;
                cb.Click += (s, e) => SelectCategory((string)((Button)s).Tag);
                catButtons.Add(cb);
                panelCatBar.Controls.Add(cb);
            }

            // Lista de software (categorías normales)
            catListBox = new CheckedListBox
            {
                Location = new Point(15, 226),
                Size = new Size(872, 208),
                Font = new Font("Consolas", 10, FontStyle.Bold),
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true,
                IntegralHeight = false
            };
            catListBox.ItemCheck += (s, e) =>
            {
                if (catListBox.Items[e.Index] is SoftwareItem it)
                    it.IsChecked = e.NewValue == CheckState.Checked;
                BeginInvoke((Action)UpdateSelectedCount);
            };

            // Módulo COPIAR / RESTAURAR DRIVERS (mismo hueco, oculto por defecto)
            panelDrivers = new Panel
            {
                Location = new Point(15, 226),
                Size = new Size(872, 208),
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            lblDriversInfo = new Label
            {
                Location = new Point(14, 12),
                Size = new Size(842, 92),
                Font = new Font("Consolas", 9F, FontStyle.Regular),
                Text =
                    "MODULO COPIAR / RESTAURAR DRIVERS  —  gestion de controladores para formateo" + Environment.NewLine + Environment.NewLine +
                    "1) COPIAR : exporta TODOS los controladores de este equipo a la carpeta" + Environment.NewLine +
                    "            'PCPI_Drivers_Backup' del Escritorio (DISM). Llevala en un pendrive USB." + Environment.NewLine +
                    "2) RESTAURAR : elige la carpeta de backup (Escritorio o USB) e instala en masa" + Environment.NewLine +
                    "            todos los .inf con PNPUTIL.   >> Ejecuta PCPI como administrador <<"
            };
            btnDriversBackup = new Button
            {
                Text = "[ COPIAR / EXPORTAR DRIVERS DE ESTE EQUIPO ]",
                Location = new Point(14, 118),
                Size = new Size(600, 36),
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnDriversBackup.FlatAppearance.BorderSize = 1;
            btnDriversBackup.Click += async (s, e) => await BackupDriversAsync();

            btnDriversRestore = new Button
            {
                Text = "[ RESTAURAR DRIVERS DESDE CARPETA ]",
                Location = new Point(14, 160),
                Size = new Size(600, 36),
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnDriversRestore.FlatAppearance.BorderSize = 1;
            btnDriversRestore.Click += async (s, e) => await RestoreDriversAsync();

            driverButtons.Add(btnDriversBackup);
            driverButtons.Add(btnDriversRestore);
            panelDrivers.Controls.Add(lblDriversInfo);
            panelDrivers.Controls.Add(btnDriversBackup);
            panelDrivers.Controls.Add(btnDriversRestore);

            // ================= ACCIONES =================
            btnSelectAll = CreateButton("[ X ] MARCAR TODOS", new Point(15, 444), new Size(190, 35), (s, e) => SetAllChecks(true));
            btnDeselectAll = CreateButton("[   ] DESMARCAR TODOS", new Point(215, 444), new Size(205, 35), (s, e) => SetAllChecks(false));
            btnClearLog = CreateButton("[ LIMPIAR ]", new Point(430, 444), new Size(120, 35), (s, e) => { logTextBox.Clear(); AppendLog("CONSOLA LIMPIADA."); });
            btnInstallAll = CreateButton(">>> INSTALAR SELECCIONADOS <<<", new Point(562, 444), new Size(325, 35), async (s, e) => await StartInstallationProcess());

            // ================= LOG =================
            logTextBox = new RichTextBox
            {
                Location = new Point(15, 489),
                Size = new Size(872, 182),
                Font = new Font("Consolas", 9, FontStyle.Regular),
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            this.Controls.Add(headerPanel);
            this.Controls.Add(panelCatBar);
            this.Controls.Add(catListBox);
            this.Controls.Add(panelDrivers);
            this.Controls.Add(btnSelectAll);
            this.Controls.Add(btnDeselectAll);
            this.Controls.Add(btnClearLog);
            this.Controls.Add(btnInstallAll);
            this.Controls.Add(logTextBox);

            SelectCategory(CategoryOrder[0]);
            AppendLog("SISTEMA LISTO. SELECCIONA LOS PROGRAMAS Y PRESIONA INSTALAR.");
            UpdateSelectedCount();
        }

        private void SelectCategory(string cat)
        {
            currentCategory = cat;
            bool special = cat == DRIVERS_BACKUP;

            catListBox.Visible = !special;
            panelDrivers.Visible = special;

            if (!special)
            {
                catListBox.BeginUpdate();
                catListBox.Items.Clear();
                foreach (var it in softwareList.Where(i => i.Category == cat))
                    catListBox.Items.Add(it, it.IsChecked);
                catListBox.EndUpdate();
            }

            foreach (var b in catButtons)
            {
                bool on = (string)b.Tag == cat;
                b.BackColor = on ? currentTheme.Accent : currentTheme.ButtonBg;
                b.ForeColor = on ? currentTheme.Background : currentTheme.Accent;
                b.FlatAppearance.BorderColor = currentTheme.Border;
            }
        }

        private Panel MakeChip(Point loc, int width, string caption, out Label capLbl, out Label valLbl, string value)
        {
            var chip = new Panel
            {
                Location = loc,
                Size = new Size(width, 28),
                BorderStyle = BorderStyle.None
            };
            var cap = new Label { Text = caption, Font = chipFont, Location = new Point(7, 6), AutoSize = true };
            int capW = TextRenderer.MeasureText(caption, chipFont).Width;
            var val = new Label { Text = value, Font = chipFont, Location = new Point(3 + capW, 6), AutoSize = true };
            chip.Controls.Add(cap);
            chip.Controls.Add(val);
            chip.Paint += (s, e) =>
            {
                var r = chip.ClientRectangle;
                r.Width -= 1; r.Height -= 1;
                using (var pen = new Pen(currentTheme.Border))
                    e.Graphics.DrawRectangle(pen, r);
            };
            capLbl = cap;
            valLbl = val;
            return chip;
        }

        private void UpdateSelectedCount()
        {
            if (lblSelValue == null) return;
            lblSelValue.Text = softwareList.Count(i => i.IsChecked) + " / " + softwareList.Count;
        }

        private void SetEngineStatus(string text, bool busy)
        {
            if (lblEstadoValue == null) return;
            lblEstadoValue.Text = text;
            lblEstadoValue.ForeColor = busy ? statusBusyColor : statusIdleColor;
        }

        private void SetBusy(bool busy, string status)
        {
            btnInstallAll.Enabled = !busy;
            btnSelectAll.Enabled = !busy;
            btnDeselectAll.Enabled = !busy;
            btnDriversBackup.Enabled = !busy;
            btnDriversRestore.Enabled = !busy;
            SetEngineStatus(status, busy);
        }

        private Button CreateButton(string text, Point loc, Size size, EventHandler onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Size = size,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private void ApplyTheme(ColorTheme theme)
        {
            currentTheme = theme;
            this.BackColor = theme.Background;
            headerPanel.BackColor = theme.Background;

            asciiLabel.ForeColor = theme.Accent;
            taglineLabel.ForeColor = theme.Accent;
            titleLabel.ForeColor = theme.Accent;
            creditLabel.ForeColor = theme.Accent;
            subtitleLabel.ForeColor = Color.FromArgb(165, 165, 165);
            badgeLabel.ForeColor = theme.Accent;
            badgeLabel.Invalidate();

            StyleChip(chipEstado, lblEstadoCaption, lblEstadoValue, theme);
            StyleChip(chipSeleccionados, lblSelCaption, lblSelValue, theme);
            lblSelValue.ForeColor = theme.Accent;

            panelCatBar.BackColor = theme.Background;
            catListBox.BackColor = theme.Background;
            catListBox.ForeColor = theme.Foreground;
            foreach (var b in catButtons) StyleButton(b, theme);

            // Módulo drivers
            panelDrivers.BackColor = theme.Background;
            lblDriversInfo.BackColor = theme.Background;
            lblDriversInfo.ForeColor = Color.FromArgb(200, 200, 200);
            foreach (var b in driverButtons) StyleButton(b, theme);

            if (currentCategory != null) SelectCategory(currentCategory); // reaplica el resaltado activo

            logTextBox.BackColor = Color.FromArgb(5, 5, 5);
            logTextBox.ForeColor = theme.Foreground;

            StyleButton(btnSelectAll, theme);
            StyleButton(btnDeselectAll, theme);
            StyleButton(btnClearLog, theme);
            StyleButton(btnInstallAll, theme, true);
            StyleButton(btnCrtToggle, theme);

            foreach (Control ctrl in themePanel.Controls)
                if (ctrl is Button b) StyleButton(b, theme);

            SaveConfig();   // recuerda el color de énfasis para la próxima sesión
            AppendLog($"CAMBIO DE TEMA APLICADO: [{theme.Name}]");
        }

        private void StyleChip(Panel chip, Label caption, Label value, ColorTheme theme)
        {
            chip.BackColor = theme.ButtonBg;
            caption.BackColor = theme.ButtonBg;
            value.BackColor = theme.ButtonBg;
            caption.ForeColor = theme.Foreground;
            if (value == lblEstadoValue)
                value.ForeColor = statusIdleColor;
            chip.Invalidate();
        }

        private void StyleButton(Button btn, ColorTheme theme, bool isHighlight = false)
        {
            btn.BackColor = isHighlight ? theme.Accent : theme.ButtonBg;
            btn.ForeColor = isHighlight ? theme.Background : theme.Accent;
            btn.FlatAppearance.BorderColor = theme.Border;
            btn.FlatAppearance.BorderSize = 1;
        }

        // ================= OVERLAY CRT =================
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                crtOverlay = new CrtOverlay(this);
                crtOverlay.Show(this);
                crtOverlay.Enabled = false;   // Show(owner) rechaza formularios deshabilitados
            }
            catch
            {
                crtOverlay = null;
                AppendLog("OVERLAY CRT NO DISPONIBLE EN ESTE EQUIPO.", true);
            }

            this.Move += (s, ev) => SyncCrtOverlay();
            this.Resize += (s, ev) => SyncCrtOverlay();
            this.LocationChanged += (s, ev) => SyncCrtOverlay();
            this.SizeChanged += (s, ev) => SyncCrtOverlay();
            this.Activated += (s, ev) => SyncCrtOverlay();
            SyncCrtOverlay();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            crtOverlay?.Close();
            base.OnFormClosing(e);
        }

        private void SyncCrtOverlay()
        {
            if (crtOverlay == null || crtOverlay.IsDisposed) return;
            if (!crtEnabled || WindowState == FormWindowState.Minimized || !Visible)
            {
                crtOverlay.StopFx();
                return;
            }
            crtOverlay.SyncTo(RectangleToScreen(ClientRectangle));
            crtOverlay.StartFx();
            crtOverlay.BringToFront();
        }

        private void ToggleCrt()
        {
            crtEnabled = !crtEnabled;
            btnCrtToggle.Text = crtEnabled ? "[ CRT: ON ]" : "[ CRT: OFF ]";
            SyncCrtOverlay();
            SaveConfig();   // recuerda el estado del CRT para la próxima sesión
            AppendLog($"EFECTO RETRO CRT: {(crtEnabled ? "ACTIVADO" : "DESACTIVADO")}");
        }

        private void SetAllChecks(bool state)
        {
            foreach (var it in softwareList) it.IsChecked = state;
            for (int i = 0; i < catListBox.Items.Count; i++)
                catListBox.SetItemChecked(i, state);
            UpdateSelectedCount();
        }

        // ================= LOG =================
        private void AppendLog(string text, bool isError = false)
        {
            string timeStamp = DateTime.Now.ToString("HH:mm:ss");
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionLength = 0;

            logTextBox.SelectionColor = currentTheme.Border;
            logTextBox.AppendText($"[{timeStamp}] ");

            logTextBox.SelectionColor = isError ? Color.Red : currentTheme.Accent;
            logTextBox.AppendText(text + "\n");

            logTextBox.ScrollToCaret();
        }

        private void LogUI(string text, bool isError = false)
        {
            if (logTextBox.InvokeRequired) logTextBox.BeginInvoke((Action)(() => AppendLog(text, isError)));
            else AppendLog(text, isError);
        }

        // ================= INSTALACIÓN DE SOFTWARE =================
        private async Task StartInstallationProcess()
        {
            var seleccionados = softwareList.Where(i => i.IsChecked).ToList();
            if (seleccionados.Count == 0)
            {
                MessageBox.Show("No has seleccionado ningún programa de la lista.", "PCPI - Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetBusy(true, "INSTALANDO...");
            AppendLog("==========================================================");
            AppendLog($"INICIANDO INSTALACIÓN DE {seleccionados.Count} PROGRAMAS...");
            AppendLog("==========================================================");

            int okCount = 0, failCount = 0, n = 0;
            foreach (var item in seleccionados)
            {
                n++;
                AppendLog($"[{n}/{seleccionados.Count}] {item.Name}");
                bool success = await RunWithProgressAsync(item);
                if (success) okCount++; else failCount++;
            }

            AppendLog("==========================================================");
            AppendLog($"PROCESO FINALIZADO. OK: {okCount}  ·  FALLOS: {failCount}", failCount > 0);
            AppendLog("==========================================================");
            SetBusy(false, "EN ESPERA");
        }

        // ---- Barra de progreso + porcentaje por aplicación (en color de acento) ----
        private int progressLineStart = -1;

        private static string BuildBar(int pct)
        {
            const int W = 24;
            if (pct < 0) pct = 0; if (pct > 100) pct = 100;
            int fill = pct * W / 100;
            return "[" + new string('#', fill) + new string('-', W - fill) + "] " + pct.ToString().PadLeft(3) + "%";
        }

        private void ProgressBegin()
        {
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionColor = currentTheme.Border;
            logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}]   ");
            progressLineStart = logTextBox.TextLength;
        }

        private void ProgressUpdate(string text, Color color)
        {
            if (progressLineStart < 0) return;
            logTextBox.SelectionStart = progressLineStart;
            logTextBox.SelectionLength = logTextBox.TextLength - progressLineStart;
            logTextBox.SelectionColor = color;
            logTextBox.SelectedText = text;
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();
        }

        private void ProgressEnd()
        {
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionColor = logTextBox.ForeColor;
            logTextBox.AppendText("\n");
            progressLineStart = -1;
        }

        private async Task<bool> RunWithProgressAsync(SoftwareItem item)
        {
            ProgressBegin();
            int pct = 0;
            ProgressUpdate(BuildBar(pct) + "  " + item.Name, currentTheme.Accent);

            var tcs = new TaskCompletionSource<int>();
            Process p;
            try
            {
                p = new Process
                {
                    EnableRaisingEvents = true,
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c " + item.Command,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                p.Exited += (s, e) => { try { tcs.TrySetResult(p.ExitCode); } catch { tcs.TrySetResult(-1); } };
                p.OutputDataReceived += (s, e) => { };
                p.ErrorDataReceived += (s, e) => { };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                ProgressUpdate(BuildBar(pct) + "  " + item.Name + "  ·  ERROR: " + ex.Message, Color.Red);
                ProgressEnd();
                return false;
            }

            // winget no da % fiable al redirigir su salida -> se estima y se completa al terminar
            while (!tcs.Task.IsCompleted)
            {
                await Task.WhenAny(tcs.Task, Task.Delay(160));
                if (tcs.Task.IsCompleted) break;
                pct += Math.Max(1, (93 - pct) / 10);
                if (pct > 93) pct = 93;
                ProgressUpdate(BuildBar(pct) + "  " + item.Name, currentTheme.Accent);
            }

            int code = tcs.Task.Result;
            bool ok = code == 0;
            if (ok)
                ProgressUpdate(BuildBar(100) + "  " + item.Name + "  ·  OK", currentTheme.Accent);
            else
                ProgressUpdate(BuildBar(pct) + "  " + item.Name + "  ·  ERROR (" + code + ")", Color.Red);
            ProgressEnd();

            try { p.Dispose(); } catch { }
            return ok;
        }

        // ================= MÓDULO COPIAR / RESTAURAR DRIVERS =================
        private Task<int> RunNativeAsync(string command)
        {
            var tcs = new TaskCompletionSource<int>();
            Process p;
            try
            {
                p = new Process
                {
                    EnableRaisingEvents = true,
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c " + command,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
                return tcs.Task;
            }

            p.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogUI("   " + e.Data.TrimEnd()); };
            p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) LogUI("   " + e.Data.TrimEnd(), true); };
            p.Exited += (s, e) =>
            {
                int c;
                try { c = p.ExitCode; } catch { c = -1; }
                try { p.Dispose(); } catch { }
                tcs.TrySetResult(c);
            };

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            return tcs.Task;
        }

        private async Task BackupDriversAsync()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string dest = Path.Combine(desktop, "PCPI_Drivers_Backup");

            SetBusy(true, "COPIANDO DRIVERS...");
            AppendLog("==========================================================");
            AppendLog("COPIA / EXPORTACION DE CONTROLADORES (DISM)");
            try
            {
                Directory.CreateDirectory(dest);
                AppendLog("Carpeta destino: " + dest);
                AppendLog("Exportando... puede tardar varios minutos, no cierres la ventana.");

                string cmd = "dism /online /export-driver /destination:\"" + dest + "\"";
                int code = await RunNativeAsync(cmd);

                if (code == 0 || code == 3010)
                {
                    AppendLog("COPIA DE DRIVERS COMPLETADA CORRECTAMENTE.");
                    AppendLog(">> Copia la carpeta 'PCPI_Drivers_Backup' a un pendrive USB ANTES de formatear <<");
                    try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dest + "\"") { UseShellExecute = true }); } catch { }
                }
                else
                {
                    AppendLog("DISM termino con codigo " + code + ". Si es un error de permisos, ejecuta PCPI como administrador.", true);
                }
            }
            catch (Exception ex)
            {
                AppendLog("ERROR en la copia de drivers: " + ex.Message, true);
            }
            finally
            {
                AppendLog("==========================================================");
                SetBusy(false, "EN ESPERA");
            }
        }

        private async Task RestoreDriversAsync()
        {
            string src;
            using (var fbd = new FolderBrowserDialog
            {
                Description = "Selecciona la carpeta de backup de drivers (Escritorio o pendrive USB)",
                ShowNewFolderButton = false
            })
            {
                string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PCPI_Drivers_Backup");
                if (Directory.Exists(def)) fbd.SelectedPath = def;
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                src = fbd.SelectedPath;
            }

            SetBusy(true, "RESTAURANDO DRIVERS...");
            AppendLog("==========================================================");
            AppendLog("RESTAURACION DE CONTROLADORES (PNPUTIL)");
            AppendLog("Origen: " + src);
            try
            {
                string cmd = "pnputil /add-driver \"" + src + "\\*.inf\" /subdirs /install";
                int code = await RunNativeAsync(cmd);

                if (code == 0 || code == 3010 || code == 259)
                    AppendLog("RESTAURACION DE DRIVERS FINALIZADA. Reinicia el equipo para aplicar todos los cambios.");
                else
                    AppendLog("PNPUTIL termino con codigo " + code + ". Si es un error de permisos, ejecuta PCPI como administrador.", true);
            }
            catch (Exception ex)
            {
                AppendLog("ERROR en la restauracion de drivers: " + ex.Message, true);
            }
            finally
            {
                AppendLog("==========================================================");
                SetBusy(false, "EN ESPERA");
            }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
