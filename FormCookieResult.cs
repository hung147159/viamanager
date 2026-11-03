namespace ViaManager;

public partial class FormCookieResult : Form
{
    private readonly string _viaUid;
    private readonly string _bm;
    private readonly List<string> _cookieLines;

    public FormCookieResult(List<string> cookieLines) : this("", "", cookieLines)
    {
    }

    public FormCookieResult(string viaUid, string bm, List<string> cookieLines)
    {
        InitializeComponent();
        _viaUid = viaUid;
        _bm = bm;

        // Bỏ hoàn toàn tiền tố ID Page hoặc bất kỳ nội dung nào trước dấu '|', chỉ giữ lại chuỗi cookie chuẩn
        var cleanedLines = (cookieLines ?? new List<string>()).Select(line =>
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            int pipeIdx = line.IndexOf('|');
            if (pipeIdx >= 0)
            {
                string afterPipe = line.Substring(pipeIdx + 1).Trim();
                if (afterPipe.Contains('='))
                    return afterPipe;
            }
            return line.Trim();
        }).Where(l => !string.IsNullOrEmpty(l)).ToList();

        _cookieLines = cleanedLines;

        Text = $"Kết Quả Cookie Page - [{_viaUid}]";
        lblTitle.Text = $"🔑 Kết Quả Cookie Page - UID: {_viaUid}";
        lblSubtitle.Text = $"Đã phân bổ {_cookieLines.Count} Page không trùng lặp cho Via {_viaUid} thuộc nhóm BM '{_bm}'.";
        lblCount.Text = $"Tổng cộng: {_cookieLines.Count} Page (Tối đa 10 Page)";

        txtCookies.Text = string.Join(Environment.NewLine, _cookieLines);
    }

    private void btnCopyAll_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtCookies.Text))
        {
            MessageBox.Show("Không có nội dung để copy!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Clipboard.SetText(txtCookies.Text);
            btnCopyAll.Text = "✅ Đã Copy Xong!";
            btnCopyAll.BackColor = Color.FromArgb(14, 116, 144);

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += (s, ev) =>
            {
                btnCopyAll.Text = "📋 Copy Tất Cả";
                btnCopyAll.BackColor = Color.FromArgb(16, 124, 65);
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi sao chép: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnClose_Click(object? sender, EventArgs e)
    {
        Close();
    }
}
