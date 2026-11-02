namespace ViaManager;

partial class FormCookieResult
{
    private System.ComponentModel.IContainer components = null!;
    private Panel panelHeader = null!;
    private Label lblTitle = null!;
    private Label lblSubtitle = null!;
    private Button btnCopyAll = null!;
    private TextBox txtCookies = null!;
    private Panel panelBottom = null!;
    private Label lblCount = null!;
    private Button btnClose = null!;

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
        this.btnCopyAll = new Button();
        this.txtCookies = new TextBox();
        this.panelBottom = new Panel();
        this.lblCount = new Label();
        this.btnClose = new Button();

        this.panelHeader.SuspendLayout();
        this.panelBottom.SuspendLayout();
        this.SuspendLayout();

        // 
        // panelHeader
        // 
        this.panelHeader.BackColor = Color.FromArgb(241, 245, 249);
        this.panelHeader.Controls.Add(this.btnCopyAll);
        this.panelHeader.Controls.Add(this.lblTitle);
        this.panelHeader.Controls.Add(this.lblSubtitle);
        this.panelHeader.Dock = DockStyle.Top;
        this.panelHeader.Height = 65;
        this.panelHeader.Padding = new Padding(15, 10, 15, 8);

        // 
        // lblTitle
        // 
        this.lblTitle.AutoSize = true;
        this.lblTitle.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
        this.lblTitle.ForeColor = Color.FromArgb(15, 23, 42);
        this.lblTitle.Location = new Point(12, 10);
        this.lblTitle.Text = "🔑 Kết Quả Cookie Page";

        // 
        // lblSubtitle
        // 
        this.lblSubtitle.AutoSize = true;
        this.lblSubtitle.Font = new Font("Segoe UI", 9F);
        this.lblSubtitle.ForeColor = Color.FromArgb(71, 85, 105);
        this.lblSubtitle.Location = new Point(14, 35);
        this.lblSubtitle.Text = "Chuỗi Cookie Page hoàn chỉnh theo định dạng chuẩn: c_user; datr; dpr; ps_l; ps_n; sb; xs; i_user; fr; wd";

        // 
        // btnCopyAll
        // 
        this.btnCopyAll.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.btnCopyAll.BackColor = Color.FromArgb(16, 124, 65);
        this.btnCopyAll.FlatAppearance.BorderSize = 0;
        this.btnCopyAll.FlatStyle = FlatStyle.Flat;
        this.btnCopyAll.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        this.btnCopyAll.ForeColor = Color.White;
        this.btnCopyAll.Location = new Point(780, 12);
        this.btnCopyAll.Size = new Size(160, 40);
        this.btnCopyAll.Text = "📋 Copy Tất Cả";
        this.btnCopyAll.UseVisualStyleBackColor = false;
        this.btnCopyAll.Cursor = Cursors.Hand;
        this.btnCopyAll.Click += new EventHandler(this.btnCopyAll_Click);

        // 
        // txtCookies
        // 
        this.txtCookies.BackColor = Color.FromArgb(255, 255, 255);
        this.txtCookies.BorderStyle = BorderStyle.None;
        this.txtCookies.Dock = DockStyle.Fill;
        this.txtCookies.Font = new Font("Consolas", 9.75F);
        this.txtCookies.Location = new Point(0, 65);
        this.txtCookies.Multiline = true;
        this.txtCookies.Name = "txtCookies";
        this.txtCookies.ReadOnly = true;
        this.txtCookies.ScrollBars = ScrollBars.Both;
        this.txtCookies.Size = new Size(954, 435);
        this.txtCookies.WordWrap = false;

        // 
        // panelBottom
        // 
        this.panelBottom.BackColor = Color.FromArgb(241, 245, 249);
        this.panelBottom.Controls.Add(this.lblCount);
        this.panelBottom.Controls.Add(this.btnClose);
        this.panelBottom.Dock = DockStyle.Bottom;
        this.panelBottom.Height = 50;
        this.panelBottom.Padding = new Padding(15, 8, 15, 8);

        // 
        // lblCount
        // 
        this.lblCount.AutoSize = true;
        this.lblCount.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        this.lblCount.ForeColor = Color.FromArgb(15, 23, 42);
        this.lblCount.Location = new Point(12, 16);
        this.lblCount.Text = "Tổng số Page: 0 dòng";

        // 
        // btnClose
        // 
        this.btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.btnClose.BackColor = Color.FromArgb(203, 213, 225);
        this.btnClose.DialogResult = DialogResult.OK;
        this.btnClose.FlatAppearance.BorderSize = 0;
        this.btnClose.FlatStyle = FlatStyle.Flat;
        this.btnClose.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        this.btnClose.ForeColor = Color.FromArgb(30, 41, 59);
        this.btnClose.Location = new Point(840, 8);
        this.btnClose.Size = new Size(100, 34);
        this.btnClose.Text = "Đóng";
        this.btnClose.UseVisualStyleBackColor = false;
        this.btnClose.Cursor = Cursors.Hand;
        this.btnClose.Click += new EventHandler(this.btnClose_Click);

        // 
        // FormCookieResult
        // 
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(954, 550);
        this.Controls.Add(this.txtCookies);
        this.Controls.Add(this.panelBottom);
        this.Controls.Add(this.panelHeader);
        this.Font = new Font("Segoe UI", 9F);
        this.MinimumSize = new Size(750, 400);
        this.Name = "FormCookieResult";
        this.StartPosition = FormStartPosition.CenterParent;
        this.Text = "Kết Quả Cookie Page";

        this.panelHeader.ResumeLayout(false);
        this.panelHeader.PerformLayout();
        this.panelBottom.ResumeLayout(false);
        this.panelBottom.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
