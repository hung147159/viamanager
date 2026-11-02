namespace ViaManager;

partial class Form1
{
    private System.ComponentModel.IContainer components = null!;
    private DataGridView dgvAccounts = null!;
    private Panel panelTop = null!;
    private Button btnImport = null!;
    private Button btnPasteSheet = null!;
    private Button btnSave = null!;
    private Button btnDelete = null!;
    private Button btnGetCookie = null!;
    private Button btnScanTotalPages = null!;
    private Button btnCampaign = null!;
    private Button btnReload = null!;
    private TextBox txtSearch = null!;
    private Label lblSearch = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private ToolStripStatusLabel countLabel = null!;

    private DataGridViewButtonColumn colBtnRun = null!;
    private DataGridViewTextBoxColumn colID = null!;
    private DataGridViewTextBoxColumn colPASS = null!;
    private DataGridViewTextBoxColumn colMa2FA = null!;
    private DataGridViewTextBoxColumn colCookie = null!;
    private DataGridViewTextBoxColumn colToken = null!;
    private DataGridViewTextBoxColumn colMAIL = null!;
    private DataGridViewTextBoxColumn colPASS_MAIL = null!;
    private DataGridViewComboBoxColumn colTrangThai = null!;
    private DataGridViewComboBoxColumn colVaiTro = null!;
    private DataGridViewTextBoxColumn colPageCount = null!;
    private DataGridViewTextBoxColumn colTenTaiKhoan = null!;
    private DataGridViewComboBoxColumn colBM = null!;
    private DataGridViewTextBoxColumn colBM_Mail = null!;
    private DataGridViewTextBoxColumn colLastRunTime = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.panelTop = new Panel();
        this.btnImport = new Button();
        this.btnPasteSheet = new Button();
        this.btnSave = new Button();
        this.btnDelete = new Button();
        this.btnGetCookie = new Button();
        this.btnScanTotalPages = new Button();
        this.btnCampaign = new Button();
        this.btnReload = new Button();
        this.txtSearch = new TextBox();
        this.lblSearch = new Label();

        this.statusStrip = new StatusStrip();
        this.statusLabel = new ToolStripStatusLabel();
        this.countLabel = new ToolStripStatusLabel();

        this.dgvAccounts = new DataGridView();

        this.colBtnRun = new DataGridViewButtonColumn();
        this.colID = new DataGridViewTextBoxColumn();
        this.colPASS = new DataGridViewTextBoxColumn();
        this.colMa2FA = new DataGridViewTextBoxColumn();
        this.colCookie = new DataGridViewTextBoxColumn();
        this.colToken = new DataGridViewTextBoxColumn();
        this.colMAIL = new DataGridViewTextBoxColumn();
        this.colPASS_MAIL = new DataGridViewTextBoxColumn();
        this.colTrangThai = new DataGridViewComboBoxColumn();
        this.colVaiTro = new DataGridViewComboBoxColumn();
        this.colPageCount = new DataGridViewTextBoxColumn();
        this.colTenTaiKhoan = new DataGridViewTextBoxColumn();
        this.colBM = new DataGridViewComboBoxColumn();
        this.colBM_Mail = new DataGridViewTextBoxColumn();
        this.colLastRunTime = new DataGridViewTextBoxColumn();

        this.panelTop.SuspendLayout();
        this.statusStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)(this.dgvAccounts)).BeginInit();
        this.SuspendLayout();

        // 
        // panelTop
        // 
        this.panelTop.BackColor = Color.FromArgb(248, 250, 252);
        this.panelTop.Controls.Add(this.btnImport);
        this.panelTop.Controls.Add(this.btnPasteSheet);
        this.panelTop.Controls.Add(this.btnSave);
        this.panelTop.Controls.Add(this.btnDelete);
        this.panelTop.Controls.Add(this.btnGetCookie);
        this.panelTop.Controls.Add(this.btnScanTotalPages);
        this.panelTop.Controls.Add(this.btnCampaign);
        this.panelTop.Controls.Add(this.btnReload);
        this.panelTop.Controls.Add(this.txtSearch);
        this.panelTop.Controls.Add(this.lblSearch);
        this.panelTop.Dock = DockStyle.Top;
        this.panelTop.Height = 56;
        this.panelTop.Padding = new Padding(10, 8, 10, 8);

        // 
        // btnImport
        // 
        this.btnImport.BackColor = Color.FromArgb(16, 124, 65);
        this.btnImport.FlatAppearance.BorderSize = 0;
        this.btnImport.FlatStyle = FlatStyle.Flat;
        this.btnImport.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnImport.ForeColor = Color.White;
        this.btnImport.Location = new Point(10, 10);
        this.btnImport.Size = new Size(150, 36);
        this.btnImport.Text = "📥 Import Nick (|)";
        this.btnImport.UseVisualStyleBackColor = false;
        this.btnImport.Cursor = Cursors.Hand;
        this.btnImport.Click += new EventHandler(this.btnImport_Click);

        // 
        // btnPasteSheet
        // 
        this.btnPasteSheet.BackColor = Color.FromArgb(37, 99, 235);
        this.btnPasteSheet.FlatAppearance.BorderSize = 0;
        this.btnPasteSheet.FlatStyle = FlatStyle.Flat;
        this.btnPasteSheet.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnPasteSheet.ForeColor = Color.White;
        this.btnPasteSheet.Location = new Point(165, 10);
        this.btnPasteSheet.Size = new Size(155, 36);
        this.btnPasteSheet.Text = "📋 Dán Clipboard";
        this.btnPasteSheet.UseVisualStyleBackColor = false;
        this.btnPasteSheet.Cursor = Cursors.Hand;
        this.btnPasteSheet.Click += new EventHandler(this.btnPasteSheet_Click);

        // 
        // btnSave
        // 
        this.btnSave.BackColor = Color.FromArgb(14, 116, 144);
        this.btnSave.FlatAppearance.BorderSize = 0;
        this.btnSave.FlatStyle = FlatStyle.Flat;
        this.btnSave.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnSave.ForeColor = Color.White;
        this.btnSave.Location = new Point(325, 10);
        this.btnSave.Size = new Size(115, 36);
        this.btnSave.Text = "💾 Lưu đổi";
        this.btnSave.UseVisualStyleBackColor = false;
        this.btnSave.Cursor = Cursors.Hand;
        this.btnSave.Click += new EventHandler(this.btnSave_Click);

        // 
        // btnDelete
        // 
        this.btnDelete.BackColor = Color.FromArgb(220, 38, 38);
        this.btnDelete.FlatAppearance.BorderSize = 0;
        this.btnDelete.FlatStyle = FlatStyle.Flat;
        this.btnDelete.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnDelete.ForeColor = Color.White;
        this.btnDelete.Location = new Point(445, 10);
        this.btnDelete.Size = new Size(95, 36);
        this.btnDelete.Text = "🗑️ Xóa dòng";
        this.btnDelete.UseVisualStyleBackColor = false;
        this.btnDelete.Cursor = Cursors.Hand;
        this.btnDelete.Click += new EventHandler(this.btnDelete_Click);

        // 
        // btnGetCookie
        // 
        this.btnGetCookie.BackColor = Color.FromArgb(126, 34, 206);
        this.btnGetCookie.FlatAppearance.BorderSize = 0;
        this.btnGetCookie.FlatStyle = FlatStyle.Flat;
        this.btnGetCookie.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnGetCookie.ForeColor = Color.White;
        this.btnGetCookie.Location = new Point(545, 10);
        this.btnGetCookie.Size = new Size(130, 36);
        this.btnGetCookie.Text = "🚀 Lấy Cookie";
        this.btnGetCookie.UseVisualStyleBackColor = false;
        this.btnGetCookie.Cursor = Cursors.Hand;
        this.btnGetCookie.Click += new EventHandler(this.btnGetCookie_Click);

        // 
        // btnScanTotalPages
        // 
        this.btnScanTotalPages.BackColor = Color.FromArgb(13, 148, 136);
        this.btnScanTotalPages.FlatAppearance.BorderSize = 0;
        this.btnScanTotalPages.FlatStyle = FlatStyle.Flat;
        this.btnScanTotalPages.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnScanTotalPages.ForeColor = Color.White;
        this.btnScanTotalPages.Location = new Point(685, 10);
        this.btnScanTotalPages.Size = new Size(150, 36);
        this.btnScanTotalPages.Text = "📊 Quét Tổng Page";
        this.btnScanTotalPages.UseVisualStyleBackColor = false;
        this.btnScanTotalPages.Cursor = Cursors.Hand;
        this.btnScanTotalPages.Click += new EventHandler(this.btnScanTotalPages_Click);

        // 
        // btnCampaign
        // 
        this.btnCampaign.BackColor = Color.FromArgb(217, 119, 6);
        this.btnCampaign.FlatAppearance.BorderSize = 0;
        this.btnCampaign.FlatStyle = FlatStyle.Flat;
        this.btnCampaign.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnCampaign.ForeColor = Color.White;
        this.btnCampaign.Location = new Point(845, 10);
        this.btnCampaign.Size = new Size(155, 36);
        this.btnCampaign.Text = "Chạy 0 Luồng";
        this.btnCampaign.UseVisualStyleBackColor = false;
        this.btnCampaign.Cursor = Cursors.Hand;
        this.btnCampaign.Click += new EventHandler(this.btnCampaign_Click);

        // 
        // btnReload
        // 
        this.btnReload.BackColor = Color.FromArgb(71, 85, 105);
        this.btnReload.FlatAppearance.BorderSize = 0;
        this.btnReload.FlatStyle = FlatStyle.Flat;
        this.btnReload.Font = new Font("Segoe UI", 9.25F, FontStyle.Bold);
        this.btnReload.ForeColor = Color.White;
        this.btnReload.Location = new Point(1010, 10);
        this.btnReload.Size = new Size(85, 36);
        this.btnReload.Text = "🔄 Tải lại";
        this.btnReload.UseVisualStyleBackColor = false;
        this.btnReload.Cursor = Cursors.Hand;
        this.btnReload.Click += new EventHandler(this.btnReload_Click);

        // 
        // lblSearch
        // 
        this.lblSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.lblSearch.AutoSize = true;
        this.lblSearch.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        this.lblSearch.ForeColor = Color.FromArgb(71, 85, 105);
        this.lblSearch.Location = new Point(1210, 20);
        this.lblSearch.Text = "🔍 Lọc:";

        // 
        // txtSearch
        // 
        this.txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.txtSearch.Font = new Font("Segoe UI", 9.5F);
        this.txtSearch.Location = new Point(1260, 16);
        this.txtSearch.Name = "txtSearch";
        this.txtSearch.PlaceholderText = "UID, Mail, BM, Vai trò...";
        this.txtSearch.Size = new Size(220, 24);
        this.txtSearch.TextChanged += new EventHandler(this.txtSearch_TextChanged);

        // 
        // statusStrip
        // 
        this.statusStrip.BackColor = Color.FromArgb(241, 245, 249);
        this.statusStrip.Font = new Font("Segoe UI", 9F);
        this.statusStrip.Items.AddRange(new ToolStripItem[] {
            this.statusLabel,
            this.countLabel
        });
        this.statusStrip.Location = new Point(0, 718);
        this.statusStrip.Name = "statusStrip";
        this.statusStrip.Size = new Size(1520, 24);
        this.statusStrip.TabIndex = 2;

        // 
        // statusLabel
        // 
        this.statusLabel.ForeColor = Color.FromArgb(30, 41, 59);
        this.statusLabel.Name = "statusLabel";
        this.statusLabel.Size = new Size(1205, 19);
        this.statusLabel.Spring = true;
        this.statusLabel.Text = "Sẵn sàng.";
        this.statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        // 
        // countLabel
        // 
        this.countLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        this.countLabel.ForeColor = Color.FromArgb(15, 23, 42);
        this.countLabel.Name = "countLabel";
        this.countLabel.Size = new Size(300, 19);
        this.countLabel.Text = "Tổng: 0 | Live: 0 | Running: 0 | Lock: 0";
        this.countLabel.TextAlign = ContentAlignment.MiddleRight;

        // 
        // dgvAccounts
        // 
        this.dgvAccounts.AllowUserToAddRows = true;
        this.dgvAccounts.AllowUserToDeleteRows = true;
        this.dgvAccounts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        this.dgvAccounts.BackgroundColor = Color.White;
        this.dgvAccounts.BorderStyle = BorderStyle.None;
        this.dgvAccounts.CellBorderStyle = DataGridViewCellBorderStyle.Single;
        this.dgvAccounts.ColumnHeadersHeight = 36;
        this.dgvAccounts.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        this.dgvAccounts.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        this.dgvAccounts.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(30, 41, 59);
        this.dgvAccounts.DefaultCellStyle.Font = new Font("Segoe UI", 9.5F);
        this.dgvAccounts.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        this.dgvAccounts.DefaultCellStyle.SelectionForeColor = Color.Black;
        this.dgvAccounts.EnableHeadersVisualStyles = false;
        this.dgvAccounts.GridColor = Color.FromArgb(226, 232, 240);
        this.dgvAccounts.Dock = DockStyle.Fill;
        this.dgvAccounts.RowTemplate.Height = 32;
        this.dgvAccounts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.dgvAccounts.MultiSelect = true;
        this.dgvAccounts.EditMode = DataGridViewEditMode.EditOnEnter;

        // 
        // colBtnRun (Cột nút bấm Chạy nằm đầu dòng)
        // 
        this.colBtnRun.Name = "colBtnRun";
        this.colBtnRun.HeaderText = "Chạy";
        this.colBtnRun.Text = "▶ Chạy";
        this.colBtnRun.UseColumnTextForButtonValue = true;
        this.colBtnRun.FlatStyle = FlatStyle.Flat;
        this.colBtnRun.Width = 75;
        this.colBtnRun.DefaultCellStyle.BackColor = Color.FromArgb(16, 124, 65);
        this.colBtnRun.DefaultCellStyle.ForeColor = Color.White;
        this.colBtnRun.DefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        this.colBtnRun.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        // Columns configuration
        this.colID.Name = "ID";
        this.colID.HeaderText = "ID (UID)";
        this.colID.DataPropertyName = "ID";
        this.colID.Width = 145;

        this.colPASS.Name = "PASS";
        this.colPASS.HeaderText = "PASS";
        this.colPASS.DataPropertyName = "PASS";
        this.colPASS.Width = 110;

        this.colMa2FA.Name = "Ma2FA";
        this.colMa2FA.HeaderText = "Mã 2FA";
        this.colMa2FA.DataPropertyName = "Ma2FA";
        this.colMa2FA.Width = 140;

        this.colCookie.Name = "Cookie";
        this.colCookie.HeaderText = "Cookie";
        this.colCookie.DataPropertyName = "Cookie";
        this.colCookie.Width = 180;

        this.colToken.Name = "Token";
        this.colToken.HeaderText = "Token";
        this.colToken.DataPropertyName = "Token";
        this.colToken.Width = 180;

        this.colMAIL.Name = "MAIL";
        this.colMAIL.HeaderText = "MAIL";
        this.colMAIL.DataPropertyName = "MAIL";
        this.colMAIL.Width = 180;

        this.colPASS_MAIL.Name = "PASS_MAIL";
        this.colPASS_MAIL.HeaderText = "PASS_MAIL";
        this.colPASS_MAIL.DataPropertyName = "PASS_MAIL";
        this.colPASS_MAIL.Width = 110;

        this.colTrangThai.Name = "TrangThai";
        this.colTrangThai.HeaderText = "Trạng Thái";
        this.colTrangThai.DataPropertyName = "TrangThai";
        this.colTrangThai.Items.AddRange("live", "die", "check", "running", "lock", "");
        this.colTrangThai.FlatStyle = FlatStyle.Flat;
        this.colTrangThai.Width = 100;

        this.colVaiTro.Name = "VaiTro";
        this.colVaiTro.HeaderText = "Vai Trò";
        this.colVaiTro.DataPropertyName = "VaiTro";
        this.colVaiTro.Items.AddRange("Run", "Reg", "Lock", "");
        this.colVaiTro.FlatStyle = FlatStyle.Flat;
        this.colVaiTro.Width = 95;

        // 
        // colPageCount (Nằm cạnh cột Vai Trò)
        // 
        this.colPageCount.Name = "PageCount";
        this.colPageCount.HeaderText = "Số Page";
        this.colPageCount.DataPropertyName = "PageCount";
        this.colPageCount.Width = 85;
        this.colPageCount.ReadOnly = true;
        this.colPageCount.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        this.colPageCount.DefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);

        this.colTenTaiKhoan.Name = "TenTaiKhoan";
        this.colTenTaiKhoan.HeaderText = "Tên Nick";
        this.colTenTaiKhoan.DataPropertyName = "TenTaiKhoan";
        this.colTenTaiKhoan.Width = 130;

        this.colBM.Name = "BM";
        this.colBM.HeaderText = "BM";
        this.colBM.DataPropertyName = "BM";
        this.colBM.Items.AddRange("Bmv1", "bmv2", "bmv3", "bmv4", "bmv5", "");
        this.colBM.FlatStyle = FlatStyle.Flat;
        this.colBM.Width = 95;

        this.colBM_Mail.Name = "BM_Mail";
        this.colBM_Mail.HeaderText = "BM_Mail";
        this.colBM_Mail.DataPropertyName = "BM_Mail";
        this.colBM_Mail.Width = 160;

        this.colLastRunTime.Name = "LastRunTime";
        this.colLastRunTime.HeaderText = "Lần Chạy Cuối";
        this.colLastRunTime.DataPropertyName = "LastRunTime";
        this.colLastRunTime.Width = 145;
        this.colLastRunTime.ReadOnly = true;

        this.dgvAccounts.Columns.AddRange(new DataGridViewColumn[] {
            this.colBtnRun,
            this.colID,
            this.colPASS,
            this.colMa2FA,
            this.colCookie,
            this.colToken,
            this.colMAIL,
            this.colPASS_MAIL,
            this.colTrangThai,
            this.colVaiTro,
            this.colPageCount,
            this.colTenTaiKhoan,
            this.colBM,
            this.colBM_Mail,
            this.colLastRunTime
        });

        this.dgvAccounts.CellFormatting += new DataGridViewCellFormattingEventHandler(this.dgvAccounts_CellFormatting);
        this.dgvAccounts.CellContentClick += new DataGridViewCellEventHandler(this.dgvAccounts_CellContentClick);
        this.dgvAccounts.CellDoubleClick += new DataGridViewCellEventHandler(this.dgvAccounts_CellDoubleClick);
        this.dgvAccounts.DataError += new DataGridViewDataErrorEventHandler(this.dgvAccounts_DataError);
        this.dgvAccounts.CurrentCellDirtyStateChanged += new EventHandler(this.dgvAccounts_CurrentCellDirtyStateChanged);

        // 
        // Form1
        // 
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(1520, 742);
        this.Controls.Add(this.dgvAccounts);
        this.Controls.Add(this.statusStrip);
        this.Controls.Add(this.panelTop);
        this.Name = "Form1";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Text = "ViaManager - Quản lý hồ sơ Via & Đa luồng động theo Page (Playwright + SQLite)";
        this.Load += new EventHandler(this.Form1_Load);

        this.panelTop.ResumeLayout(false);
        this.panelTop.PerformLayout();
        this.statusStrip.ResumeLayout(false);
        this.statusStrip.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)(this.dgvAccounts)).EndInit();
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
