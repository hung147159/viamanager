namespace ViaManager;

partial class FormImport
{
    private System.ComponentModel.IContainer components = null!;
    private Panel panelHeader = null!;
    private Label lblTitle = null!;
    private Label lblSubtitle = null!;
    private Panel panelConfig = null!;
    private Label lblTrangThai = null!;
    private ComboBox cboTrangThai = null!;
    private Label lblVaiTro = null!;
    private ComboBox cboVaiTro = null!;
    private Label lblBM = null!;
    private ComboBox cboBM = null!;
    private Button btnOpenFile = null!;
    private Button btnPasteClipboard = null!;
    private Button btnClear = null!;
    private TextBox txtInput = null!;
    private Panel panelBottom = null!;
    private Label lblStats = null!;
    private Button btnImport = null!;
    private Button btnCancel = null!;

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
        this.panelHeader = new Panel();
        this.lblTitle = new Label();
        this.lblSubtitle = new Label();
        this.panelConfig = new Panel();
        this.lblTrangThai = new Label();
        this.cboTrangThai = new ComboBox();
        this.lblVaiTro = new Label();
        this.cboVaiTro = new ComboBox();
        this.lblBM = new Label();
        this.cboBM = new ComboBox();
        this.btnOpenFile = new Button();
        this.btnPasteClipboard = new Button();
        this.btnClear = new Button();
        this.txtInput = new TextBox();
        this.panelBottom = new Panel();
        this.lblStats = new Label();
        this.btnImport = new Button();
        this.btnCancel = new Button();

        this.panelHeader.SuspendLayout();
        this.panelConfig.SuspendLayout();
        this.panelBottom.SuspendLayout();
        this.SuspendLayout();

        // 
        // panelHeader
        // 
        this.panelHeader.BackColor = Color.FromArgb(241, 245, 249);
        this.panelHeader.Controls.Add(this.lblTitle);
        this.panelHeader.Controls.Add(this.lblSubtitle);
        this.panelHeader.Dock = DockStyle.Top;
        this.panelHeader.Height = 65;
        this.panelHeader.Padding = new Padding(15, 10, 15, 8);

        // 
        // lblTitle
        // 
        this.lblTitle.AutoSize = true;
        this.lblTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        this.lblTitle.ForeColor = Color.FromArgb(15, 23, 42);
        this.lblTitle.Location = new Point(14, 10);
        this.lblTitle.Text = "📥 Import Nick Facebook / Via vào SQLite";

        // 
        // lblSubtitle
        // 
        this.lblSubtitle.AutoSize = true;
        this.lblSubtitle.Font = new Font("Segoe UI", 9F);
        this.lblSubtitle.ForeColor = Color.FromArgb(71, 85, 105);
        this.lblSubtitle.Location = new Point(15, 36);
        this.lblSubtitle.Text = "Chuỗi ngăn cách '|': UID | PASS | Ma2FA | Cookie | Token | MAIL | PASS_MAIL (Tự động nhận diện thông minh nếu thiếu cột)";

        // 
        // panelConfig
        // 
        this.panelConfig.BackColor = Color.FromArgb(248, 250, 252);
        this.panelConfig.BorderStyle = BorderStyle.FixedSingle;
        this.panelConfig.Controls.Add(this.lblTrangThai);
        this.panelConfig.Controls.Add(this.cboTrangThai);
        this.panelConfig.Controls.Add(this.lblVaiTro);
        this.panelConfig.Controls.Add(this.cboVaiTro);
        this.panelConfig.Controls.Add(this.lblBM);
        this.panelConfig.Controls.Add(this.cboBM);
        this.panelConfig.Controls.Add(this.btnPasteClipboard);
        this.panelConfig.Controls.Add(this.btnOpenFile);
        this.panelConfig.Controls.Add(this.btnClear);
        this.panelConfig.Dock = DockStyle.Top;
        this.panelConfig.Height = 52;
        this.panelConfig.Padding = new Padding(10, 8, 10, 8);

        // 
        // lblTrangThai
        // 
        this.lblTrangThai.AutoSize = true;
        this.lblTrangThai.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        this.lblTrangThai.Location = new Point(12, 17);
        this.lblTrangThai.Text = "Trạng thái:";

        // 
        // cboTrangThai
        // 
        this.cboTrangThai.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cboTrangThai.Font = new Font("Segoe UI", 9F);
        this.cboTrangThai.FormattingEnabled = true;
        this.cboTrangThai.Items.AddRange(new object[] { "live", "check", "die" });
        this.cboTrangThai.Location = new Point(80, 14);
        this.cboTrangThai.Size = new Size(85, 23);

        // 
        // lblVaiTro
        // 
        this.lblVaiTro.AutoSize = true;
        this.lblVaiTro.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        this.lblVaiTro.Location = new Point(175, 17);
        this.lblVaiTro.Text = "Vai trò:";

        // 
        // cboVaiTro
        // 
        this.cboVaiTro.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cboVaiTro.Font = new Font("Segoe UI", 9F);
        this.cboVaiTro.FormattingEnabled = true;
        this.cboVaiTro.Items.AddRange(new object[] { "Run", "Reg", "Lock" });
        this.cboVaiTro.Location = new Point(227, 14);
        this.cboVaiTro.Size = new Size(85, 23);

        // 
        // lblBM
        // 
        this.lblBM.AutoSize = true;
        this.lblBM.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        this.lblBM.Location = new Point(322, 17);
        this.lblBM.Text = "Tên BM:";

        // 
        // cboBM
        // 
        this.cboBM.Font = new Font("Segoe UI", 9F);
        this.cboBM.FormattingEnabled = true;
        this.cboBM.Items.AddRange(new object[] { "Bmv1", "bmv2", "bmv3", "bmv4", "bmv5" });
        this.cboBM.Location = new Point(376, 14);
        this.cboBM.Size = new Size(95, 23);

        // 
        // btnPasteClipboard
        // 
        this.btnPasteClipboard.BackColor = Color.FromArgb(16, 124, 65);
        this.btnPasteClipboard.FlatAppearance.BorderSize = 0;
        this.btnPasteClipboard.FlatStyle = FlatStyle.Flat;
        this.btnPasteClipboard.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        this.btnPasteClipboard.ForeColor = Color.White;
        this.btnPasteClipboard.Location = new Point(488, 11);
        this.btnPasteClipboard.Size = new Size(130, 28);
        this.btnPasteClipboard.Text = "📋 Dán Clipboard";
        this.btnPasteClipboard.UseVisualStyleBackColor = false;
        this.btnPasteClipboard.Cursor = Cursors.Hand;
        this.btnPasteClipboard.Click += new EventHandler(this.btnPasteClipboard_Click);

        // 
        // btnOpenFile
        // 
        this.btnOpenFile.BackColor = Color.FromArgb(0, 120, 212);
        this.btnOpenFile.FlatAppearance.BorderSize = 0;
        this.btnOpenFile.FlatStyle = FlatStyle.Flat;
        this.btnOpenFile.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        this.btnOpenFile.ForeColor = Color.White;
        this.btnOpenFile.Location = new Point(626, 11);
        this.btnOpenFile.Size = new Size(115, 28);
        this.btnOpenFile.Text = "📂 Chọn file .txt";
        this.btnOpenFile.UseVisualStyleBackColor = false;
        this.btnOpenFile.Cursor = Cursors.Hand;
        this.btnOpenFile.Click += new EventHandler(this.btnOpenFile_Click);

        // 
        // btnClear
        // 
        this.btnClear.BackColor = Color.FromArgb(203, 213, 225);
        this.btnClear.FlatAppearance.BorderSize = 0;
        this.btnClear.FlatStyle = FlatStyle.Flat;
        this.btnClear.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        this.btnClear.ForeColor = Color.FromArgb(30, 41, 59);
        this.btnClear.Location = new Point(749, 11);
        this.btnClear.Size = new Size(85, 28);
        this.btnClear.Text = "🧹 Xóa trắng";
        this.btnClear.UseVisualStyleBackColor = false;
        this.btnClear.Cursor = Cursors.Hand;
        this.btnClear.Click += new EventHandler(this.btnClear_Click);

        // 
        // txtInput
        // 
        this.txtInput.BorderStyle = BorderStyle.None;
        this.txtInput.Dock = DockStyle.Fill;
        this.txtInput.Font = new Font("Consolas", 9.75F);
        this.txtInput.Location = new Point(0, 117);
        this.txtInput.Multiline = true;
        this.txtInput.Name = "txtInput";
        this.txtInput.ScrollBars = ScrollBars.Both;
        this.txtInput.Size = new Size(920, 415);
        this.txtInput.WordWrap = false;
        this.txtInput.TextChanged += new EventHandler(this.txtInput_TextChanged);

        // 
        // panelBottom
        // 
        this.panelBottom.BackColor = Color.FromArgb(241, 245, 249);
        this.panelBottom.Controls.Add(this.lblStats);
        this.panelBottom.Controls.Add(this.btnImport);
        this.panelBottom.Controls.Add(this.btnCancel);
        this.panelBottom.Dock = DockStyle.Bottom;
        this.panelBottom.Height = 58;
        this.panelBottom.Padding = new Padding(15, 10, 15, 10);

        // 
        // lblStats
        // 
        this.lblStats.AutoSize = true;
        this.lblStats.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        this.lblStats.ForeColor = Color.FromArgb(15, 23, 42);
        this.lblStats.Location = new Point(14, 20);
        this.lblStats.Text = "Tổng số dòng: 0 | Hợp lệ: 0";

        // 
        // btnImport
        // 
        this.btnImport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.btnImport.BackColor = Color.FromArgb(16, 124, 65);
        this.btnImport.FlatAppearance.BorderSize = 0;
        this.btnImport.FlatStyle = FlatStyle.Flat;
        this.btnImport.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        this.btnImport.ForeColor = Color.White;
        this.btnImport.Location = new Point(660, 10);
        this.btnImport.Size = new Size(150, 38);
        this.btnImport.Text = "🚀 Bắt đầu Import";
        this.btnImport.UseVisualStyleBackColor = false;
        this.btnImport.Cursor = Cursors.Hand;
        this.btnImport.Click += new EventHandler(this.btnImport_Click);

        // 
        // btnCancel
        // 
        this.btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.btnCancel.BackColor = Color.FromArgb(203, 213, 225);
        this.btnCancel.DialogResult = DialogResult.Cancel;
        this.btnCancel.FlatAppearance.BorderSize = 0;
        this.btnCancel.FlatStyle = FlatStyle.Flat;
        this.btnCancel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        this.btnCancel.ForeColor = Color.FromArgb(30, 41, 59);
        this.btnCancel.Location = new Point(818, 10);
        this.btnCancel.Size = new Size(90, 38);
        this.btnCancel.Text = "Đóng";
        this.btnCancel.UseVisualStyleBackColor = false;
        this.btnCancel.Cursor = Cursors.Hand;
        this.btnCancel.Click += new EventHandler(this.btnCancel_Click);

        // 
        // FormImport
        // 
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(920, 590);
        this.Controls.Add(this.txtInput);
        this.Controls.Add(this.panelBottom);
        this.Controls.Add(this.panelConfig);
        this.Controls.Add(this.panelHeader);
        this.Font = new Font("Segoe UI", 9F);
        this.MinimumSize = new Size(800, 500);
        this.Name = "FormImport";
        this.StartPosition = FormStartPosition.CenterParent;
        this.Text = "Import Danh Sách Nick - ViaManager";
        this.Load += new EventHandler(this.FormImport_Load);

        this.panelHeader.ResumeLayout(false);
        this.panelHeader.PerformLayout();
        this.panelConfig.ResumeLayout(false);
        this.panelConfig.PerformLayout();
        this.panelBottom.ResumeLayout(false);
        this.panelBottom.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
