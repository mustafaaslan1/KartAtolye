using System;
using System.Drawing;
using System.Windows.Forms;
using WIA;

namespace KartAtolye
{
    public class ScannerSelectionForm : Form
    {
        public string SelectedScannerName { get; private set; }
        private ListBox listBoxScanners;
        private Button btnSelect, btnCancel;
        private Panel headerPanel;
        private Label lblTitle;

        public ScannerSelectionForm(bool isDarkMode)
        {
            this.Text = "Donanım Seçimi";
            this.Size = new Size(450, 320);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;

            Color bgMain = isDarkMode ? Color.FromArgb(45, 45, 48) : Color.WhiteSmoke;
            Color fgText = isDarkMode ? Color.White : Color.FromArgb(44, 62, 80);
            Color headerBg = isDarkMode ? Color.FromArgb(30, 30, 30) : Color.FromArgb(41, 128, 185);
            Color headerFg = Color.White;

            this.BackColor = bgMain;

            headerPanel = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = headerBg };
            lblTitle = new Label
            {
                Text = "Tarayıcı Seçimi",
                AutoSize = true,
                Top = 15,
                Left = 20,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = headerFg
            };
            headerPanel.Controls.Add(lblTitle);
            this.Controls.Add(headerPanel);

            Label lblInfo = new Label
            {
                Text = "Sisteme bağlı ve kullanıma hazır olan cihazlardan birini seçin:",
                Top = 70,
                Left = 20,
                AutoSize = true,
                ForeColor = fgText,
                Font = new Font("Segoe UI", 9)
            };
            this.Controls.Add(lblInfo);

            listBoxScanners = new ListBox
            {
                Top = 100,
                Left = 20,
                Width = 390,
                Height = 110,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = isDarkMode ? Color.FromArgb(60, 60, 60) : Color.White,
                ForeColor = fgText
            };
            this.Controls.Add(listBoxScanners);

            btnSelect = new Button
            {
                Text = "✔ Cihazı Onayla",
                Top = 225,
                Left = 20,
                Width = 190,
                Height = 40,
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(39, 174, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSelect.FlatAppearance.BorderSize = 0;
            btnSelect.Click += BtnSelect_Click;
            this.Controls.Add(btnSelect);

            btnCancel = new Button
            {
                Text = "✖ İptal Et",
                Top = 225,
                Left = 220,
                Width = 190,
                Height = 40,
                DialogResult = DialogResult.Cancel,
                BackColor = Color.FromArgb(231, 76, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            this.Controls.Add(btnCancel);

            LoadScanners();
        }

        private void LoadScanners()
        {
            try
            {
                var deviceManager = new WIA.DeviceManager();
                foreach (WIA.DeviceInfo info in deviceManager.DeviceInfos)
                {
                    if (info.Type == WIA.WiaDeviceType.ScannerDeviceType)
                    {
                        listBoxScanners.Items.Add(info.Properties["Name"].get_Value().ToString());
                    }
                }
                if (listBoxScanners.Items.Count > 0) listBoxScanners.SelectedIndex = 0;
                else listBoxScanners.Items.Add("Bağlı Cihaz Bulunamadı");
            }
            catch { listBoxScanners.Items.Add("WIA Servis Bağlantı Hatası"); }
        }

        private void BtnSelect_Click(object sender, EventArgs e)
        {
            if (listBoxScanners.SelectedItem != null && !listBoxScanners.SelectedItem.ToString().Contains("Bulunamadı") && !listBoxScanners.SelectedItem.ToString().Contains("Hatası"))
            {
                SelectedScannerName = listBoxScanners.SelectedItem.ToString();
            }
            else
            {
                MessageBox.Show("Lütfen listeden geçerli, donanımsal bir tarayıcı seçin.", "Geçersiz Seçim", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
            }
        }
    }
}