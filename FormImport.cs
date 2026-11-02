namespace ViaManager;

public partial class FormImport : Form
{
    private readonly string _connectionString;
    public int ImportedCount { get; private set; }

    public FormImport(string connectionString)
    {
        InitializeComponent();
        _connectionString = connectionString;
    }

    private void FormImport_Load(object? sender, EventArgs e)
    {
        cboTrangThai.SelectedIndex = 0; // "live"
        cboVaiTro.SelectedIndex = 0;    // "Run"
        cboBM.Text = "Bmv1";

        // Tự động kiểm tra clipboard: nếu có sẵn dữ liệu chứa ký tự '|', điền trước cho người dùng
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text) && text.Contains('|'))
                {
                    txtInput.Text = text;
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi truy cập clipboard nếu đang bận
        }
    }

    private void btnOpenFile_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Chọn file danh sách Nick (.txt)",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                txtInput.Text = File.ReadAllText(ofd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi đọc file: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void btnPasteClipboard_Click(object? sender, EventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                txtInput.Text = Clipboard.GetText();
            }
            else
            {
                MessageBox.Show("Clipboard hiện không có văn bản!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi clipboard: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnClear_Click(object? sender, EventArgs e)
    {
        txtInput.Clear();
    }

    private void txtInput_TextChanged(object? sender, EventArgs e)
    {
        string text = txtInput.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            lblStats.Text = "Tổng số dòng: 0 | Hợp lệ: 0";
            return;
        }

        string[] lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        int valid = 0;
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var item = AccountParser.ParseLine(line);
                if (item != null && !string.IsNullOrWhiteSpace(item.ID))
                {
                    valid++;
                }
            }
        }

        lblStats.Text = $"Tổng số dòng: {lines.Length} | Hợp lệ: {valid} nick";
    }

    private void btnImport_Click(object? sender, EventArgs e)
    {
        string rawText = txtInput.Text;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            MessageBox.Show("Vui lòng dán dữ liệu hoặc chọn file trước khi bấm Import!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string defaultTrangThai = cboTrangThai.SelectedItem?.ToString() ?? "live";
        string defaultVaiTro = cboVaiTro.SelectedItem?.ToString() ?? "Run";
        string defaultBM = string.IsNullOrWhiteSpace(cboBM.Text) ? "Bmv1" : cboBM.Text.Trim();

        string[] lines = rawText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        var accountList = new List<AccountItem>();

        foreach (var line in lines)
        {
            var item = AccountParser.ParseLine(line, defaultTrangThai, defaultVaiTro, defaultBM);
            if (item != null && !string.IsNullOrWhiteSpace(item.ID))
            {
                accountList.Add(item);
            }
        }

        if (accountList.Count == 0)
        {
            MessageBox.Show("Không tìm thấy dòng tài khoản hợp lệ nào trong nội dung!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnImport.Enabled = false;
        Cursor = Cursors.WaitCursor;

        try
        {
            int saved = AccountRepository.BatchUpsert(_connectionString, accountList);
            ImportedCount = saved;

            MessageBox.Show($"Đã import và cập nhật thành công {saved} tài khoản vào SQLite!",
                "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi import vào SQLite: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnImport.Enabled = true;
            Cursor = Cursors.Default;
        }
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }
}
