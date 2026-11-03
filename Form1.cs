using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;
using ViaManager.Utilities;

namespace ViaManager;

public partial class Form1 : Form
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly ViaManager.Data.AccountRepository _accountRepository;
    private readonly ViaManager.Data.PageRepository _pageRepository;
    private readonly ViaManager.Services.IBrowserProfileService _browserProfileService;
    private readonly ViaManager.Services.IViaAuthenticationService _viaAuthService;
    private readonly ViaManager.Services.IPageDiscoveryService _pageDiscoveryService;
    private readonly ViaManager.Services.IPageSwitchService _pageSwitchService;
    private readonly ViaManager.Services.IPageVerificationService _pageVerificationService;
    private readonly ViaManager.Services.IPageSessionService _pageSessionService;




    public Form1()
    {
        AllocConsole();
        InitializeComponent();
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "ViaManager - Debug Console";
            Console.WriteLine("================================================================================");
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] - [SYSTEM] Khởi tạo cửa sổ Debug Console thành công.");
            Console.WriteLine("================================================================================");
        }
        catch { }

        _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ViaManager.db");
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30
        };
        _connectionString = csb.ToString();

        _accountRepository = new ViaManager.Data.AccountRepository(_connectionString);
        _pageRepository = new ViaManager.Data.PageRepository(_connectionString);
        _browserProfileService = new ViaManager.Services.BrowserProfileService();
        _viaAuthService = new ViaManager.Services.ViaAuthenticationService();
        _pageDiscoveryService = new ViaManager.Services.PageDiscoveryService();
        _pageSwitchService = new ViaManager.Services.PageSwitchService();
        _pageVerificationService = new ViaManager.Services.PageVerificationService();
        _pageSessionService = new ViaManager.Services.PageSessionService(
            _pageDiscoveryService,
            _pageSwitchService,
            _pageVerificationService,
            _pageRepository,
            _accountRepository);
    }

    private void Form1_Load(object? sender, EventArgs e)
    {
        try
        {
            AccountRepository.InitializeDatabase(_connectionString);
            // 1. Tự động nạp toàn bộ Page ID đã phân bổ vào bộ nhớ ảo chống trùng (Global Cache)
            GlobalState.InitializeFromDatabase(_connectionString);
            LoadAccountsFromDb();
            // Cập nhật số luồng động ngay khi mở phần mềm
            UpdateDynamicThreadCount();

            // [NHIỆM VỤ 2 - THỜI ĐIỂM 2]: Chạy ngầm dọn rác toàn bộ BrowserProfiles khi mở ứng dụng
            Task.Run(() =>
            {
                try
                {
                    ViaManager.Services.BrowserProfileService.CleanAllProfilesCache();
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khởi tạo cơ sở dữ liệu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ==========================================
    // 1. HÀM TÍNH TỔNG LUỒNG
    // ==========================================
    public int UpdateDynamicThreadCount()
    {
        if (this.InvokeRequired)
        {
            return (int)this.Invoke(new Func<int>(UpdateDynamicThreadCount));
        }

        try
        {
            int totalPageCount = AccountRepository.GetTotalPageCount(_connectionString);
            return totalPageCount;
        }
        catch
        {
            int fallback = GlobalState.ProcessedPageIds.Count;
            return fallback;
        }
    }

    // ==========================================
    // THREAD-SAFE UI HELPER METHODS
    // ==========================================

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "live", "die", "check", "running", "lock", ""
    };

    private void LogConsole(string uid, string message)
    {
        try
        {
            Console.WriteLine($"[{uid}] [{DateTime.Now:HH:mm:ss}] - {message}");
        }
        catch { }
    }

    private void EnsureComboValueExists(DataGridViewComboBoxColumn column, string? value)
    {
        if (!string.IsNullOrEmpty(value) && !column.Items.Contains(value))
        {
            column.Items.Add(value);
        }
    }

    private string NormalizeComboValue(DataGridViewComboBoxColumn column, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return "";

        string trimmed = rawValue.Trim();
        foreach (var item in column.Items)
        {
            if (item is string strItem && string.Equals(strItem, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return strItem;
            }
        }

        // Cột Trạng Thái chỉ chấp nhận các giá trị tĩnh: "live", "die", "check", "running", "lock"
        if (column == colTrangThai)
        {
            return "check";
        }

        EnsureComboValueExists(column, trimmed);
        return trimmed;
    }

    private void SetRowStatus(DataGridViewRow row, string id, string status)
    {
        if (dgvAccounts.InvokeRequired)
        {
            dgvAccounts.Invoke(new Action(() => SetRowStatus(row, id, status)));
            return;
        }

        try
        {
            string cleanStatus = (status ?? "").Trim().ToLowerInvariant();
            if (!ValidStatuses.Contains(cleanStatus))
            {
                // Cột 'Trạng Thái' TRÊN DATAGRIDVIEW BÂY GIỜ CHỈ ĐƯỢC CHỨA các giá trị tĩnh: "live", "die", "check", "running", "lock".
                // Tuyệt đối không cho phép gán chuỗi log/tiến trình vào cột này.
                return;
            }

            row.Cells["TrangThai"].Value = cleanStatus;
            dgvAccounts.InvalidateRow(row.Index);
            dgvAccounts.Update();
            UpdateCountLabels();
        }
        catch { }
    }

    private void SetRowCookie(DataGridViewRow row, string cookie)
    {
        if (dgvAccounts.InvokeRequired)
        {
            dgvAccounts.BeginInvoke(new Action(() => SetRowCookie(row, cookie)));
            return;
        }

        try
        {
            row.Cells["Cookie"].Value = cookie;
            dgvAccounts.InvalidateRow(row.Index);
        }
        catch { }
    }

    private void SetRowLastRunTime(DataGridViewRow row, DateTime time)
    {
        if (dgvAccounts.InvokeRequired)
        {
            dgvAccounts.BeginInvoke(new Action(() => SetRowLastRunTime(row, time)));
            return;
        }

        try
        {
            row.Cells["LastRunTime"].Value = time.ToString("yyyy-MM-dd HH:mm:ss");
            dgvAccounts.InvalidateRow(row.Index);
        }
        catch { }
    }

    private void SetRowPageCount(DataGridViewRow row, int count)
    {
        if (dgvAccounts.InvokeRequired)
        {
            dgvAccounts.Invoke(new Action(() => SetRowPageCount(row, count)));
            return;
        }

        try
        {
            row.Cells["PageCount"].Value = count;
            dgvAccounts.InvalidateRow(row.Index);
        }
        catch { }
    }

    private string GetRowStatus(DataGridViewRow row)
    {
        if (dgvAccounts.InvokeRequired)
        {
            return (string)dgvAccounts.Invoke(new Func<string>(() => GetRowStatus(row)));
        }
        return row.Cells["TrangThai"].Value?.ToString()?.Trim() ?? "";
    }

    private void UpdateStatusLabel(string text)
    {
        if (statusStrip.InvokeRequired)
        {
            statusStrip.BeginInvoke(new Action(() => UpdateStatusLabel(text)));
            return;
        }

        try
        {
            statusLabel.Text = text;
            statusStrip.Refresh();
        }
        catch { }
    }

    private void LoadAccountsFromDb(string searchFilter = "")
    {
        dgvAccounts.Rows.Clear();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000;";
            pragmaCmd.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(searchFilter))
        {
            cmd.CommandText = @"
                SELECT ID, PASS, Ma2FA, Cookie, Token, MAIL, PASS_MAIL, TrangThai, VaiTro, TenTaiKhoan, BM, BM_Mail, LastRunTime, PageCount
                FROM Accounts;
            ";
        }
        else
        {
            cmd.CommandText = @"
                SELECT ID, PASS, Ma2FA, Cookie, Token, MAIL, PASS_MAIL, TrangThai, VaiTro, TenTaiKhoan, BM, BM_Mail, LastRunTime, PageCount
                FROM Accounts
                WHERE ID LIKE $search OR MAIL LIKE $search OR TenTaiKhoan LIKE $search OR BM LIKE $search OR TrangThai LIKE $search OR VaiTro LIKE $search;
            ";
            cmd.Parameters.AddWithValue("$search", $"%{searchFilter.Trim()}%");
        }

        int totalCount = 0;
        int liveCount = 0;
        int runningCount = 0;
        int lockCount = 0;
        int dieCount = 0;
        int checkCount = 0;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.IsDBNull(0) ? "" : reader.GetString(0);
            string pass = reader.IsDBNull(1) ? "" : reader.GetString(1);
            string ma2fa = reader.IsDBNull(2) ? "" : reader.GetString(2);
            string cookie = reader.IsDBNull(3) ? "" : reader.GetString(3);
            string token = reader.IsDBNull(4) ? "" : reader.GetString(4);
            string mail = reader.IsDBNull(5) ? "" : reader.GetString(5);
            string passMail = reader.IsDBNull(6) ? "" : reader.GetString(6);
            string trangThai = NormalizeComboValue(colTrangThai, reader.IsDBNull(7) ? "" : reader.GetString(7));
            string vaiTro = NormalizeComboValue(colVaiTro, reader.IsDBNull(8) ? "" : reader.GetString(8));
            string tenTaiKhoan = reader.IsDBNull(9) ? "" : reader.GetString(9);
            string bm = NormalizeComboValue(colBM, reader.IsDBNull(10) ? "" : reader.GetString(10));
            string bmMail = reader.IsDBNull(11) ? "" : reader.GetString(11);
            string lastRunTime = reader.IsDBNull(12) ? "" : reader.GetString(12);
            int pageCount = reader.IsDBNull(13) ? 0 : Convert.ToInt32(reader.GetValue(13));

            int rowIndex = dgvAccounts.Rows.Add("▶ Chạy", id, pass, ma2fa, cookie, token, mail, passMail, trangThai, vaiTro, pageCount, tenTaiKhoan, bm, bmMail, lastRunTime);
            dgvAccounts.Rows[rowIndex].Tag = id;

            totalCount++;
            if (trangThai.Equals("live", StringComparison.OrdinalIgnoreCase)) liveCount++;
            else if (trangThai.Equals("running", StringComparison.OrdinalIgnoreCase)) runningCount++;
            else if (trangThai.Equals("lock", StringComparison.OrdinalIgnoreCase)) lockCount++;
            else if (trangThai.Equals("die", StringComparison.OrdinalIgnoreCase)) dieCount++;
            else if (trangThai.Equals("check", StringComparison.OrdinalIgnoreCase)) checkCount++;
        }

        statusLabel.Text = string.IsNullOrWhiteSpace(searchFilter)
            ? $"Đã tải {totalCount} tài khoản từ SQLite."
            : $"Bộ lọc '{searchFilter}': tìm thấy {totalCount} tài khoản.";

        countLabel.Text = $"Tổng: {totalCount} | Live: {liveCount} | Running: {runningCount} | Lock: {lockCount} | Check: {checkCount}";
        UpdateDynamicThreadCount();
    }

    private void UpdateCountLabels()
    {
        if (dgvAccounts.InvokeRequired)
        {
            dgvAccounts.BeginInvoke(new Action(UpdateCountLabels));
            return;
        }

        int total = 0, live = 0, running = 0, lockCount = 0, check = 0, die = 0;
        foreach (DataGridViewRow row in dgvAccounts.Rows)
        {
            if (row.IsNewRow) continue;
            total++;
            string st = row.Cells["TrangThai"].Value?.ToString() ?? "";
            if (st.Equals("live", StringComparison.OrdinalIgnoreCase)) live++;
            else if (st.Equals("running", StringComparison.OrdinalIgnoreCase)) running++;
            else if (st.Equals("lock", StringComparison.OrdinalIgnoreCase)) lockCount++;
            else if (st.Equals("check", StringComparison.OrdinalIgnoreCase)) check++;
            else if (st.Equals("die", StringComparison.OrdinalIgnoreCase)) die++;
        }
        countLabel.Text = $"Tổng: {total} | Live: {live} | Running: {running} | Lock: {lockCount} | Check: {check}";
    }

    private void dgvAccounts_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.CellStyle == null)
            return;

        string colName = dgvAccounts.Columns[e.ColumnIndex].Name;
        if (colName != "TrangThai" && colName != "VaiTro")
            return;

        string val = e.Value?.ToString()?.Trim() ?? "";
        var baseFont = dgvAccounts.DefaultCellStyle.Font ?? SystemFonts.DefaultFont;

        if (string.IsNullOrEmpty(val))
        {
            e.CellStyle.BackColor = Color.White;
            e.CellStyle.ForeColor = Color.Black;
            e.CellStyle.Font = baseFont;
            return;
        }

        // 'running' -> nền xanh dương nhạt (cyan), chữ xanh đậm
        if (val.Equals("running", StringComparison.OrdinalIgnoreCase))
        {
            e.CellStyle.BackColor = Color.FromArgb(186, 230, 253);
            e.CellStyle.ForeColor = Color.FromArgb(3, 105, 161);
            e.CellStyle.Font = new Font(baseFont, FontStyle.Bold);
            e.CellStyle.SelectionBackColor = Color.FromArgb(125, 211, 252);
            e.CellStyle.SelectionForeColor = Color.FromArgb(3, 105, 161);
        }
        // 'lock' -> nền vàng cam nhạt (amber), chữ nâu sẫm
        else if (val.Equals("lock", StringComparison.OrdinalIgnoreCase) && colName == "TrangThai")
        {
            e.CellStyle.BackColor = Color.FromArgb(254, 243, 199);
            e.CellStyle.ForeColor = Color.FromArgb(146, 64, 14);
            e.CellStyle.Font = baseFont;
            e.CellStyle.SelectionBackColor = Color.FromArgb(253, 230, 138);
            e.CellStyle.SelectionForeColor = Color.FromArgb(146, 64, 14);
        }
        // 'live' và 'Run' -> nền xanh lá chữ đậm
        else if (val.Equals("live", StringComparison.OrdinalIgnoreCase) ||
                 val.Equals("Run", StringComparison.OrdinalIgnoreCase))
        {
            e.CellStyle.BackColor = Color.FromArgb(183, 225, 205);
            e.CellStyle.ForeColor = Color.FromArgb(11, 83, 35);
            e.CellStyle.Font = new Font(baseFont, FontStyle.Bold);
            e.CellStyle.SelectionBackColor = Color.FromArgb(147, 196, 125);
            e.CellStyle.SelectionForeColor = Color.FromArgb(11, 83, 35);
        }
        // 'die' và 'Lock' (vai trò) -> nền đỏ nhạt chữ đỏ
        else if (val.Equals("die", StringComparison.OrdinalIgnoreCase) ||
                 (val.Equals("Lock", StringComparison.OrdinalIgnoreCase) && colName == "VaiTro"))
        {
            e.CellStyle.BackColor = Color.FromArgb(244, 199, 195);
            e.CellStyle.ForeColor = Color.FromArgb(180, 0, 0);
            e.CellStyle.Font = baseFont;
            e.CellStyle.SelectionBackColor = Color.FromArgb(224, 152, 145);
            e.CellStyle.SelectionForeColor = Color.FromArgb(160, 0, 0);
        }
        // 'check' -> nền vàng
        else if (val.Equals("check", StringComparison.OrdinalIgnoreCase))
        {
            e.CellStyle.BackColor = Color.FromArgb(252, 232, 178);
            e.CellStyle.ForeColor = Color.FromArgb(127, 96, 0);
            e.CellStyle.Font = baseFont;
            e.CellStyle.SelectionBackColor = Color.FromArgb(245, 212, 122);
            e.CellStyle.SelectionForeColor = Color.FromArgb(100, 75, 0);
        }
        // 'Reg' -> nền xanh dương nhạt
        else if (val.Equals("Reg", StringComparison.OrdinalIgnoreCase))
        {
            e.CellStyle.BackColor = Color.FromArgb(201, 218, 248);
            e.CellStyle.ForeColor = Color.FromArgb(17, 85, 204);
            e.CellStyle.Font = baseFont;
            e.CellStyle.SelectionBackColor = Color.FromArgb(164, 194, 244);
            e.CellStyle.SelectionForeColor = Color.FromArgb(17, 85, 204);
        }
    }

    private void chkOnlyLogin_CheckedChanged(object? sender, EventArgs e)
    {
        if (chkOnlyLogin.Checked)
        {
            statusLabel.Text = "Chế độ hiện tại: [CHỈ LOGIN TREO ACC] - Bỏ qua tiến trình cào Cookie Page.";
            LogConsole("SYSTEM", "Đã chuyển sang chế độ: [CHỈ LOGIN TREO ACC] - Bỏ qua cào Cookie Page.");
        }
        else
        {
            statusLabel.Text = "Chế độ hiện tại: [CHẠY FULL] - Tự động login, quét và lấy Cookie Page.";
            LogConsole("SYSTEM", "Đã chuyển sang chế độ: [CHẠY FULL] - Tự động quét và lấy Cookie Page.");
        }
    }

    private static async Task SimulateNewsfeedInteractionAsync(IPage page)
    {
        try
        {
            // Cuộn nhẹ newsfeed bằng JS để Facebook kích hoạt nạp feed
            await page.EvaluateAsync(@"() => {
                window.scrollBy({ top: 300 + Math.floor(Math.random() * 200), behavior: 'smooth' });
            }").ConfigureAwait(false);
            await Task.Delay(1500).ConfigureAwait(false);

            await page.EvaluateAsync(@"() => {
                window.scrollBy({ top: 250 + Math.floor(Math.random() * 250), behavior: 'smooth' });
            }").ConfigureAwait(false);
            await Task.Delay(1200).ConfigureAwait(false);

            // Cuộn ngược nhẹ mô phỏng người dùng đọc bài
            await page.EvaluateAsync(@"() => {
                window.scrollBy({ top: -150, behavior: 'smooth' });
            }").ConfigureAwait(false);
            await Task.Delay(800).ConfigureAwait(false);
        }
        catch
        {
            // Bỏ qua lỗi nếu trang đang trong trạng thái reload
        }
    }

    private void dgvAccounts_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != colBtnRun.Index)
            return;

        var row = dgvAccounts.Rows[e.RowIndex];
        string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
        string pass = row.Cells["PASS"].Value?.ToString()?.Trim() ?? "";
        string ma2fa = row.Cells["Ma2FA"].Value?.ToString()?.Trim() ?? "";
        string bm = row.Cells["BM"].Value?.ToString()?.Trim() ?? "Bmv1";
        string cookie = row.Cells["Cookie"].Value?.ToString()?.Trim() ?? "";

        if (string.IsNullOrEmpty(id))
        {
            MessageBox.Show("Dòng này chưa có ID (UID)! Vui lòng nhập UID trước khi chạy.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(cookie) && string.IsNullOrWhiteSpace(pass))
        {
            MessageBox.Show($"Tài khoản {id} chưa có Cookie hoặc Mật khẩu để đăng nhập!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Chạy đơn lẻ từ nút bấm cột 'Chạy' -> isHeadless: false để hiển thị trình duyệt Chrome
        _ = RunViaProfileAsync(row, id, cookie, pass, ma2fa, bm, showDialog: true, isHeadless: false, isOnlyLogin: chkOnlyLogin.Checked);
    }

    private void dgvAccounts_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        var row = dgvAccounts.Rows[e.RowIndex];
        string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
        string pass = row.Cells["PASS"].Value?.ToString()?.Trim() ?? "";
        string ma2fa = row.Cells["Ma2FA"].Value?.ToString()?.Trim() ?? "";
        string bm = row.Cells["BM"].Value?.ToString()?.Trim() ?? "Bmv1";
        string cookie = row.Cells["Cookie"].Value?.ToString()?.Trim() ?? "";

        if (string.IsNullOrEmpty(id))
            return;

        // Chạy thủ công từ click đúp dòng -> isHeadless: false để hiển thị trình duyệt Chrome
        _ = RunViaProfileAsync(row, id, cookie, pass, ma2fa, bm, showDialog: true, isHeadless: false, isOnlyLogin: chkOnlyLogin.Checked);
    }

    // ==========================================
    // CẬP NHẬT HÀM CHẠY VIA (RunViaProfileAsync) THEO 3 YÊU CẦU CỐT LÕI
    // ==========================================
    private async Task RunViaProfileAsync(DataGridViewRow row, string id, string rawCookie, string pass, string ma2fa, string bm, bool showDialog = false, bool isHeadless = true, bool isOnlyLogin = false)
    {
        string initialStatus = GetRowStatus(row);
        SetRowStatus(row, id, "running");
        _accountRepository.UpdateAccountStatus(id, "running");
        string modeLabel = isOnlyLogin ? "CHỈ LOGIN TREO ACC" : "CHẠY FULL";
        LogConsole(id, $"Bắt đầu chạy VIA ({modeLabel}). Trạng thái: running. Headless={isHeadless}");
        UpdateStatusLabel($"UID: {id} - Đang khởi động trình duyệt ({modeLabel})...");

        var account = new ViaManager.Models.ViaAccount
        {
            Uid = id,
            Password = pass,
            TwoFactorSecret = ma2fa,
            Cookie = rawCookie,
            BusinessManager = bm,
            Status = initialStatus
        };

        try
        {
            await using var session = await _browserProfileService.AcquireSessionAsync(id, isHeadless).ConfigureAwait(false);

            // =========================================================================
            // 1. XÁC THỰC ĐĂNG NHẬP QUA ViaAuthenticationService
            // =========================================================================
            LogConsole(id, "Đang kiểm tra phiên đăng nhập qua ViaAuthenticationService...");
            UpdateStatusLabel($"UID: {id} - Đang xác thực tài khoản...");

            var authResult = await _viaAuthService.EnsureAuthenticatedAsync(session, account).ConfigureAwait(false);
            if (!authResult.IsSuccess)
            {
                if (authResult.ErrorCode == "CHECKPOINT" || session.Page.Url.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
                {
                    SetRowStatus(row, id, "check");
                    _accountRepository.UpdateAccountStatus(id, "check");
                    _pageRepository.ReleaseAssignedPages(id);
                    LogConsole(id, "[CHECKPOINT] Tài khoản bị Checkpoint.");
                    UpdateStatusLabel($"UID: {id} - Tài khoản bị Checkpoint!");
                    if (showDialog)
                    {
                        this.Invoke(() => MessageBox.Show($"Tài khoản UID {id} bị Checkpoint!", "Cảnh báo", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                    }
                    return;
                }

                SetRowStatus(row, id, "check");
                _accountRepository.UpdateAccountStatus(id, "check");
                LogConsole(id, $"[XÁC THỰC THẤT BẠI] {authResult.Message}");
                UpdateStatusLabel($"UID: {id} - Đăng nhập thất bại.");
                if (showDialog)
                {
                    this.Invoke(() => MessageBox.Show($"Không thể đăng nhập tài khoản UID {id}: {authResult.Message}", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
                return;
            }

            LogConsole(id, "[ĐĂNG NHẬP THÀNH CÔNG] Đã xác thực thành công vào Facebook.");
            UpdateStatusLabel($"UID: {id} - Đã đăng nhập thành công!");

            // Cập nhật Cookie mới nhất chuẩn định dạng HTTP Header vào row và SQLite nếu có
            var fbCookies = await session.Context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
            string fullCookie = ViaManager.Utilities.CookieSerializer.SerializeVerbatim(fbCookies);
            if (!string.IsNullOrEmpty(fullCookie))
            {
                SetRowCookie(row, fullCookie);
                _accountRepository.UpdateAccountCookie(id, fullCookie);
            }

            SetRowStatus(row, id, "live");
            _accountRepository.UpdateAccountStatus(id, "live");

            // =========================================================================
            // 2. CHẾ ĐỘ TREO ACC (isOnlyLogin == true)
            // =========================================================================
            if (isOnlyLogin)
            {
                string ruleName = "CHẾ ĐỘ TREO ACC";
                Console.WriteLine($"[{id}] [{ruleName}] Đăng nhập thành công. Không tự động đóng trình duyệt, chờ người dùng tự thao tác...");
                LogConsole(id, $"[{ruleName}] Đăng nhập thành công! Hãy bấm dấu 'X' trên Chrome khi bạn muốn kết thúc...");
                UpdateStatusLabel($"UID: {id} - [{ruleName}] Đang mở trình duyệt. Không tự động đóng.");

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                session.Page.Close += (_, _) => tcs.TrySetResult(true);
                session.Context.Close += (_, _) => tcs.TrySetResult(true);

                try
                {
                    await tcs.Task.ConfigureAwait(false);
                }
                catch { }

                LogConsole(id, $"[{ruleName}] Người dùng đã đóng trình duyệt. Đang lưu phiên và hoàn tất.");
                return;
            }

            // =========================================================================
            // 3. CHẾ ĐỘ CHẠY FULL: TƯƠNG TÁC NEWSFEED & XỬ LÝ PAGE QUA PageSessionService
            // =========================================================================
            LogConsole(id, "Đang mô phỏng tương tác người thật trên Newsfeed (Scroll & Hover)...");
            await SimulateNewsfeedInteractionAsync(session.Page).ConfigureAwait(false);

            var progressReporter = new Progress<string>(msg =>
            {
                UpdateStatusLabel($"UID: {id} - {msg}");
                LogConsole(id, msg);
            });

            var switchResults = await _pageSessionService.ProcessViaPagesAsync(session, account, progressReporter).ConfigureAwait(false);

            int verifiedCount = switchResults.Count(r => r.IsSuccess);
            SetRowPageCount(row, verifiedCount);

            SetRowStatus(row, id, "live");
            _accountRepository.UpdateAccountStatus(id, "live");

            var exportedCookies = switchResults
                .Where(r => r.IsSuccess && r.Snapshot != null && !string.IsNullOrWhiteSpace(r.Snapshot.RawCookieHeader))
                .Select(r => r.Snapshot!.RawCookieHeader)
                .ToList();

            if (exportedCookies.Count > 0)
            {
                string firstCookie = exportedCookies.First();
                SetRowCookie(row, firstCookie);
                _accountRepository.UpdateAccountCookie(id, firstCookie);

                this.Invoke(new Action(() =>
                {
                    using var popForm = new FormCookieResult(id, bm, exportedCookies);
                    popForm.ShowDialog(this);
                }));
            }

            LogConsole(id, $"Hoàn thành! Thu thập thành công {verifiedCount} Cookie Page hợp lệ.");
            UpdateStatusLabel($"UID: {id} - Hoàn tất! Đã lưu {verifiedCount} Page.");
        }
        catch (PlaywrightException pex) when (pex.Message.Contains("closed") || pex.Message.Contains("Target closed"))
        {
            LogConsole(id, "Cửa sổ trình duyệt đã được đóng.");
        }
        catch (PlaywrightException pex) when (pex.Message.Contains("ERR_NAME_NOT_RESOLVED"))
        {
            SetRowStatus(row, id, "check");
            _accountRepository.UpdateAccountStatus(id, "check");
            LogConsole(id, $"[LỖI MẠNG / DNS] {pex.Message}");
            UpdateStatusLabel($"UID: {id} - Lỗi phân giải tên miền (DNS/Mạng)!");
            if (showDialog)
            {
                this.Invoke(() => MessageBox.Show($"Không thể kết nối tới Facebook (net::ERR_NAME_NOT_RESOLVED)!", "Lỗi kết nối", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
        }
        catch (Exception ex)
        {
            SetRowStatus(row, id, "check");
            _accountRepository.UpdateAccountStatus(id, "check");
            LogConsole(id, $"[LỖI NGOẠI LỆ] {ex.Message}");
            UpdateStatusLabel($"UID: {id} - Lỗi: {ex.Message}");
            if (showDialog)
            {
                this.Invoke(() => MessageBox.Show($"Lỗi trong quá trình chạy tự động UID {id}: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
        }
        finally
        {
            var now = DateTime.Now;
            try
            {
                _accountRepository.UpdateLastRunTime(id, now);
                SetRowLastRunTime(row, now);

                string currentStatus = GetRowStatus(row);
                if (!currentStatus.Equals("check", StringComparison.OrdinalIgnoreCase) &&
                    !currentStatus.Equals("die", StringComparison.OrdinalIgnoreCase))
                {
                    SetRowStatus(row, id, "lock");
                    _accountRepository.UpdateAccountStatus(id, "lock");
                }
            }
            catch { }

            // [NHIỆM VỤ 2 - THỜI ĐIỂM 1]: Dọn rác cache sau khi hoàn tất phiên chạy của Via
            try
            {
                string profileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserProfiles", id);
                ViaManager.Services.BrowserProfileService.CleanProfileCache(profileDir);
            }
            catch { }
        }
    }

    // ==========================================
    // 3. LOGIC XOAY VÒNG & NGHỈ 2 TIẾNG (GetAvailableVias)
    // ==========================================

    public List<DataGridViewRow> GetAvailableVias()
    {
        var available = new List<DataGridViewRow>();

        foreach (DataGridViewRow row in dgvAccounts.Rows)
        {
            if (row.IsNewRow) continue;

            string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(id)) continue;

            string status = row.Cells["TrangThai"].Value?.ToString()?.Trim() ?? "";

            // Bỏ qua các via đang 'running', 'die', 'check'
            if (status.Equals("running", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("die", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("check", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Nếu via đang 'lock', kiểm tra cột LastRunTime
            if (status.Equals("lock", StringComparison.OrdinalIgnoreCase))
            {
                string timeStr = row.Cells["LastRunTime"].Value?.ToString()?.Trim() ?? "";
                DateTime? lastRun = null;

                if (DateTime.TryParse(timeStr, out var parsedTime))
                {
                    lastRun = parsedTime;
                }
                else
                {
                    lastRun = AccountRepository.GetLastRunTime(_connectionString, id);
                }

                if (lastRun.HasValue)
                {
                    // Nếu đã nghỉ đủ >= 2 tiếng, tự động đổi trạng thái thành 'live' (mở khóa)
                    if ((DateTime.Now - lastRun.Value).TotalHours >= 2.0)
                    {
                        SetRowStatus(row, id, "live");
                        AccountRepository.UpdateAccountStatus(_connectionString, id, "live");
                        available.Add(row);
                    }
                }
                else
                {
                    // Không có lịch sử LastRunTime cũ -> mở khóa sang 'live'
                    SetRowStatus(row, id, "live");
                    AccountRepository.UpdateAccountStatus(_connectionString, id, "live");
                    available.Add(row);
                }

                continue;
            }

            // Các via đang 'live' hoặc chưa có trạng thái
            available.Add(row);
        }

        return available;
    }

    private void dgvAccounts_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (dgvAccounts.IsCurrentCellDirty && dgvAccounts.CurrentCell is DataGridViewComboBoxCell)
        {
            dgvAccounts.CommitEdit(DataGridViewDataErrorContexts.Commit);
            dgvAccounts.InvalidateCell(dgvAccounts.CurrentCell);
        }
    }

    private void dgvAccounts_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
    }

    private void btnImport_Click(object? sender, EventArgs e)
    {
        using var dlg = new FormImport(_connectionString);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            LoadAccountsFromDb(txtSearch.Text);
            statusLabel.Text = $"Đã import thành công {dlg.ImportedCount} tài khoản vào SQLite.";
        }
    }

    private void btnPasteSheet_Click(object? sender, EventArgs e)
    {
        string clipboardText = Clipboard.GetText();
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            MessageBox.Show("Clipboard đang trống! Hãy copy các dòng dữ liệu trước khi bấm nút.",
                "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string[] lines = clipboardText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        var accountList = new List<AccountItem>();

        foreach (var line in lines)
        {
            var item = AccountParser.ParseLine(line, "live", "Run", "Bmv1");
            if (item != null && !string.IsNullOrWhiteSpace(item.ID))
            {
                accountList.Add(item);
            }
        }

        if (accountList.Count == 0)
        {
            MessageBox.Show("Không nhận diện được dòng tài khoản hợp lệ nào từ clipboard!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            int saved = AccountRepository.BatchUpsert(_connectionString, accountList);
            LoadAccountsFromDb(txtSearch.Text);
            statusLabel.Text = $"Đã dán và lưu thành công {saved} tài khoản từ Clipboard vào SQLite.";
            MessageBox.Show($"Đã dán và lưu thành công {saved} tài khoản vào SQLite!",
                "Dán thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi dán dữ liệu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnSave_Click(object? sender, EventArgs e)
    {
        dgvAccounts.EndEdit();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 15000;";
            pragmaCmd.ExecuteNonQuery();
        }

        using var transaction = conn.BeginTransaction();

        try
        {
            int savedCount = 0;
            foreach (DataGridViewRow row in dgvAccounts.Rows)
            {
                if (row.IsNewRow)
                    continue;

                string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(id))
                    continue;

                string? oldId = row.Tag as string;
                if (!string.IsNullOrEmpty(oldId) && !string.Equals(oldId, id, StringComparison.Ordinal))
                {
                    using var delCmd = conn.CreateCommand();
                    delCmd.Transaction = transaction;
                    delCmd.CommandText = "DELETE FROM Accounts WHERE ID = $oldId";
                    delCmd.Parameters.AddWithValue("$oldId", oldId);
                    delCmd.ExecuteNonQuery();

                    using var delPagesCmd = conn.CreateCommand();
                    delPagesCmd.Transaction = transaction;
                    delPagesCmd.CommandText = "DELETE FROM AssignedPages WHERE Via_UID = $oldId";
                    delPagesCmd.Parameters.AddWithValue("$oldId", oldId);
                    delPagesCmd.ExecuteNonQuery();
                }

                string pass = row.Cells["PASS"].Value?.ToString()?.Trim() ?? "";
                string ma2fa = row.Cells["Ma2FA"].Value?.ToString()?.Trim() ?? "";
                string cookie = row.Cells["Cookie"].Value?.ToString()?.Trim() ?? "";
                string token = row.Cells["Token"].Value?.ToString()?.Trim() ?? "";
                string mail = row.Cells["MAIL"].Value?.ToString()?.Trim() ?? "";
                string passMail = row.Cells["PASS_MAIL"].Value?.ToString()?.Trim() ?? "";
                string trangThai = NormalizeComboValue(colTrangThai, row.Cells["TrangThai"].Value?.ToString());
                string vaiTro = NormalizeComboValue(colVaiTro, row.Cells["VaiTro"].Value?.ToString());
                string tenTaiKhoan = row.Cells["TenTaiKhoan"].Value?.ToString()?.Trim() ?? "";
                string bm = NormalizeComboValue(colBM, row.Cells["BM"].Value?.ToString());
                string bmMail = row.Cells["BM_Mail"].Value?.ToString()?.Trim() ?? "";
                string lastRunTime = row.Cells["LastRunTime"].Value?.ToString()?.Trim() ?? "";
                int pageCount = int.TryParse(row.Cells["PageCount"].Value?.ToString(), out var pc) ? pc : 0;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO Accounts (ID, PASS, Ma2FA, Cookie, Token, MAIL, PASS_MAIL, TrangThai, VaiTro, TenTaiKhoan, BM, BM_Mail, LastRunTime, PageCount)
                    VALUES ($id, $pass, $ma2fa, $cookie, $token, $mail, $passMail, $trangThai, $vaiTro, $tenTaiKhoan, $bm, $bmMail, $lastRunTime, $pageCount)
                    ON CONFLICT(ID) DO UPDATE SET
                        PASS = excluded.PASS,
                        Ma2FA = excluded.Ma2FA,
                        Cookie = excluded.Cookie,
                        Token = excluded.Token,
                        MAIL = excluded.MAIL,
                        PASS_MAIL = excluded.PASS_MAIL,
                        TrangThai = excluded.TrangThai,
                        VaiTro = excluded.VaiTro,
                        TenTaiKhoan = excluded.TenTaiKhoan,
                        BM = excluded.BM,
                        BM_Mail = excluded.BM_Mail,
                        LastRunTime = CASE WHEN excluded.LastRunTime <> '' THEN excluded.LastRunTime ELSE Accounts.LastRunTime END,
                        PageCount = CASE WHEN excluded.PageCount > 0 THEN excluded.PageCount ELSE Accounts.PageCount END;
                ";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$pass", pass);
                cmd.Parameters.AddWithValue("$ma2fa", ma2fa);
                cmd.Parameters.AddWithValue("$cookie", cookie);
                cmd.Parameters.AddWithValue("$token", token);
                cmd.Parameters.AddWithValue("$mail", mail);
                cmd.Parameters.AddWithValue("$passMail", passMail);
                cmd.Parameters.AddWithValue("$trangThai", trangThai);
                cmd.Parameters.AddWithValue("$vaiTro", vaiTro);
                cmd.Parameters.AddWithValue("$tenTaiKhoan", tenTaiKhoan);
                cmd.Parameters.AddWithValue("$bm", bm);
                cmd.Parameters.AddWithValue("$bmMail", bmMail);
                cmd.Parameters.AddWithValue("$lastRunTime", lastRunTime);
                cmd.Parameters.AddWithValue("$pageCount", pageCount);
                cmd.ExecuteNonQuery();

                // Nếu trạng thái là die hoặc check, tự động giải phóng Pages
                if (trangThai.Equals("die", StringComparison.OrdinalIgnoreCase) ||
                    trangThai.Equals("check", StringComparison.OrdinalIgnoreCase))
                {
                    using var relCmd = conn.CreateCommand();
                    relCmd.Transaction = transaction;
                    relCmd.CommandText = "DELETE FROM AssignedPages WHERE Via_UID = $id;";
                    relCmd.Parameters.AddWithValue("$id", id);
                    relCmd.ExecuteNonQuery();
                }

                row.Tag = id;
                savedCount++;
            }

            transaction.Commit();
            UpdateCountLabels();
            statusLabel.Text = $"Đã lưu {savedCount} dòng vào SQLite lúc {DateTime.Now:HH:mm:ss}.";
            MessageBox.Show($"Đã lưu thành công {savedCount} tài khoản vào cơ sở dữ liệu!",
                "Lưu thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            MessageBox.Show($"Lỗi khi lưu thay đổi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnDelete_Click(object? sender, EventArgs e)
    {
        var selectedRows = new List<DataGridViewRow>();

        foreach (DataGridViewRow row in dgvAccounts.SelectedRows)
        {
            if (!row.IsNewRow)
                selectedRows.Add(row);
        }

        if (selectedRows.Count == 0 && dgvAccounts.CurrentRow != null && !dgvAccounts.CurrentRow.IsNewRow)
        {
            selectedRows.Add(dgvAccounts.CurrentRow);
        }

        if (selectedRows.Count == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất một dòng để xóa.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // RÀNG BUỘC: Chỉ tài khoản có trạng thái 'die' mới được phép xóa
        var nonDieRows = selectedRows.Where(r =>
        {
            string status = r.Cells["TrangThai"].Value?.ToString()?.Trim() ?? "";
            return !status.Equals("die", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        if (nonDieRows.Count > 0)
        {
            var nonDieIds = nonDieRows.Select(r => r.Cells["ID"].Value?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).Take(5).ToList();
            string idsSample = string.Join(", ", nonDieIds);
            MessageBox.Show(
                $"Ràng buộc an toàn: Chỉ những tài khoản có trạng thái 'die' mới được phép xóa!\n\n" +
                $"Phát hiện {nonDieRows.Count} tài khoản được chọn đang có trạng thái khác 'die' (Ví dụ: {idsSample}).\n" +
                $"Vui lòng chỉ chọn các dòng có trạng thái 'die' để xóa.",
                "Cảnh báo ràng buộc xóa",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var rowsToDelete = selectedRows;

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa {rowsToDelete.Count} tài khoản 'die' đã chọn khỏi cơ sở dữ liệu và bảng không?",
            "Xác nhận xóa tài khoản die",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        var idsToDelete = new List<string>();
        foreach (var row in rowsToDelete)
        {
            string id = (row.Tag as string) ?? row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
            if (!string.IsNullOrEmpty(id))
            {
                idsToDelete.Add(id);
            }
        }

        int deletedCount = 0;
        try
        {
            deletedCount = _accountRepository.DeleteAccounts(idsToDelete);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi xóa dòng: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        statusLabel.Text = $"Đã xóa {deletedCount} tài khoản 'die'.";
        UpdateCountLabels();
        LoadAccountsFromDb(txtSearch.Text);
        UpdateDynamicThreadCount();

        MessageBox.Show($"Đã xóa thành công {deletedCount} tài khoản 'die' khỏi SQLite!",
            "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void btnReload_Click(object? sender, EventArgs e)
    {
        txtSearch.Clear();
        LoadAccountsFromDb();
    }

    private void txtSearch_TextChanged(object? sender, EventArgs e)
    {
        LoadAccountsFromDb(txtSearch.Text);
    }
}
