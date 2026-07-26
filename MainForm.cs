using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WIA;

namespace KartAtolye
{
    public partial class MainForm : Form
    {
        private const string WIA_FORMAT_PNG = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";

        private Panel panelLeft, panelRight, panelCenter;
        private FlowLayoutPanel flowThumbnails;
        private PictureBox pbMain, pbLogo;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;

        private TextBox txtSelectedScanner;
        private Button btnRefreshScanners, btnScanDevice, btnScanFile, btnSaveAll, btnThemeToggle;
        private TextBox txtName, txtRegistryNo, txtSearchFilter;
        private ComboBox cmbEmployeeType;
        private TextBox txtCropWidth, txtCropHeight, txtRotationManual;
        private Button btnAutoCrop, btnStartCrop, btnApplyCrop, btnCancelCrop, btnEraser, btnRestore, btnUndo, btnRedo;
        private TrackBar tbRotation, tbZoom, tbEraserSize;
        private Label lblZoomValue, lblEraserSizeValue;

        private Color colorNavy = Color.FromArgb(25, 42, 86);
        private Color colorGreen = Color.FromArgb(39, 174, 96);

        public class DocState : IDisposable
        {
            public Bitmap Current;
            public Bitmap Master;
            public void Dispose()
            {
                Current?.Dispose();
                Master?.Dispose();
            }
        }

        // Photoshop Benzeri Mimari
        public class PhotoDoc : IDisposable
        {
            public Bitmap OriginalScan;
            public Bitmap WorkingMaster;
            public Bitmap CurrentImage;

            public Stack<DocState> UndoStack = new Stack<DocState>();
            public Stack<DocState> RedoStack = new Stack<DocState>();

            public string Name = "";
            public string RegistryNo = "";
            public string EmployeeType = "PERSONEL";

            public void SaveState()
            {
                UndoStack.Push(new DocState { Current = new Bitmap(CurrentImage), Master = new Bitmap(WorkingMaster) });
                foreach (var s in RedoStack) s.Dispose();
                RedoStack.Clear();
            }

            public void Dispose()
            {
                OriginalScan?.Dispose(); WorkingMaster?.Dispose(); CurrentImage?.Dispose();
                foreach (var s in UndoStack) s.Dispose();
                foreach (var s in RedoStack) s.Dispose();
                UndoStack.Clear(); RedoStack.Clear();
            }
        }

        private List<PhotoDoc> documents = new List<PhotoDoc>();
        private int currentDocIndex = -1;

        private float zoomFactor = 1.0f;
        private bool isDarkMode = false;
        private bool isShowingOriginal = false;

        private enum ToolMode { None, Crop, Eraser, Restore, Pan }
        private ToolMode currentMode = ToolMode.None;
        private ToolMode previousMode = ToolMode.None;

        private Rectangle selectionRect;
        private bool isSelecting = false, isDraggingSelection = false, isPanning = false;
        private Point selectionStart, dragOffset, panStartMouse, panStartScroll, currentMousePos;
        private enum DragZone { None, Inside, TopLeft, TopRight, BottomLeft, BottomRight }
        private DragZone activeDragZone = DragZone.None;
        private Point dragStartPoint;
        private Rectangle originalRect;
        private const int handleSize = 8;
        private bool isUpdatingTextBoxes = false;

        public MainForm()
        {
            InitializeComponent();
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;
            this.KeyUp += MainForm_KeyUp;

            this.FormClosing += MainForm_FormClosing;

            SetupUI();
            LoadIconsAndLogos();
            ApplyTheme();
            CreateCheckerboardBackground();

            UpdateStatus("Sistem başlatıldı. 'Tarayıcıları Bul' butonu ile cihaz seçin veya dosyadan yükleyin.");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (documents.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    "Uygulamayı kapatmak istediğinize emin misiniz? Kaydedilmemiş evraklar silinecek.",
                    "Çıkış Onayı",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
        }

        private void LoadIconsAndLogos()
        {
            try
            {
                string iconPath = Path.Combine(Application.StartupPath, "kart_atolye.ico");
                if (File.Exists(iconPath)) { this.Icon = new Icon(iconPath); this.ShowIcon = true; }
            }
            catch { }
        }

        private void CreateCheckerboardBackground()
        {
            Bitmap tile = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(tile))
            {
                g.Clear(Color.White);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(220, 220, 220)))
                {
                    g.FillRectangle(b, 0, 0, 16, 16);
                    g.FillRectangle(b, 16, 16, 16, 16);
                }
            }
            pbMain.BackgroundImage = tile;
            pbMain.BackgroundImageLayout = ImageLayout.Tile;
        }

        private void SetupUI()
        {
            this.Text = "Kart Atölye";
            this.Size = new Size(1350, 980);
            this.MinimumSize = new Size(1250, 880);
            this.StartPosition = FormStartPosition.CenterScreen;

            statusStrip = new StatusStrip();
            lblStatus = new ToolStripStatusLabel { Text = "Sistem Hazır", Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            statusStrip.Items.Add(lblStatus);
            this.Controls.Add(statusStrip);

            // SOL PANEL
            panelLeft = new Panel { Dock = DockStyle.Left, Width = 290, Padding = new Padding(10) };
            this.Controls.Add(panelLeft);

            pbLogo = new PictureBox { Top = 10, Left = 10, Width = 270, Height = 80, SizeMode = PictureBoxSizeMode.Zoom };
            panelLeft.Controls.Add(pbLogo);

            GroupBox gbDevice = new GroupBox { Text = "Evrak Kaynağı & Donanım", Top = 100, Left = 10, Width = 270, Height = 175, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            btnRefreshScanners = CreateButton("🔍 Tarayıcıları Bul / Seç", 25, 10, 250, 30, colorNavy, Color.White);
            btnRefreshScanners.Click += BtnRefreshScanners_Click;
            txtSelectedScanner = new TextBox { Top = 60, Left = 10, Width = 250, ReadOnly = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Text = "Tarayıcı seçilmedi" };
            btnScanDevice = CreateButton("🖨️ Tarayıcıdan Aktar", 90, 10, 250, 35, colorNavy, Color.White);
            btnScanDevice.Click += BtnScanDevice_Click;
            btnScanFile = CreateButton("📁 Bilgisayardan Seç", 130, 10, 250, 35, colorNavy, Color.White);
            btnScanFile.Click += BtnScanFile_Click;
            gbDevice.Controls.Add(btnRefreshScanners); gbDevice.Controls.Add(txtSelectedScanner); gbDevice.Controls.Add(btnScanDevice); gbDevice.Controls.Add(btnScanFile);
            panelLeft.Controls.Add(gbDevice);

            GroupBox gbThumbnails = new GroupBox { Text = "İşlem Bekleyen Evraklar", Top = 285, Left = 10, Width = 270, Height = 390, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            Label lblSearch = new Label { Text = "🔍 Ara:", Top = 26, Left = 10, AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
            txtSearchFilter = new TextBox { Width = 180, Top = 24, Left = 75, Font = new Font("Segoe UI", 9) };
            txtSearchFilter.TextChanged += TxtSearchFilter_TextChanged;
            gbThumbnails.Controls.Add(lblSearch);
            gbThumbnails.Controls.Add(txtSearchFilter);

            flowThumbnails = new FlowLayoutPanel { Top = 55, Left = 5, Width = 260, Height = 325, AutoScroll = true };
            gbThumbnails.Controls.Add(flowThumbnails);

            panelLeft.Controls.Add(gbThumbnails);

            btnSaveAll = CreateButton("💾 Arşive Kaydet", 690, 10, 270, 50, colorGreen, Color.White);
            btnSaveAll.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnSaveAll.Click += BtnSaveAll_Click;
            panelLeft.Controls.Add(btnSaveAll);

            btnThemeToggle = CreateButton("🌙 Karanlık Tema", 750, 10, 270, 40, colorNavy, Color.White);
            btnThemeToggle.Click += BtnThemeToggle_Click;
            panelLeft.Controls.Add(btnThemeToggle);

            // SAĞ PANEL
            panelRight = new Panel { Dock = DockStyle.Right, Width = 340, Padding = new Padding(10) };
            this.Controls.Add(panelRight);

            GroupBox gbProfile = new GroupBox { Text = "Personel Kimlik Bilgileri", Top = 10, Left = 10, Width = 320, Height = 175, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            Label lblName = new Label { Text = "İsim Soyisim:", Top = 25, Left = 15, Width = 290, Font = new Font("Segoe UI", 8) };
            txtName = new TextBox { Top = 40, Left = 15, Width = 290, Font = new Font("Segoe UI", 10) };
            txtName.TextChanged += TxtProfile_TextChanged;
            Label lblReg = new Label { Text = "Sicil Numarası (Opsiyonel):", Top = 70, Left = 15, Width = 290, Font = new Font("Segoe UI", 8) };
            txtRegistryNo = new TextBox { Top = 85, Left = 15, Width = 290, Font = new Font("Segoe UI", 10) };
            txtRegistryNo.TextChanged += TxtProfile_TextChanged;
            Label lblType = new Label { Text = "Kadro Tipi:", Top = 115, Left = 15, Width = 290, Font = new Font("Segoe UI", 8) };
            cmbEmployeeType = new ComboBox { Top = 130, Left = 15, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 10) };
            cmbEmployeeType.Items.Add("PERSONEL"); cmbEmployeeType.Items.Add("STAJYER"); cmbEmployeeType.SelectedIndex = 0;
            cmbEmployeeType.SelectedIndexChanged += CmbEmployeeType_SelectedIndexChanged;
            gbProfile.Controls.Add(lblName); gbProfile.Controls.Add(txtName); gbProfile.Controls.Add(lblReg); gbProfile.Controls.Add(txtRegistryNo); gbProfile.Controls.Add(lblType); gbProfile.Controls.Add(cmbEmployeeType);
            panelRight.Controls.Add(gbProfile);

            GroupBox gbEdit = new GroupBox { Text = "Görüntü İşleme ve Düzenleme", Top = 195, Left = 10, Width = 320, Height = 525, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            Label lblRot = new Label { Text = "Yüz Hizalama (Açı):", Top = 23, Left = 15, AutoSize = true, Font = new Font("Segoe UI", 9) };
            txtRotationManual = new TextBox { Top = 20, Left = 155, Width = 40, Font = new Font("Segoe UI", 9, FontStyle.Bold), Text = "0", TextAlign = HorizontalAlignment.Center };
            txtRotationManual.TextChanged += (s, e) => {
                if (int.TryParse(txtRotationManual.Text, out int val))
                {
                    if (val >= -180 && val <= 180 && tbRotation.Value != val)
                    {
                        GetCurrentDoc()?.SaveState();
                        tbRotation.Value = val;
                    }
                }
            };
            Label lblDegree = new Label { Text = "°", Top = 20, Left = 195, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            tbRotation = new TrackBar { Top = 55, Left = 15, Width = 290, Minimum = -180, Maximum = 180, Value = 0, TickStyle = TickStyle.None, AutoSize = false, Height = 25 };

            tbRotation.MouseDown += (s, e) => { GetCurrentDoc()?.SaveState(); };
            tbRotation.ValueChanged += TbRotation_Scroll;
            gbEdit.Controls.Add(lblRot); gbEdit.Controls.Add(txtRotationManual); gbEdit.Controls.Add(lblDegree); gbEdit.Controls.Add(tbRotation);

            Label lblZoom = new Label { Text = "Yakınlaştırma:", Top = 85, Left = 15, AutoSize = true, Font = new Font("Segoe UI", 9) };
            lblZoomValue = new Label { Text = "%100", Top = 85, Left = 220, Width = 80, TextAlign = ContentAlignment.TopRight, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            tbZoom = new TrackBar { Top = 105, Left = 15, Width = 290, Minimum = 10, Maximum = 500, Value = 100, TickStyle = TickStyle.None, AutoSize = false, Height = 25 };
            tbZoom.ValueChanged += TbZoom_Scroll;
            gbEdit.Controls.Add(lblZoom); gbEdit.Controls.Add(lblZoomValue); gbEdit.Controls.Add(tbZoom);

            Label lblW = new Label { Text = "Genişlik:", Top = 155, Left = 15, AutoSize = true, Font = new Font("Segoe UI", 9) };
            txtCropWidth = new TextBox { Top = 152, Left = 80, Width = 40, Font = new Font("Segoe UI", 9) };
            txtCropWidth.TextChanged += ManualDimension_TextChanged;

            Label lblH = new Label { Text = "Yükseklik:", Top = 155, Left = 160, AutoSize = true, Font = new Font("Segoe UI", 9) };
            txtCropHeight = new TextBox { Top = 152, Left = 230, Width = 40, Font = new Font("Segoe UI", 9) };
            txtCropHeight.TextChanged += ManualDimension_TextChanged;

            btnAutoCrop = CreateButton("🎯 Biyometrik Odakla", 195, 15, 290, 35, colorNavy, Color.White);
            btnAutoCrop.Click += BtnAutoCrop_Click;

            btnStartCrop = CreateButton("✂ Seçim Kutusu Aç", 240, 15, 290, 35, colorNavy, Color.White);
            btnStartCrop.Click += (s, e) => { SetMode(ToolMode.Crop); selectionRect = Rectangle.Empty; pbMain.Invalidate(); };

            btnApplyCrop = CreateButton("✔ Kes", 285, 15, 140, 35, colorGreen, Color.White);
            btnApplyCrop.Click += BtnApplyCrop_Click;

            btnCancelCrop = CreateButton("❌ İptal", 285, 165, 140, 35, Color.FromArgb(192, 57, 43), Color.White);
            btnCancelCrop.Click += (s, e) => {
                selectionRect = Rectangle.Empty;
                txtCropWidth.Clear();
                txtCropHeight.Clear();
                SetMode(ToolMode.None);
                pbMain.Invalidate();
            };

            gbEdit.Controls.Add(lblW); gbEdit.Controls.Add(txtCropWidth); gbEdit.Controls.Add(lblH); gbEdit.Controls.Add(txtCropHeight);
            gbEdit.Controls.Add(btnAutoCrop); gbEdit.Controls.Add(btnStartCrop); gbEdit.Controls.Add(btnApplyCrop); gbEdit.Controls.Add(btnCancelCrop);

            Label lblEraserSize = new Label { Text = "Fırça Boyutu:", Top = 345, Left = 15, AutoSize = true, Font = new Font("Segoe UI", 9) };
            lblEraserSizeValue = new Label { Text = "30px", Top = 345, Left = 220, Width = 80, TextAlign = ContentAlignment.TopRight, Font = new Font("Segoe UI", 9, FontStyle.Bold) };

            tbEraserSize = new TrackBar { Top = 365, Left = 15, Width = 290, Minimum = 5, Maximum = 150, Value = 30, TickStyle = TickStyle.None, AutoSize = false, Height = 25 };
            tbEraserSize.ValueChanged += (s, e) => { lblEraserSizeValue.Text = $"{tbEraserSize.Value}px"; pbMain.Invalidate(); };

            btnEraser = CreateButton("🧹 Akıllı Silgi", 410, 15, 135, 35, colorNavy, Color.White);
            btnEraser.Click += (s, e) => { SetMode(ToolMode.Eraser); };

            btnRestore = CreateButton("✏ Onarma", 410, 170, 135, 35, colorNavy, Color.White);
            btnRestore.Click += (s, e) => { SetMode(ToolMode.Restore); };

            gbEdit.Controls.Add(lblEraserSize); gbEdit.Controls.Add(lblEraserSizeValue); gbEdit.Controls.Add(tbEraserSize);
            gbEdit.Controls.Add(btnEraser); gbEdit.Controls.Add(btnRestore);

            btnUndo = CreateButton("↩ Geri", 465, 15, 135, 35, colorNavy, Color.White);
            btnUndo.Click += (s, e) => Undo();

            btnRedo = CreateButton("↪ İleri", 465, 170, 135, 35, colorNavy, Color.White);
            btnRedo.Click += (s, e) => Redo();

            gbEdit.Controls.Add(btnUndo); gbEdit.Controls.Add(btnRedo);

            panelRight.Controls.Add(gbEdit);

            Label lblInfo = new Label
            {
                Text = "Bilgi: Silgi ile dış arka planı temizleyin, hatalı yerleri 'Onarma' kalemi ile orijinal haline döndürün.",
                Top = 730,
                Left = 10,
                Width = 320,
                AutoSize = true,
                MaximumSize = new Size(320, 0),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.Gray,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            panelRight.Controls.Add(lblInfo);

            panelCenter = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BorderStyle = BorderStyle.Fixed3D };

            pbMain = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, Visible = false };

            pbMain.MouseDown += PbMain_MouseDown;
            pbMain.MouseMove += PbMain_MouseMove;
            pbMain.MouseUp += PbMain_MouseUp;
            pbMain.Paint += PbMain_Paint;
            pbMain.MouseLeave += (s, e) => { if (currentMode == ToolMode.Eraser || currentMode == ToolMode.Restore) { currentMousePos = Point.Empty; pbMain.Invalidate(); } };
            pbMain.DoubleClick += (s, e) => FitToScreen();

            panelCenter.MouseWheel += PanelCenter_MouseWheel;
            panelCenter.MouseEnter += (s, e) => panelCenter.Focus();

            panelCenter.Controls.Add(pbMain);
            this.Controls.Add(panelCenter);

            SetupContextMenu();
        }

        private void TxtSearchFilter_TextChanged(object sender, EventArgs e)
        {
            string searchTerm = txtSearchFilter.Text.ToLower(new CultureInfo("tr-TR"));

            foreach (Control c in flowThumbnails.Controls)
            {
                if (c is Panel pnl)
                {
                    int index = (int)pnl.Tag;
                    if (index >= 0 && index < documents.Count)
                    {
                        var doc = documents[index];
                        bool match = string.IsNullOrEmpty(searchTerm) ||
                                     doc.Name.ToLower(new CultureInfo("tr-TR")).Contains(searchTerm) ||
                                     doc.RegistryNo.ToLower(new CultureInfo("tr-TR")).Contains(searchTerm);

                        pnl.Visible = match;
                    }
                }
            }
        }

        private void SetupContextMenu()
        {
            ContextMenuStrip ctx = new ContextMenuStrip();
            ctx.Items.Add("🧹 Akıllı Silgi (E)", null, (s, e) => SetMode(ToolMode.Eraser));
            ctx.Items.Add("✏ Onarma (R)", null, (s, e) => SetMode(ToolMode.Restore));
            ctx.Items.Add("✂ Kırpma Kutusu (C)", null, (s, e) => SetMode(ToolMode.Crop));
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("↩ Geri Al (Ctrl+Z)", null, (s, e) => Undo());
            ctx.Items.Add("↪ İleri Al (Ctrl+Y)", null, (s, e) => Redo());
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("🔲 Ekrana Sığdır", null, (s, e) => FitToScreen());
            pbMain.ContextMenuStrip = ctx;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab && !isShowingOriginal)
            {
                isShowingOriginal = true;
                RefreshMainImage();
                e.Handled = true;
            }

            if (e.KeyCode == Keys.Space && currentMode != ToolMode.Pan)
            {
                previousMode = currentMode;
                SetMode(ToolMode.Pan);
                e.Handled = true;
            }

            if (e.KeyCode == Keys.E) { btnEraser.PerformClick(); e.Handled = true; }
            if (e.KeyCode == Keys.R) { btnRestore.PerformClick(); e.Handled = true; }
            if (e.KeyCode == Keys.C) { btnStartCrop.PerformClick(); e.Handled = true; }

            if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; }
        }

        private void MainForm_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab)
            {
                isShowingOriginal = false;
                RefreshMainImage();
                e.Handled = true;
            }
            if (e.KeyCode == Keys.Space)
            {
                SetMode(previousMode);
                e.Handled = true;
            }
        }

        private Button CreateButton(string text, int top, int left, int width, int height, Color backColor, Color foreColor)
        {
            Button btn = new Button { Text = text, Top = top, Left = left, Width = width, Height = height, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = backColor, ForeColor = foreColor, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void UpdateStatus(string message) { lblStatus.Text = message; statusStrip.Refresh(); }

        private PhotoDoc GetCurrentDoc() => currentDocIndex >= 0 && currentDocIndex < documents.Count ? documents[currentDocIndex] : null;

        private void BtnThemeToggle_Click(object sender, EventArgs e)
        {
            isDarkMode = !isDarkMode;
            ApplyTheme();
            UpdateStatus(isDarkMode ? "Karanlık tema aktif." : "Aydınlık tema aktif.");
        }

        private void ApplyTheme()
        {
            Color bgMain = isDarkMode ? Color.FromArgb(30, 30, 30) : Color.FromArgb(240, 240, 240);
            Color bgPanel = isDarkMode ? Color.FromArgb(45, 45, 48) : Color.WhiteSmoke;
            Color fgText = isDarkMode ? Color.White : Color.Black;
            Color bgThumb = isDarkMode ? Color.FromArgb(60, 60, 60) : Color.White;
            Color bgCenter = isDarkMode ? Color.FromArgb(20, 20, 20) : Color.DarkGray;

            this.BackColor = bgMain; panelLeft.BackColor = bgPanel; panelRight.BackColor = bgPanel;
            panelCenter.BackColor = bgCenter; flowThumbnails.BackColor = bgThumb; pbMain.BackColor = bgCenter;
            statusStrip.BackColor = isDarkMode ? Color.FromArgb(45, 45, 48) : Color.LightGray;
            lblStatus.ForeColor = fgText;

            btnThemeToggle.Text = isDarkMode ? "☀ Aydınlık Tema" : "🌙 Karanlık Tema";

            UpdateControlsTheme(this.Controls, fgText, bgPanel, bgThumb);
            try
            {
                string logo = isDarkMode ? "kart_atolye_logo_dark.png" : "kart_atolye_logo_light.png";
                string logoPath = Path.Combine(Application.StartupPath, logo);
                if (File.Exists(logoPath)) { if (pbLogo.Image != null) pbLogo.Image.Dispose(); pbLogo.Image = Image.FromFile(logoPath); }
            }
            catch { }
        }

        private void UpdateControlsTheme(Control.ControlCollection controls, Color fg, Color bgPanel, Color bgThumb)
        {
            foreach (Control c in controls)
            {
                if (c is GroupBox || c is Label) c.ForeColor = fg;
                if (c is TextBox || c is ComboBox || c is TrackBar) { c.BackColor = bgThumb; c.ForeColor = fg; }
                if (c.HasChildren) UpdateControlsTheme(c.Controls, fg, bgPanel, bgThumb);
            }
        }

        private void SetMode(ToolMode mode)
        {
            currentMode = mode;
            if (mode == ToolMode.Eraser || mode == ToolMode.Restore) { pbMain.Cursor = Cursors.Cross; selectionRect = Rectangle.Empty; pbMain.Invalidate(); }
            else if (mode == ToolMode.Crop) pbMain.Cursor = Cursors.Cross;
            else if (mode == ToolMode.Pan) pbMain.Cursor = Cursors.Hand;
            else pbMain.Cursor = Cursors.Default;
        }

        private void ManualDimension_TextChanged(object sender, EventArgs e)
        {
            if (isUpdatingTextBoxes || GetCurrentDoc() == null || currentMode != ToolMode.Crop) return;
            if (int.TryParse(txtCropWidth.Text, out int w) && int.TryParse(txtCropHeight.Text, out int h) && w > 0 && h > 0)
            {
                int sW = (int)(w * zoomFactor); int sH = (int)(h * zoomFactor);
                if (selectionRect == Rectangle.Empty) selectionRect = new Rectangle(50, 50, sW, sH);
                else { selectionRect.Width = sW; selectionRect.Height = sH; }
                pbMain.Invalidate();
            }
        }

        private void UpdateDimensionTextBoxes()
        {
            if (selectionRect != Rectangle.Empty)
            {
                isUpdatingTextBoxes = true;
                txtCropWidth.Text = ((int)(selectionRect.Width / zoomFactor)).ToString();
                txtCropHeight.Text = ((int)(selectionRect.Height / zoomFactor)).ToString();
                isUpdatingTextBoxes = false;
            }
        }

        private void BtnAutoCrop_Click(object sender, EventArgs e)
        {
            var doc = GetCurrentDoc(); if (doc == null) return;
            SetMode(ToolMode.Crop);
            int cropWidth = (int)(doc.CurrentImage.Width * 0.55);
            int cropHeight = (int)(cropWidth * 1.2);
            if (cropHeight > doc.CurrentImage.Height * 0.95) { cropHeight = (int)(doc.CurrentImage.Height * 0.95); cropWidth = (int)(cropHeight / 1.2); }
            int centerX = (doc.CurrentImage.Width - cropWidth) / 2;
            int centerY = (int)(doc.CurrentImage.Height * 0.12);
            if (centerY + cropHeight > doc.CurrentImage.Height) centerY = doc.CurrentImage.Height - cropHeight;
            if (centerY < 0) centerY = 0;

            selectionRect = new Rectangle((int)(centerX * zoomFactor), (int)(centerY * zoomFactor), (int)(cropWidth * zoomFactor), (int)(cropHeight * zoomFactor));
            UpdateDimensionTextBoxes(); pbMain.Invalidate();
        }

        private void BtnRefreshScanners_Click(object sender, EventArgs e)
        {
            using (ScannerSelectionForm form = new ScannerSelectionForm(isDarkMode))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    txtSelectedScanner.Text = form.SelectedScannerName;
                    UpdateStatus($"Tarayıcı seçildi: {form.SelectedScannerName}");
                }
            }
        }

        private void BtnScanDevice_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(txtSelectedScanner.Text) || txtSelectedScanner.Text.Contains("Bulunamadı"))
            { MessageBox.Show("Lütfen önce bir cihaz seçin.", "Cihaz Seçilmedi", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try
            {
                var deviceManager = new WIA.DeviceManager(); WIA.DeviceInfo selected = null;
                foreach (WIA.DeviceInfo info in deviceManager.DeviceInfos) { if (info.Properties["Name"].get_Value().ToString() == txtSelectedScanner.Text) { selected = info; break; } }
                if (selected != null)
                {
                    var imageFile = (WIA.ImageFile)new WIA.CommonDialog().ShowTransfer(selected.Connect().Items[1], WIA_FORMAT_PNG, true);
                    if (imageFile != null)
                    {
                        byte[] buffer = (byte[])imageFile.FileData.get_BinaryData();
                        using (MemoryStream ms = new MemoryStream(buffer))
                        using (Image temp = Image.FromStream(ms)) { AddImageToQueue(new Bitmap(temp)); }
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message, "Tarama Başarısız", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void BtnScanFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Multiselect = true, Filter = "Resim|*.jpg;*.png;*.bmp" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    foreach (string file in ofd.FileNames)
                    {
                        using (Image temp = Image.FromFile(file)) { AddImageToQueue(new Bitmap(temp)); }
                    }
                }
            }
        }

        private void AddImageToQueue(Bitmap bmp)
        {
            PhotoDoc doc = new PhotoDoc { OriginalScan = new Bitmap(bmp), WorkingMaster = new Bitmap(bmp), CurrentImage = new Bitmap(bmp) };
            documents.Add(doc);

            int index = documents.Count - 1;

            Panel pnl = new Panel { Width = 240, Height = 170, Margin = new Padding(8, 5, 5, 5), Tag = index };
            PictureBox pb = new PictureBox { Width = 220, Height = 130, SizeMode = PictureBoxSizeMode.Zoom, Image = new Bitmap(bmp), Top = 5, Left = 10, Cursor = Cursors.Hand, BorderStyle = BorderStyle.FixedSingle, Tag = index };
            pb.Click += (s, ev) => SelectImage((int)((PictureBox)s).Tag);

            ContextMenuStrip thumbMenu = new ContextMenuStrip();
            thumbMenu.Items.Add("🗑️ Seçili Evrakı Kaldır", null, (s, ev) => RemoveImage((int)pb.Tag));
            pb.ContextMenuStrip = thumbMenu;

            Label lbl = new Label { Width = 220, Top = 140, Left = 10, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8, FontStyle.Bold), ForeColor = isDarkMode ? Color.White : Color.Black };

            pnl.Controls.Add(pb); pnl.Controls.Add(lbl);
            flowThumbnails.Controls.Add(pnl);

            UpdateThumbnailLabels();

            TxtSearchFilter_TextChanged(null, null);

            if (documents.Count == 1) SelectImage(0);
        }

        private void RemoveImage(int index)
        {
            if (index < 0 || index >= documents.Count) return;

            DialogResult result = MessageBox.Show(
                "Bu evrakı listeden kaldırmak istediğinize emin misiniz?",
                "Evrak Silme Onayı",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.No) return;

            documents[index].Dispose();
            documents.RemoveAt(index);

            Control panelToRemove = null;
            foreach (Control c in flowThumbnails.Controls)
            {
                if (c is Panel pnl && (int)pnl.Tag == index)
                {
                    panelToRemove = c;
                    break;
                }
            }

            if (panelToRemove != null)
            {
                foreach (Control child in panelToRemove.Controls)
                {
                    if (child is PictureBox pb) pb.Image?.Dispose();
                    child.Dispose();
                }
                flowThumbnails.Controls.Remove(panelToRemove);
                panelToRemove.Dispose();
            }

            for (int i = 0; i < flowThumbnails.Controls.Count; i++)
            {
                if (flowThumbnails.Controls[i] is Panel pnl)
                {
                    pnl.Tag = i;
                    foreach (Control child in pnl.Controls)
                    {
                        if (child is PictureBox pb) pb.Tag = i;
                    }
                }
            }

            UpdateThumbnailLabels();

            if (documents.Count == 0)
            {
                currentDocIndex = -1;
                pbMain.Image?.Dispose();
                pbMain.Image = null;

                pbMain.Visible = false;

                txtName.Clear();
                txtRegistryNo.Clear();
                selectionRect = Rectangle.Empty;
                pbMain.Invalidate();
                UpdateStatus("Tüm evraklar listeden kaldırıldı.");
            }
            else
            {
                int nextIndex = index >= documents.Count ? documents.Count - 1 : index;
                SelectImage(nextIndex);
            }
        }

        private void BtnSaveAll_Click(object sender, EventArgs e)
        {
            if (documents.Count == 0) return;
            using (FolderBrowserDialog fbd = new FolderBrowserDialog { Description = "Kurum Arşivi Klasörünü Seçin" })
            {
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        CultureInfo tr = new CultureInfo("tr-TR");
                        string folder = Path.Combine(fbd.SelectedPath, cmbEmployeeType.SelectedItem.ToString(), DateTime.Now.ToString("MMMM yyyy", tr).ToUpper(tr));

                        for (int i = 0; i < documents.Count; i++)
                        {
                            var doc = documents[i];
                            string name = Sanitize(doc.Name); string reg = Sanitize(doc.RegistryNo);
                            string init = string.IsNullOrEmpty(name) ? "BILINMEYEN" : name.Substring(0, 1).ToUpper(tr);
                            string tgtDir = Path.Combine(folder, init);
                            if (!Directory.Exists(tgtDir)) Directory.CreateDirectory(tgtDir);

                            string bName = string.IsNullOrEmpty(name) ? "Evrak" : name;
                            string rSuf = string.IsNullOrEmpty(reg) ? "" : $"_{reg}";
                            string file = $"{bName}{rSuf}.png";
                            string full = Path.Combine(tgtDir, file);

                            int c = 1; while (File.Exists(full)) { full = Path.Combine(tgtDir, $"{bName}{rSuf}_{c}.png"); c++; }
                            doc.CurrentImage.Save(full, ImageFormat.Png);
                        }

                        foreach (var d in documents) d.Dispose();
                        documents.Clear();

                        foreach (Control c in flowThumbnails.Controls)
                        {
                            if (c is Panel pnl)
                            {
                                foreach (Control child in pnl.Controls) if (child is PictureBox pb) pb.Image?.Dispose();
                            }
                        }
                        flowThumbnails.Controls.Clear();

                        currentDocIndex = -1;
                        txtName.Clear(); txtRegistryNo.Clear();
                        txtSearchFilter.Clear();
                        SetZoom(1.0f);
                        tbRotation.Value = 0; txtRotationManual.Text = "0";
                        pbMain.Image?.Dispose(); pbMain.Image = null;

                        pbMain.Visible = false;

                        selectionRect = Rectangle.Empty;

                        UpdateStatus("Arşivleme işlemi başarıyla tamamlandı. Liste temizlendi.");
                        MessageBox.Show("Kayıt İşlemi Başarılı.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show("Hata: " + ex.Message, "Kayıt Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
        }
        private string Sanitize(string n) { foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c.ToString(), ""); return n.Replace(" ", "_"); }

        private void CmbEmployeeType_SelectedIndexChanged(object sender, EventArgs e)
        {
            var doc = GetCurrentDoc();
            if (isUpdatingTextBoxes || doc == null || cmbEmployeeType.SelectedItem == null) return;

            doc.EmployeeType = cmbEmployeeType.SelectedItem.ToString();
        }

        private void TxtProfile_TextChanged(object sender, EventArgs e)
        {
            var doc = GetCurrentDoc(); if (isUpdatingTextBoxes || doc == null) return;
            doc.Name = txtName.Text; doc.RegistryNo = txtRegistryNo.Text;
            UpdateThumbnailLabels();

            TxtSearchFilter_TextChanged(null, null);
        }

        private void UpdateThumbnailLabels()
        {
            foreach (Control c in flowThumbnails.Controls)
            {
                if (c is Panel pnl)
                {
                    int i = (int)pnl.Tag; var doc = documents[i];
                    string fn = Sanitize(doc.Name); string rg = Sanitize(doc.RegistryNo);
                    if (string.IsNullOrEmpty(fn)) fn = "Evrak";
                    string display = $"{fn}{(string.IsNullOrEmpty(rg) ? "" : $"_{rg}")}.png";
                    foreach (Control child in pnl.Controls) if (child is Label lbl) lbl.Text = display;
                }
            }
        }

        private void SelectImage(int index)
        {
            if (index < 0 || index >= documents.Count) return;
            currentDocIndex = index; var doc = documents[currentDocIndex];

            isUpdatingTextBoxes = true;
            txtName.Text = doc.Name; txtRegistryNo.Text = doc.RegistryNo;

            if (cmbEmployeeType.Items.Contains(doc.EmployeeType))
            {
                cmbEmployeeType.SelectedItem = doc.EmployeeType;
            }

            isUpdatingTextBoxes = false;

            SetMode(ToolMode.None);
            selectionRect = Rectangle.Empty;
            txtCropWidth.Clear();
            txtCropHeight.Clear();

            SetZoom(1.0f);
            tbRotation.Value = 0; txtRotationManual.Text = "0";

            pbMain.Visible = true;

            RefreshMainImage();
        }

        private void UpdateThumbnail(int index, Bitmap bmp)
        {
            foreach (Control c in flowThumbnails.Controls)
            {
                if (c is Panel pnl && (int)pnl.Tag == index)
                {
                    foreach (Control child in pnl.Controls) if (child is PictureBox pb) { pb.Image?.Dispose(); pb.Image = new Bitmap(bmp); break; }
                    break;
                }
            }
        }

        private void TbRotation_Scroll(object sender, EventArgs e)
        {
            var doc = GetCurrentDoc(); if (doc == null) return;
            txtRotationManual.Text = tbRotation.Value.ToString();

            doc.WorkingMaster?.Dispose();
            doc.WorkingMaster = RotateImageByAngle(doc.OriginalScan, tbRotation.Value);
            doc.CurrentImage?.Dispose();
            doc.CurrentImage = new Bitmap(doc.WorkingMaster);

            RefreshMainImage();
        }

        private Bitmap RotateImageByAngle(Image img, float angle)
        {
            Bitmap bmp = new Bitmap(img.Width, img.Height);
            bmp.SetResolution(img.HorizontalResolution, img.VerticalResolution);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TranslateTransform((float)img.Width / 2, (float)img.Height / 2);
                g.RotateTransform(angle);
                g.TranslateTransform(-(float)img.Width / 2, -(float)img.Height / 2);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new PointF(0, 0));
            }
            return bmp;
        }

        private void SetZoom(float newZoom)
        {
            newZoom = Math.Max(0.1f, Math.Min(5.0f, newZoom));
            if (zoomFactor == newZoom) return;

            float oldZoom = zoomFactor;
            zoomFactor = newZoom;

            if (selectionRect != Rectangle.Empty && oldZoom > 0)
            {
                float ratio = zoomFactor / oldZoom;
                selectionRect = new Rectangle(
                    (int)(selectionRect.X * ratio),
                    (int)(selectionRect.Y * ratio),
                    (int)(selectionRect.Width * ratio),
                    (int)(selectionRect.Height * ratio)
                );
                UpdateDimensionTextBoxes();
            }

            tbZoom.ValueChanged -= TbZoom_Scroll;
            tbZoom.Value = (int)(zoomFactor * 100);
            lblZoomValue.Text = $"%{tbZoom.Value}";
            tbZoom.ValueChanged += TbZoom_Scroll;

            RefreshMainImage();
        }

        private void TbZoom_Scroll(object sender, EventArgs e)
        {
            SetZoom(tbZoom.Value / 100f);
        }

        private void UpdateZoomUI()
        {
            int z = (int)(zoomFactor * 100);
            tbZoom.Value = Math.Max(tbZoom.Minimum, Math.Min(tbZoom.Maximum, z));
            lblZoomValue.Text = $"%{tbZoom.Value}";
        }

        private void FitToScreen()
        {
            var doc = GetCurrentDoc(); if (doc == null) return;
            float wRatio = (float)(panelCenter.ClientSize.Width - 30) / doc.CurrentImage.Width;
            float hRatio = (float)(panelCenter.ClientSize.Height - 30) / doc.CurrentImage.Height;
            SetZoom(Math.Min(wRatio, hRatio));
        }

        private void RefreshMainImage()
        {
            var doc = GetCurrentDoc(); if (doc == null) return;
            Bitmap source = isShowingOriginal ? doc.WorkingMaster : doc.CurrentImage;

            Bitmap zoomedImg = new Bitmap((int)(source.Width * zoomFactor), (int)(source.Height * zoomFactor));
            using (Graphics g = Graphics.FromImage(zoomedImg))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, new Rectangle(0, 0, zoomedImg.Width, zoomedImg.Height));
            }
            var old = pbMain.Image;
            pbMain.Image = zoomedImg;
            old?.Dispose();
            pbMain.Location = new Point(Math.Max(0, (panelCenter.Width - pbMain.Width) / 2), Math.Max(0, (panelCenter.Height - pbMain.Height) / 2));
        }

        private void PanelCenter_MouseWheel(object sender, MouseEventArgs e)
        {
            if (GetCurrentDoc() == null) return;
            Point mousePos = pbMain.PointToClient(Cursor.Position);
            float oldZoom = zoomFactor;

            float newZoom = zoomFactor;
            if (e.Delta > 0) newZoom *= 1.2f; else newZoom /= 1.2f;

            SetZoom(newZoom);

            int newX = (int)(mousePos.X * (zoomFactor / oldZoom)) - mousePos.X + Math.Abs(panelCenter.AutoScrollPosition.X);
            int newY = (int)(mousePos.Y * (zoomFactor / oldZoom)) - mousePos.Y + Math.Abs(panelCenter.AutoScrollPosition.Y);
            panelCenter.AutoScrollPosition = new Point(newX, newY);
        }

        private DragZone GetDragZone(Point p)
        {
            if (selectionRect == Rectangle.Empty) return DragZone.None;
            if (new Rectangle(selectionRect.X - handleSize, selectionRect.Y - handleSize, handleSize * 2, handleSize * 2).Contains(p)) return DragZone.TopLeft;
            if (new Rectangle(selectionRect.Right - handleSize, selectionRect.Y - handleSize, handleSize * 2, handleSize * 2).Contains(p)) return DragZone.TopRight;
            if (new Rectangle(selectionRect.X - handleSize, selectionRect.Bottom - handleSize, handleSize * 2, handleSize * 2).Contains(p)) return DragZone.BottomLeft;
            if (new Rectangle(selectionRect.Right - handleSize, selectionRect.Bottom - handleSize, handleSize * 2, handleSize * 2).Contains(p)) return DragZone.BottomRight;
            if (selectionRect.Contains(p)) return DragZone.Inside;
            return DragZone.None;
        }

        private void PbMain_MouseDown(object sender, MouseEventArgs e)
        {
            var doc = GetCurrentDoc(); if (doc == null || currentMode == ToolMode.None || isShowingOriginal) return;

            if (currentMode == ToolMode.Pan)
            {
                isPanning = true; panStartMouse = Cursor.Position; panStartScroll = panelCenter.AutoScrollPosition;
            }
            else if (currentMode == ToolMode.Crop)
            {
                if (e.Button == MouseButtons.Left)
                {
                    activeDragZone = GetDragZone(e.Location);
                    if (activeDragZone != DragZone.None) { isDraggingSelection = true; dragStartPoint = e.Location; originalRect = selectionRect; if (activeDragZone == DragZone.Inside) dragOffset = new Point(e.X - selectionRect.X, e.Y - selectionRect.Y); }
                    else { isSelecting = true; selectionStart = e.Location; selectionRect = new Rectangle(e.X, e.Y, 0, 0); }
                }
            }
            else if (currentMode == ToolMode.Eraser || currentMode == ToolMode.Restore)
            {
                if (e.Button == MouseButtons.Left)
                {
                    doc.SaveState();
                    ExecuteToolAtPoint(e.Location);
                }
            }
        }

        private void PbMain_MouseMove(object sender, MouseEventArgs e)
        {
            var doc = GetCurrentDoc(); if (doc == null || isShowingOriginal) return;

            if (currentMode == ToolMode.Pan && isPanning)
            {
                Point curMouse = Cursor.Position;
                panelCenter.AutoScrollPosition = new Point(Math.Abs(panStartScroll.X) + (panStartMouse.X - curMouse.X), Math.Abs(panStartScroll.Y) + (panStartMouse.Y - curMouse.Y));
            }
            else if (currentMode == ToolMode.Eraser || currentMode == ToolMode.Restore)
            {
                currentMousePos = e.Location;
                if (e.Button == MouseButtons.Left) ExecuteToolAtPoint(e.Location);
                else pbMain.Invalidate();
            }
            else if (currentMode == ToolMode.Crop)
            {
                if (isSelecting)
                {
                    selectionRect = new Rectangle(Math.Min(selectionStart.X, e.X), Math.Min(selectionStart.Y, e.Y), Math.Abs(selectionStart.X - e.X), Math.Abs(selectionStart.Y - e.Y));
                    UpdateDimensionTextBoxes(); pbMain.Invalidate();
                }
                else if (isDraggingSelection)
                {
                    int dx = e.X - dragStartPoint.X; int dy = e.Y - dragStartPoint.Y;
                    switch (activeDragZone)
                    {
                        case DragZone.Inside: selectionRect.X = e.X - dragOffset.X; selectionRect.Y = e.Y - dragOffset.Y; break;
                        case DragZone.TopLeft: selectionRect = new Rectangle(originalRect.X + dx, originalRect.Y + dy, originalRect.Width - dx, originalRect.Height - dy); break;
                        case DragZone.TopRight: selectionRect = new Rectangle(originalRect.X, originalRect.Y + dy, originalRect.Width + dx, originalRect.Height - dy); break;
                        case DragZone.BottomLeft: selectionRect = new Rectangle(originalRect.X + dx, originalRect.Y, originalRect.Width - dx, originalRect.Height + dy); break;
                        case DragZone.BottomRight: selectionRect = new Rectangle(originalRect.X, originalRect.Y, originalRect.Width + dx, originalRect.Height + dy); break;
                    }
                    UpdateDimensionTextBoxes(); pbMain.Invalidate();
                }
                else
                {
                    switch (GetDragZone(e.Location))
                    {
                        case DragZone.TopLeft: case DragZone.BottomRight: pbMain.Cursor = Cursors.SizeNWSE; break;
                        case DragZone.TopRight: case DragZone.BottomLeft: pbMain.Cursor = Cursors.SizeNESW; break;
                        case DragZone.Inside: pbMain.Cursor = Cursors.SizeAll; break;
                        default: pbMain.Cursor = Cursors.Cross; break;
                    }
                }
            }
        }

        private void PbMain_MouseUp(object sender, MouseEventArgs e)
        {
            var doc = GetCurrentDoc();
            if (isPanning) isPanning = false;
            else if (isSelecting) isSelecting = false;
            else if (isDraggingSelection) { isDraggingSelection = false; activeDragZone = DragZone.None; pbMain.Cursor = Cursors.Cross; }
            else if (currentMode == ToolMode.Eraser || currentMode == ToolMode.Restore)
            {
                if (e.Button == MouseButtons.Left && doc != null) UpdateThumbnail(currentDocIndex, doc.CurrentImage);
            }
        }

        private void PbMain_Paint(object sender, PaintEventArgs e)
        {
            if (selectionRect.Width > 0 && selectionRect.Height > 0 && currentMode == ToolMode.Crop)
            {
                using (Pen pen = new Pen(Color.FromArgb(231, 76, 60), 2) { DashStyle = DashStyle.Dash }) { e.Graphics.DrawRectangle(pen, selectionRect); }
                SolidBrush brush = new SolidBrush(Color.FromArgb(41, 128, 185));
                e.Graphics.FillRectangle(brush, selectionRect.X - handleSize / 2, selectionRect.Y - handleSize / 2, handleSize, handleSize);
                e.Graphics.FillRectangle(brush, selectionRect.Right - handleSize / 2, selectionRect.Y - handleSize / 2, handleSize, handleSize);
                e.Graphics.FillRectangle(brush, selectionRect.X - handleSize / 2, selectionRect.Bottom - handleSize / 2, handleSize, handleSize);
                e.Graphics.FillRectangle(brush, selectionRect.Right - handleSize / 2, selectionRect.Bottom - handleSize / 2, handleSize, handleSize);
                brush.Dispose();
            }

            if (!isShowingOriginal && (currentMode == ToolMode.Eraser || currentMode == ToolMode.Restore) && currentMousePos != Point.Empty)
            {
                int scaledSize = (int)(tbEraserSize.Value * zoomFactor);
                Color ringColor = currentMode == ToolMode.Eraser ? Color.Red : Color.Green;
                using (Pen pen = new Pen(ringColor, 2)) { e.Graphics.DrawEllipse(pen, currentMousePos.X - scaledSize / 2, currentMousePos.Y - scaledSize / 2, scaledSize, scaledSize); }
            }
        }

        private void Undo()
        {
            var doc = GetCurrentDoc(); if (doc == null || doc.UndoStack.Count == 0) return;
            doc.RedoStack.Push(new DocState { Current = new Bitmap(doc.CurrentImage), Master = new Bitmap(doc.WorkingMaster) });

            var prevState = doc.UndoStack.Pop();
            doc.CurrentImage.Dispose();
            doc.WorkingMaster.Dispose();

            doc.CurrentImage = new Bitmap(prevState.Current);
            doc.WorkingMaster = new Bitmap(prevState.Master);
            prevState.Dispose();

            UpdateThumbnail(currentDocIndex, doc.CurrentImage);
            RefreshMainImage(); UpdateStatus("İşlem Geri Alındı.");
        }

        private void Redo()
        {
            var doc = GetCurrentDoc(); if (doc == null || doc.RedoStack.Count == 0) return;
            doc.UndoStack.Push(new DocState { Current = new Bitmap(doc.CurrentImage), Master = new Bitmap(doc.WorkingMaster) });

            var nextState = doc.RedoStack.Pop();
            doc.CurrentImage.Dispose();
            doc.WorkingMaster.Dispose();

            doc.CurrentImage = new Bitmap(nextState.Current);
            doc.WorkingMaster = new Bitmap(nextState.Master);
            nextState.Dispose();

            UpdateThumbnail(currentDocIndex, doc.CurrentImage);
            RefreshMainImage(); UpdateStatus("İşlem İleri Alındı.");
        }

        private void ExecuteToolAtPoint(Point p)
        {
            var doc = GetCurrentDoc();
            if (doc == null) return;

            int origX = (int)(p.X / zoomFactor);
            int origY = (int)(p.Y / zoomFactor);
            int radius = tbEraserSize.Value / 2;

            Bitmap bmp = doc.CurrentImage;
            Bitmap masterBmp = doc.WorkingMaster;

            int startX = Math.Max(0, origX - radius);
            int endX = Math.Min(bmp.Width, origX + radius);
            int startY = Math.Max(0, origY - radius);
            int endY = Math.Min(bmp.Height, origY + radius);

            int rectW = endX - startX;
            int rectH = endY - startY;

            if (rectW <= 0 || rectH <= 0) return;

            Rectangle lockRect = new Rectangle(startX, startY, rectW, rectH);

            BitmapData bmpData = bmp.LockBits(lockRect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            BitmapData masterData = masterBmp.LockBits(lockRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            int bytes = Math.Abs(bmpData.Stride) * (rectH - 1) + (rectW * 4);

            byte[] rgbValues = new byte[bytes];
            byte[] masterValues = new byte[bytes];

            Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
            Marshal.Copy(masterData.Scan0, masterValues, 0, bytes);

            for (int y = 0; y < rectH; y++)
            {
                for (int x = 0; x < rectW; x++)
                {
                    int imgX = startX + x;
                    int imgY = startY + y;

                    if ((imgX - origX) * (imgX - origX) + (imgY - origY) * (imgY - origY) <= radius * radius)
                    {
                        int index = y * bmpData.Stride + x * 4;
                        if (currentMode == ToolMode.Eraser)
                        {
                            int b = rgbValues[index]; int g = rgbValues[index + 1]; int r = rgbValues[index + 2];
                            if (r > 200 && g > 200 && b > 200)
                            {
                                rgbValues[index + 3] = 0; rgbValues[index + 2] = 0; rgbValues[index + 1] = 0; rgbValues[index] = 0;
                            }
                        }
                        else if (currentMode == ToolMode.Restore)
                        {
                            rgbValues[index] = masterValues[index];
                            rgbValues[index + 1] = masterValues[index + 1];
                            rgbValues[index + 2] = masterValues[index + 2];
                            rgbValues[index + 3] = masterValues[index + 3];
                        }
                    }
                }
            }

            Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
            bmp.UnlockBits(bmpData);
            masterBmp.UnlockBits(masterData);

            RefreshMainImage();
        }

        private void BtnApplyCrop_Click(object sender, EventArgs e)
        {
            var doc = GetCurrentDoc();
            if (doc == null || selectionRect == Rectangle.Empty || selectionRect.Width < 10 || selectionRect.Height < 10)
            { MessageBox.Show("Lütfen önce kırpılacak alanı seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            int origX = (int)(selectionRect.X / zoomFactor); int origY = (int)(selectionRect.Y / zoomFactor);
            int origW = (int)(selectionRect.Width / zoomFactor); int origH = (int)(selectionRect.Height / zoomFactor);
            if (origW <= 0 || origH <= 0) return;

            // KIRPMADAN ÖNCE HAFIZAYA KAYDET
            doc.SaveState();

            Rectangle origRect = new Rectangle(origX, origY, origW, origH);
            Bitmap croppedImg = new Bitmap(origW, origH);
            Bitmap croppedMaster = new Bitmap(origW, origH);

            using (Graphics g = Graphics.FromImage(croppedImg))
            {
                g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(doc.CurrentImage, new Rectangle(0, 0, origW, origH), origRect, GraphicsUnit.Pixel);
            }
            using (Graphics g = Graphics.FromImage(croppedMaster))
            {
                g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(doc.WorkingMaster, new Rectangle(0, 0, origW, origH), origRect, GraphicsUnit.Pixel);
            }

            doc.CurrentImage.Dispose(); doc.CurrentImage = croppedImg;
            doc.WorkingMaster.Dispose(); doc.WorkingMaster = croppedMaster;

            UpdateThumbnail(currentDocIndex, doc.CurrentImage);
            zoomFactor = 1.0f; UpdateZoomUI();
            tbRotation.Value = 0; txtRotationManual.Text = "0";
            selectionRect = Rectangle.Empty; txtCropWidth.Clear(); txtCropHeight.Clear();
            RefreshMainImage(); SetMode(ToolMode.None); UpdateStatus("Kırpma başarıyla uygulandı.");
        }
    }
}