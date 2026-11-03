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

            var page = session.Page;
            var context = session.Context;

            // =========================================================================
            // 1. BẢO TOÀN LOGIC LOGIN ĐÃ CÓ:
            // - Mở URL https://www.facebook.com
            // - Kiểm tra xem đã có phiên đăng nhập chưa
            // - NẾU CHƯA: Chạy toàn bộ logic cũ (tiêm Cookie từ DataGridView, hoặc nhập Pass/2FA)
            // - Xác nhận đăng nhập thành công (vào được bảng tin Newsfeed)
            // =========================================================================
            LogConsole(id, "Đang mở Facebook để kiểm tra phiên đăng nhập...");
            UpdateStatusLabel($"UID: {id} - Đang kết nối Facebook...");
            await page.GotoAsync("https://www.facebook.com", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 }).ConfigureAwait(false);
            await Task.Delay(2500).ConfigureAwait(false);

            var cookies = await context.CookiesAsync().ConfigureAwait(false);
            bool hasCUser = cookies.Any(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
            string currentUrl = page.Url;
            bool isCheckpoint = currentUrl.Contains("checkpoint", StringComparison.OrdinalIgnoreCase);
            bool isLoginPage = currentUrl.Contains("/login", StringComparison.OrdinalIgnoreCase);
            bool isLoggedIn = hasCUser && !isCheckpoint && !isLoginPage;

            if (!isLoggedIn)
            {
                // Bước 1A: Tiêm Cookie từ DataGridView nếu có
                if (!string.IsNullOrWhiteSpace(rawCookie))
                {
                    LogConsole(id, "Chưa có phiên đăng nhập. Đang tiêm Cookie từ bảng tài khoản...");
                    UpdateStatusLabel($"UID: {id} - Đang tiêm Cookie...");
                    var cookieList = CookieHelper.ParseCookieString(rawCookie);
                    if (cookieList.Count > 0)
                    {
                        await context.AddCookiesAsync(cookieList).ConfigureAwait(false);
                        await page.GotoAsync("https://www.facebook.com", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 }).ConfigureAwait(false);
                        await Task.Delay(3000).ConfigureAwait(false);

                        cookies = await context.CookiesAsync().ConfigureAwait(false);
                        hasCUser = cookies.Any(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                        currentUrl = page.Url;
                        isCheckpoint = currentUrl.Contains("checkpoint", StringComparison.OrdinalIgnoreCase);
                        isLoginPage = currentUrl.Contains("/login", StringComparison.OrdinalIgnoreCase);
                        isLoggedIn = hasCUser && !isCheckpoint && !isLoginPage;
                    }
                }

                // Bước 1B: Nếu chưa đăng nhập được và có Pass -> Fallback nhập Pass & 2FA
                if (!isLoggedIn && !string.IsNullOrWhiteSpace(pass))
                {
                    LogConsole(id, "Cookie không đăng nhập được. Tiến hành đăng nhập bằng UID / Mật khẩu...");
                    UpdateStatusLabel($"UID: {id} - Đang đăng nhập bằng Pass...");

                    if (!page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase))
                    {
                        await page.GotoAsync("https://www.facebook.com/login", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                        await Task.Delay(2000).ConfigureAwait(false);
                    }

                    var emailInput = page.Locator("input[name='email'], #email");
                    if (await emailInput.CountAsync().ConfigureAwait(false) > 0)
                    {
                        await emailInput.First.FillAsync(id).ConfigureAwait(false);
                        await page.FillAsync("input[name='pass'], #pass", pass).ConfigureAwait(false);

                        var loginBtn = page.Locator("button[name='login'], button[type='submit'], button:has-text('Đăng nhập'), button:has-text('Log In')");
                        if (await loginBtn.CountAsync().ConfigureAwait(false) > 0)
                            await loginBtn.First.ClickAsync().ConfigureAwait(false);
                        else
                            await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);

                        try
                        {
                            await page.WaitForURLAsync(u => u.Contains("checkpoint") || u.Contains("home") || u.Contains("two_factor") || u.Contains("login"), new() { Timeout = 15000 }).ConfigureAwait(false);
                        }
                        catch { }

                        await Task.Delay(3000).ConfigureAwait(false);

                        // Xử lý mã xác thực 2 bước (2FA)
                        var otpInputs = page.Locator("input[name='approvals_code'], input[type='tel'], input[placeholder*='code'], input[placeholder*='mã']");
                        if (await otpInputs.CountAsync().ConfigureAwait(false) > 0 && !string.IsNullOrWhiteSpace(ma2fa))
                        {
                            LogConsole(id, "Phát hiện xác thực 2 bước (2FA). Đang tạo mã OTP...");
                            string otp = TotpGenerator.GenerateTotp(ma2fa);
                            if (!string.IsNullOrEmpty(otp))
                            {
                                await otpInputs.First.FillAsync(otp).ConfigureAwait(false);
                                var submitBtn = page.Locator("button[type='submit'], #checkpointSubmitButton, button[id*='checkpoint']");
                                if (await submitBtn.CountAsync().ConfigureAwait(false) > 0)
                                    await submitBtn.First.ClickAsync().ConfigureAwait(false);
                                else
                                    await page.Keyboard.PressAsync("Enter").ConfigureAwait(false);

                                await Task.Delay(4000).ConfigureAwait(false);
                            }
                        }
                    }

                    cookies = await context.CookiesAsync().ConfigureAwait(false);
                    hasCUser = cookies.Any(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                    currentUrl = page.Url;
                    isCheckpoint = currentUrl.Contains("checkpoint", StringComparison.OrdinalIgnoreCase);
                    isLoginPage = currentUrl.Contains("/login", StringComparison.OrdinalIgnoreCase);
                    isLoggedIn = hasCUser && !isCheckpoint && !isLoginPage;
                }
            }

            // Kiểm tra Checkpoint
            if (isCheckpoint || page.Url.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
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

            // Kiểm tra đăng nhập thành công
            if (!isLoggedIn)
            {
                SetRowStatus(row, id, "check");
                _accountRepository.UpdateAccountStatus(id, "check");
                LogConsole(id, "[XÁC THỰC THẤT BẠI] Không thể đăng nhập vào Facebook.");
                UpdateStatusLabel($"UID: {id} - Đăng nhập thất bại.");
                if (showDialog)
                {
                    this.Invoke(() => MessageBox.Show($"Không thể đăng nhập tài khoản UID {id}!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
                return;
            }

            // 4. Xác nhận đăng nhập thành công (vào được bảng tin Newsfeed)
            LogConsole(id, "[ĐĂNG NHẬP THÀNH CÔNG] Đã vào bảng tin Newsfeed.");
            UpdateStatusLabel($"UID: {id} - Đã đăng nhập thành công!");

            // Cập nhật Cookie mới nhất chuẩn định dạng HTTP Header vào row và SQLite nếu có
            var fbCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
            if (fbCookies == null || fbCookies.Count == 0)
            {
                fbCookies = cookies;
            }
            string fullCookie = ViaManager.Utilities.CookieSerializer.SerializeVerbatim(fbCookies);
            if (!string.IsNullOrEmpty(fullCookie))
            {
                SetRowCookie(row, fullCookie);
                _accountRepository.UpdateAccountCookie(id, fullCookie);
            }

            // Cập nhật trạng thái 'live' khi đăng nhập thành công
            SetRowStatus(row, id, "live");
            _accountRepository.UpdateAccountStatus(id, "live");

            // =========================================================================
            // QUY TẮC RẼ NHÁNH: KHI VÀ CHỈ KHI BẬT CHẾ ĐỘ TREO ACC (isOnlyLogin == true)
            // ĐỨNG IM 100% - TUYỆT ĐỐI CẤM CUỘN CHUỘT / KHÔNG TƯƠNG TÁC
            // QUY TẮC: TUYỆT ĐỐI KHÔNG TỰ ĐỘNG ĐÓNG TRÌNH DUYỆT - KHÓA LUỒNG CHỜ NGƯỜI DÙNG TỰ ĐÓNG BẰNG TAY (DẤU 'X')
            // Khi người dùng chọn [CHẠY FULL] (isOnlyLogin == false): Bỏ qua hoàn toàn, đi thẳng vào Newsfeed -> Quét Page -> Lấy Cookie!
            // =========================================================================
            if (isOnlyLogin)
            {
                string ruleName = "CHẾ ĐỘ TREO ACC";

                Console.WriteLine($"[{id}] [{ruleName}] Đăng nhập thành công. QUY TẮC: Không tự động đóng trình duyệt, đứng im chờ người dùng tự thao tác...");
                LogConsole(id, $"[{ruleName}] Đăng nhập thành công! QUY TẮC: Không tự động đóng trình duyệt, đứng im để bạn thao tác. Hãy bấm dấu 'X' trên Chrome khi bạn muốn kết thúc...");
                UpdateStatusLabel($"UID: {id} - [{ruleName}] Đang mở trình duyệt. Không tự động đóng trình duyệt.");

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                page.Close += (_, _) => tcs.TrySetResult(true);
                context.Close += (_, _) => tcs.TrySetResult(true);

                try
                {
                    await tcs.Task.ConfigureAwait(false);
                }
                catch
                {
                    // Bỏ qua ngoại lệ khi người dùng đóng ngang cửa sổ Chrome
                }

                LogConsole(id, $"[{ruleName}] Người dùng đã đóng trình duyệt. Đang lưu phiên và hoàn tất.");
                return; // Thoát ra khối finally để lưu dữ liệu an toàn
            }

            // =========================================================================
            // CHẾ ĐỘ CHẠY FULL: MÔ PHỎNG TƯƠNG TÁC NGƯỜI THẬT TRÊN NEWSFEED & CÀO PAGE
            // =========================================================================
            LogConsole(id, "Đang mô phỏng tương tác người thật trên Newsfeed (Scroll & Hover)...");
            await SimulateNewsfeedInteractionAsync(page).ConfigureAwait(false);

            var progressReporter = new Progress<string>(msg =>
            {
                UpdateStatusLabel($"UID: {id} - {msg}");
                LogConsole(id, msg);
            });

            // =========================================================================
            // =========================================================================
            // 4. QUÉT & SWITCH ĐỘNG TRỰC TIẾP TỪ MENU POPUP (BỎ QUA GOTO URL)
            // =========================================================================
            LogConsole(id, "Bắt đầu mở hộp thoại Switch Profile để quét và switch trực tiếp các Page...");
            UpdateStatusLabel($"UID: {id} - Đang mở hộp thoại Switch Profile...");

            // Đảm bảo đang ở trang chủ Facebook
            if (!page.Url.StartsWith("https://www.facebook.com", StringComparison.OrdinalIgnoreCase) || page.Url.Contains("/pages/"))
            {
                await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                await Task.Delay(2000).ConfigureAwait(false);
            }

            bool popupOpened = await OpenProfileSwitcherDialogAsync(page, id).ConfigureAwait(false);
            if (popupOpened)
            {
                LogConsole(id, "Đang cuộn khung danh sách bên trong popup để tải toàn bộ Page (Lazy Load)...");
                for (int scrollIdx = 0; scrollIdx < 5; scrollIdx++)
                {
                    await page.EvaluateAsync(@"() => {
                        let dialogs = document.querySelectorAll('div[role=""dialog""]');
                        if (dialogs.length > 0) {
                            let scrollers = dialogs[0].querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                            for (let s of scrollers) s.scrollTop = s.scrollHeight;
                        }
                    }").ConfigureAwait(false);
                    await Task.Delay(800).ConfigureAwait(false);
                }

                // Đưa vị trí cuộn về đỉnh (top = 0) để bắt đầu duyệt từ các mục đầu tiên
                await page.EvaluateAsync(@"() => {
                    let dialogs = document.querySelectorAll('div[role=""dialog""]');
                    if (dialogs.length > 0) {
                        let scrollers = dialogs[0].querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                        for (let s of scrollers) s.scrollTop = 0;
                    }
                }").ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
            }

            var initialElements = popupOpened ? await GetDialogProfileElementsAsync(page).ConfigureAwait(false) : (IReadOnlyList<ILocator>)Array.Empty<ILocator>();
            int totalElements = initialElements.Count;
            LogConsole(id, $"[POPUP SCAN] Tìm thấy tổng cộng {totalElements} phần tử trong hộp thoại.");

            var exportedCookies = new List<string>();
            var capturedIUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var popupVerifiedPages = new List<ViaManager.Models.ManagedPage>();

            // 1. THUẬT TOÁN CLICK ĐỘNG BÊN TRONG POPUP
            // Nếu có phần tử (> 1 vì dòng đầu tiên là Via gốc)
            if (totalElements > 1)
            {
                LogConsole(id, $"Bắt đầu Switch trực tiếp {totalElements - 1} Page từ bên trong menu Popup...");

                for (int i = 1; i < totalElements; i++)
                {
                    LogConsole(id, $"=== Đang Switch Page thứ {i}/{totalElements - 1} ===");
                    UpdateStatusLabel($"UID: {id} - Đang switch Page ({i}/{totalElements - 1})...");

                    try
                    {
                        // a. Mỗi lần lặp i, phải MỞ LẠI hộp thoại (vì click vào 1 Page sẽ làm mất hộp thoại).
                        if (i == 1)
                        {
                            var existingDialog = page.Locator("div[role='dialog']:visible");
                            if (await existingDialog.CountAsync().ConfigureAwait(false) == 0)
                            {
                                await OpenProfileSwitcherDialogAsync(page, id).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            // Từ lượt i > 1 trở đi, do phiên đã chuyển sang Page cũ nên reset i_user để về Via gốc
                            try
                            {
                                await context.ClearCookiesAsync(new BrowserContextClearCookiesOptions { Name = "i_user" }).ConfigureAwait(false);
                            }
                            catch { }

                            await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                            await Task.Delay(1500).ConfigureAwait(false);

                            bool isDialogReady = await OpenProfileSwitcherDialogAsync(page, id).ConfigureAwait(false);
                            if (!isDialogReady)
                            {
                                await page.GotoAsync("https://www.facebook.com/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false);
                                await Task.Delay(1500).ConfigureAwait(false);
                                isDialogReady = await OpenProfileSwitcherDialogAsync(page, id).ConfigureAwait(false);
                            }

                            if (!isDialogReady)
                            {
                                LogConsole(id, $"[CẢNH BÁO] Không thể mở lại hộp thoại ở lượt {i}. Bỏ qua lượt này.");
                                continue;
                            }

                            // Cuộn lazy load nhanh để DOM tải đủ các phần tử
                            await page.EvaluateAsync(@"() => {
                                let dialogs = document.querySelectorAll('div[role=""dialog""]');
                                if (dialogs.length > 0) {
                                    let scrollers = dialogs[0].querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                                    for (let s of scrollers) s.scrollTop = s.scrollHeight;
                                }
                            }").ConfigureAwait(false);
                            await Task.Delay(600).ConfigureAwait(false);
                            await page.EvaluateAsync(@"() => {
                                let dialogs = document.querySelectorAll('div[role=""dialog""]');
                                if (dialogs.length > 0) {
                                    let scrollers = dialogs[0].querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                                    for (let s of scrollers) s.scrollTop = 0;
                                }
                            }").ConfigureAwait(false);
                            await Task.Delay(400).ConfigureAwait(false);
                        }

                        // b. Gọi lại pageElements = await page.Locator(...).AllAsync(); để cập nhật lại ElementHandle mới.
                        var currentElements = await GetDialogProfileElementsAsync(page).ConfigureAwait(false);
                        if (i >= currentElements.Count)
                        {
                            LogConsole(id, $"[CẢNH BÁO] Danh sách phần tử sau khi mở lại chỉ có {currentElements.Count}, không đủ chỉ số mục {i}. Bỏ qua.");
                            continue;
                        }

                        var targetElement = currentElements[i];
                        string rawText = "";
                        try
                        {
                            rawText = (await targetElement.InnerTextAsync().ConfigureAwait(false) ?? "").Trim();
                        }
                        catch { }

                        string targetPageName = ExtractCleanPageName(rawText);
                        if (string.IsNullOrWhiteSpace(targetPageName))
                        {
                            targetPageName = $"Mục {i}";
                        }

                        // Log rõ quá trình bóc tách phần tử và text của nó trước khi click để dễ debug (Prompt mục IV)
                        LogConsole(id, $"[POPUP ITEM {i}/{totalElements - 1}] Bóc tách: Name='{targetPageName}' | Text='{rawText.Replace("\r", " ").Replace("\n", " | ")}'");

                        // Chặn text rác (Prompt mục III)
                        if (!IsValidPageItem(rawText))
                        {
                            LogConsole(id, $"[BỎ QUA] Mục {i} ('{targetPageName}') chứa text rác hoặc thông báo. Bỏ qua.");
                            continue;
                        }

                        // c. Bỏ qua phần tử đầu tiên (Via gốc). Click vào phần tử thứ i: await pageElements[i].ClickAsync();
                        LogConsole(id, $"Đang bấm trực tiếp vào phần tử Page mục {i} ('{targetPageName}')...");
                        try
                        {
                            await targetElement.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
                            await Task.Delay(300).ConfigureAwait(false);

                            var clickable = targetElement.Locator("div[role='radio']:visible, div[role='button']:visible").Nth(0);
                            if (await clickable.CountAsync().ConfigureAwait(false) > 0)
                            {
                                await clickable.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
                            }
                            else
                            {
                                await targetElement.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
                            }
                        }
                        catch (Exception clickEx)
                        {
                            try
                            {
                                await targetElement.EvaluateAsync(@"(el) => {
                                    const target = el.querySelector('div[role=""radio""], div[role=""button""]') || el;
                                    target.click();
                                }").ConfigureAwait(false);
                            }
                            catch
                            {
                                LogConsole(id, $"[CẢNH BÁO] Không thể click vào phần tử mục {i}: {clickEx.Message}");
                                continue;
                            }
                        }

                        // Xử lý modal xác nhận chuyển nếu có ("Chuyển", "Switch", "Chuyển ngay", "Switch now")
                        for (int cWait = 0; cWait < 5; cWait++)
                        {
                            await Task.Delay(600).ConfigureAwait(false);
                            bool confirmClicked = await page.EvaluateAsync<bool>(@"() => {
                                const dialogs = document.querySelectorAll('div[role=""dialog""]');
                                for (let d = dialogs.length - 1; d >= 0; d--) {
                                    const buttons = dialogs[d].querySelectorAll('div[role=""button""], button');
                                    for (const btn of buttons) {
                                        const t = (btn.innerText || '').trim();
                                        if (/^(Chuyển|Switch|Chuyển ngay|Switch now)$/i.test(t)) {
                                            btn.click();
                                            return true;
                                        }
                                    }
                                }
                                return false;
                            }").ConfigureAwait(false);

                            if (confirmClicked)
                            {
                                LogConsole(id, "Đã bấm nút xác nhận 'Chuyển' trong dialog.");
                                break;
                            }
                        }

                        // d. Đợi trang load lại hoặc chờ context.CookiesAsync trả về cookie có chứa i_user
                        string? newIUser = null;
                        for (int poll = 0; poll < 15; poll++)
                        {
                            await Task.Delay(1000).ConfigureAwait(false);
                            var currentCookies = await context.CookiesAsync(new[] { "https://www.facebook.com", "https://facebook.com" }).ConfigureAwait(false);
                            var iUserCookie = currentCookies.FirstOrDefault(c => c.Name.Equals("i_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                            if (iUserCookie != null && !iUserCookie.Value.Equals(id, StringComparison.OrdinalIgnoreCase))
                            {
                                newIUser = iUserCookie.Value.Trim();
                                break;
                            }

                            var cUserCookie = currentCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                            if (cUserCookie != null && !cUserCookie.Value.Equals(id, StringComparison.OrdinalIgnoreCase))
                            {
                                newIUser = cUserCookie.Value.Trim();
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(newIUser))
                        {
                            LogConsole(id, $"[CẢNH BÁO] Không phát hiện cookie i_user/c_user Page mới sau khi click mục {i} ({targetPageName}).");
                            continue;
                        }

                        // Chống trùng lặp tuyệt đối theo i_user
                        if (capturedIUsers.Contains(newIUser))
                        {
                            LogConsole(id, $"[TRÙNG LẶP] Page {newIUser} đã lấy Cookie trước đó. Bỏ qua duplicate.");
                            continue;
                        }

                        capturedIUsers.Add(newIUser);

                        // Gọi hàm GetFormattedCookieStringAsync lấy chuỗi Cookie định dạng chuẩn và lưu kết quả
                        string cookieString = await GetFormattedCookieStringAsync(context).ConfigureAwait(false);
                        exportedCookies.Add(cookieString);

                        popupVerifiedPages.Add(new ViaManager.Models.ManagedPage
                        {
                            PageId = newIUser,
                            Name = !string.IsNullOrWhiteSpace(targetPageName) && !targetPageName.StartsWith("Mục ") ? targetPageName : newIUser,
                            ParentViaUid = id,
                            BusinessManager = bm,
                            VerificationStatus = ViaManager.Models.VerificationStatus.Verified,
                            VerifiedAt = DateTime.UtcNow,
                            RawCookieSnapshot = cookieString
                        });

                        LogConsole(id, $"[SUCCESS] Đã switch thành công Page: {newIUser} ({targetPageName}) ({exportedCookies.Count}/{totalElements - 1}).");
                    }
                    catch (Exception ex)
                    {
                        LogConsole(id, $"[LỖI] Lỗi khi xử lý mục {i}: {ex.Message}");
                    }
                }

                if (popupVerifiedPages.Count > 0)
                {
                    _pageRepository.AssignPagesAtomic(bm, id, popupVerifiedPages);
                }
            }
            else
            {
                LogConsole(id, $"[POPUP SCAN] Popup có {totalElements} phần tử (không đủ điều kiện switch). Chuyển sang quét trang /pages.");
            }

            // =========================================================================
            // 2. CƠ CHẾ DỰ PHÒNG TẠI TRANG /PAGES (NẾU POPUP THẤT BẠI HOẶC 0 PAGE)
            // =========================================================================
            if (exportedCookies.Count == 0)
            {
                LogConsole(id, "[DỰ PHÒNG] Bắt đầu quét danh sách Page qua trang quản lý: https://www.facebook.com/pages/?category=your_pages ...");
                UpdateStatusLabel($"UID: {id} - Đang quét dự phòng tại /pages...");

                var fallbackPages = await ExtractManagedPageIdsAsync(page, id).ConfigureAwait(false);

                // TUYỆT ĐỐI CHỈ lấy các ID thỏa mãn 2 điều kiện: Bắt đầu bằng 615... VÀ có độ dài 14-15 chữ số.
                // (Các ID đầu 179... hoặc 12... ở trang /pages phải bị Regex gạt bỏ hoàn toàn).
                var valid615Pages = fallbackPages
                    .Where(pid => !string.IsNullOrWhiteSpace(pid))
                    .Select(pid => pid.Trim())
                    .Where(pid => pid.StartsWith("615") && (pid.Length == 14 || pid.Length == 15) && pid.All(char.IsDigit))
                    .Where(pid => !pid.Equals(id, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                LogConsole(id, $"[KẾT QUẢ QUÉT /PAGES] Tìm thấy {valid615Pages.Count} Page Profile ID 615... chuẩn.");
                foreach (var pageId in valid615Pages)
                {
                    Console.WriteLine($"[{id}] ---> Page ID (615...): {pageId}");
                    LogConsole(id, $"---> Page ID (615...): {pageId}");
                }

                if (valid615Pages.Count > 0)
                {
                    UpdateStatusLabel($"UID: {id} - Tìm thấy {valid615Pages.Count} Page 615..., đang switch profile...");
                    var switchResults = await _pageSessionService.ProcessViaPagesAsync(session, account, valid615Pages, progressReporter).ConfigureAwait(false);
                    foreach (var r in switchResults.Where(r => r.IsSuccess && r.Snapshot != null))
                    {
                        exportedCookies.Add(r.Snapshot!.RawCookieHeader);
                    }
                }
            }

            int verifiedCount = exportedCookies.Count;
            SetRowPageCount(row, verifiedCount);

            SetRowStatus(row, id, "live");
            _accountRepository.UpdateAccountStatus(id, "live");

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
    // Master Prompt Quy chuẩn 5: Hàm nối chuỗi Cookie chuẩn Header
    private async Task<string> GetFormattedCookieStringAsync(IBrowserContext context)
    {
        var cookies = await context.CookiesAsync(new[] { "https://www.facebook.com" }).ConfigureAwait(false);
        return string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
    }

    private static bool IsValidPageItem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length > 120) return false;

        string lower = trimmed.ToLowerInvariant();
        string[] spamKeywords = new[]
        {
            "chưa đọc", "đăng nhập", "bình luận", "cập nhật",
            "thông báo trước đó", "tất cả thông báo", "unread", "comment",
            "nhắc đến bạn", "thích bài viết", "bày tỏ cảm xúc", "đã thích",
            "chia sẻ bài viết", "đã đăng", "vừa xong", "phút trước", "giờ trước",
            "ngày trước", "hôm qua", "tuần trước",
            "đánh dấu là đã đọc", "cài đặt thông báo",
            "cài đặt & quyền riêng tư", "trợ giúp & hỗ trợ", "màn hình & trợ năng",
            "đóng góp ý kiến", "đăng xuất", "log out",
            "tạo trang", "create page", "quản lý trang",
            "đóng", "close", "quay lại", "back"
        };

        foreach (var spam in spamKeywords)
        {
            if (lower.Contains(spam)) return false;
        }

        if (trimmed.All(char.IsDigit)) return false;

        return true;
    }

    private static string ExtractCleanPageName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrEmpty(l))
                        .ToList();
        return lines.Count > 0 ? lines[0] : text.Trim();
    }

    private async Task<bool> OpenProfileSwitcherDialogAsync(IPage page, string viaUid)
    {
        try
        {
            // Đóng bất kỳ popup Thông báo / Tin nhắn / Menu rác nào nếu đang mở
            var openDialogs = page.Locator("div[role='dialog']:visible");
            if (await openDialogs.CountAsync().ConfigureAwait(false) > 0)
            {
                bool isNotification = await page.EvaluateAsync<bool>(@"() => {
                    const dialogs = document.querySelectorAll('div[role=""dialog""]');
                    for (const d of dialogs) {
                        const txt = (d.innerText || '').toLowerCase();
                        const aria = (d.getAttribute('aria-label') || '').toLowerCase();
                        if (aria.includes('thông báo') || aria.includes('notification') || aria.includes('tin nhắn') || aria.includes('messenger')) return true;
                        if (txt.includes('thông báo trước đó') || txt.includes('tất cả thông báo') || txt.includes('đánh dấu tất cả là đã đọc')) return true;
                    }
                    return false;
                }").ConfigureAwait(false);

                if (isNotification)
                {
                    await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }

            // Kiểm tra xem hộp thoại Profile Switcher div[role='dialog'] đã đang mở và có sẵn danh sách hay chưa
            var existingDialog = page.Locator("div[role='dialog']:visible");
            if (await existingDialog.CountAsync().ConfigureAwait(false) > 0)
            {
                var seeAllInside = page.Locator("div[role='dialog'] div[role='button']:has-text('Xem tất cả trang cá nhân'):visible, div[role='dialog'] div[role='button']:has-text('See all profiles'):visible, div[role='dialog'] div[role='button']:has-text('Xem tất cả các trang cá nhân'):visible");
                if (await seeAllInside.CountAsync().ConfigureAwait(false) > 0)
                {
                    try { await seeAllInside.Nth(0).ClickAsync(new() { Timeout = 3000 }).ConfigureAwait(false); } catch { }
                    await Task.Delay(1000).ConfigureAwait(false);
                }

                var existingItems = await GetDialogProfileElementsAsync(page).ConfigureAwait(false);
                if (existingItems.Count > 1)
                {
                    return true;
                }
            }

            // Định vị chính xác nút Avatar người dùng trên banner:
            // Tuyệt đối KHÔNG click nhầm quả chuông (Thông báo) hoặc Messenger
            bool avatarClicked = false;
            var bannerButtons = page.Locator("div[role='banner'] div[role='button']:visible, div[role='banner'] [role='button']:visible");
            int btnCount = await bannerButtons.CountAsync().ConfigureAwait(false);

            for (int b = 0; b < btnCount; b++)
            {
                var btn = bannerButtons.Nth(b);
                string aria = (await btn.GetAttributeAsync("aria-label").ConfigureAwait(false) ?? "").Trim();
                if (string.IsNullOrEmpty(aria)) continue;

                string ariaLower = aria.ToLowerInvariant();
                // Bỏ qua tuyệt đối quả chuông (Thông báo), Messenger, Tin nhắn, Menu 9 chấm, Tìm kiếm
                if (ariaLower.Contains("thông báo") || ariaLower.Contains("notification") ||
                    ariaLower.Contains("tin nhắn") || ariaLower.Contains("messenger") ||
                    ariaLower.Contains("menu") || ariaLower.Contains("tìm kiếm") || ariaLower.Contains("search"))
                {
                    continue;
                }

                if (ariaLower.Contains("trang cá nhân") || ariaLower.Contains("your profile") ||
                    ariaLower.Contains("tài khoản") || ariaLower.Contains("account"))
                {
                    try
                    {
                        await btn.ClickAsync(new() { Timeout = 5000 }).ConfigureAwait(false);
                        avatarClicked = true;
                        break;
                    }
                    catch { }
                }
            }

            // Fallback: Nếu không match được qua aria-label tiếng Việt/Anh, lấy nút góc phải ngoài cùng của banner (luôn là Avatar)
            if (!avatarClicked)
            {
                avatarClicked = await page.EvaluateAsync<bool>(@"() => {
                    const banner = document.querySelector('div[role=""banner""]');
                    if (!banner) return false;
                    const buttons = Array.from(banner.querySelectorAll('div[role=""button""], [role=""button""]')).filter(b => {
                        const aria = (b.getAttribute('aria-label') || '').toLowerCase();
                        if (aria.includes('thông báo') || aria.includes('notification') || aria.includes('messenger') || aria.includes('tin nhắn') || aria.includes('menu') || aria.includes('tìm kiếm')) return false;
                        const rect = b.getBoundingClientRect();
                        return rect.width > 20 && rect.height > 20 && rect.top < 100 && rect.right > 0;
                    });
                    if (buttons.length > 0) {
                        buttons.sort((a, b) => b.getBoundingClientRect().right - a.getBoundingClientRect().right);
                        buttons[0].click();
                        return true;
                    }
                    return false;
                }").ConfigureAwait(false);
            }

            if (!avatarClicked)
            {
                return false;
            }

            await Task.Delay(1200).ConfigureAwait(false);

            // Click vào nút "Xem tất cả trang cá nhân" / "See all profiles" nếu có
            var seeAllProfilesBtn = page.Locator("div[role='button']:has-text('Xem tất cả trang cá nhân'):visible, div[role='button']:has-text('See all profiles'):visible, div[role='button']:has-text('Xem tất cả các trang cá nhân'):visible, span:has-text('Xem tất cả trang cá nhân'):visible, span:has-text('See all profiles'):visible");
            if (await seeAllProfilesBtn.CountAsync().ConfigureAwait(false) > 0)
            {
                try
                {
                    await seeAllProfilesBtn.Nth(0).ClickAsync(new() { Timeout = 4000 }).ConfigureAwait(false);
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                catch { }
            }

            // Chờ div[role='dialog'] xuất hiện (tối đa 5s)
            var dialog = await page.WaitForSelectorAsync("div[role='dialog']:visible", new() { Timeout = 5000 }).ConfigureAwait(false);
            if (dialog != null)
            {
                // Kiểm tra dialog vừa mở có phải bảng Thông báo hay không; nếu là Thông báo thì đóng ngay
                bool isNotification = await page.EvaluateAsync<bool>(@"() => {
                    const dialogs = document.querySelectorAll('div[role=""dialog""]');
                    for (const d of dialogs) {
                        const txt = (d.innerText || '').toLowerCase();
                        const aria = (d.getAttribute('aria-label') || '').toLowerCase();
                        if (aria.includes('thông báo') || aria.includes('notification')) return true;
                        if (txt.includes('thông báo trước đó') || txt.includes('đánh dấu tất cả là đã đọc')) return true;
                    }
                    return false;
                }").ConfigureAwait(false);

                if (isNotification)
                {
                    await page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
                    await Task.Delay(500).ConfigureAwait(false);
                    return false;
                }

                // Nếu có nút "Xem tất cả trang cá nhân" nằm ngay bên trong dialog vừa mở, click tiếp
                var seeAllInside = page.Locator("div[role='dialog'] div[role='button']:has-text('Xem tất cả trang cá nhân'):visible, div[role='dialog'] div[role='button']:has-text('See all profiles'):visible, div[role='dialog'] div[role='button']:has-text('Xem tất cả các trang cá nhân'):visible");
                if (await seeAllInside.CountAsync().ConfigureAwait(false) > 0)
                {
                    try
                    {
                        await seeAllInside.Nth(0).ClickAsync(new() { Timeout = 3000 }).ConfigureAwait(false);
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    catch { }
                }

                // Đưa vị trí cuộn về đỉnh (top = 0)
                await page.EvaluateAsync(@"() => {
                    let dialogs = document.querySelectorAll('div[role=""dialog""]');
                    if (dialogs.length > 0) {
                        let scrollers = dialogs[0].querySelectorAll('div[style*=""overflow-y: auto""], div[style*=""overflow-y: scroll""], div[style*=""overflow: auto""]');
                        for (let sc of scrollers) sc.scrollTop = 0;
                    }
                }").ConfigureAwait(false);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("OpenProfileSwitcherDialog", $"Lỗi mở Profile Switcher Dialog: {ex.Message}");
            return false;
        }
    }

    private async Task<IReadOnlyList<ILocator>> GetDialogProfileElementsAsync(IPage page)
    {
        try
        {
            // Selector chuẩn theo Prompt: chỉ bắt các thẻ listitem chứa radio hoặc button chuyển profile
            var rawElements = await page.Locator("div[role='dialog'] div[role='listitem']:has(div[role='radio']), div[role='dialog'] div[role='listitem']:has(div[role='button'])").AllAsync().ConfigureAwait(false);
            if (rawElements.Count == 0)
            {
                // Dự phòng: bắt div[role='listitem'] hiển thị
                rawElements = await page.Locator("div[role='dialog'] div[role='listitem']:visible").AllAsync().ConfigureAwait(false);
            }

            var validList = new List<ILocator>();
            for (int idx = 0; idx < rawElements.Count; idx++)
            {
                var el = rawElements[idx];
                try
                {
                    string text = (await el.InnerTextAsync().ConfigureAwait(false) ?? "").Trim();
                    if (IsValidPageItem(text))
                    {
                        validList.Add(el);
                    }
                }
                catch { }
            }

            return validList;
        }
        catch
        {
            return Array.Empty<ILocator>();
        }
    }

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

    // ==========================================
    // EXTRACT PAGES & OTHER EVENT HANDLERS
    // ==========================================
    private async Task<List<string>> ExtractManagedPageIdsAsync(IPage page, string viaUid)
    {
        var pageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Điều hướng tới trang danh sách Pages
            await page.GotoAsync("https://www.facebook.com/pages/?category=your_pages", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 25000 });
            // Cuộn trang từ từ xuống cuối để kích hoạt Lazy Loading tải toàn bộ danh sách Page
            long previousHeight = 0;
            int noChangeCount = 0;
            int scrollAttempts = 0;
            const int maxScrollAttempts = 40;

            while (noChangeCount < 2 && scrollAttempts < maxScrollAttempts)
            {
                scrollAttempts++;
                long currentHeight = await page.EvaluateAsync<long>(@"() => {
                    window.scrollTo(0, document.body.scrollHeight || document.documentElement.scrollHeight);
                    const mainEl = document.querySelector('div[role=""main""]') || document.scrollingElement || document.documentElement;
                    if (mainEl && mainEl.scrollHeight > mainEl.clientHeight) {
                        mainEl.scrollTop = mainEl.scrollHeight;
                    }
                    return Math.max(document.body.scrollHeight || 0, document.documentElement.scrollHeight || 0, mainEl ? mainEl.scrollHeight : 0);
                }");

                if (currentHeight == previousHeight)
                {
                    noChangeCount++;
                }
                else
                {
                    noChangeCount = 0;
                    previousHeight = currentHeight;
                }

                await Task.Delay(1000);
            }

            // [YÊU CẦU DỰ PHÒNG]: BÓC TÁCH CARD-BY-CARD TRÊN TRANG /PAGES (CHỈ LẤY ID 615... 14-15 CHỮ SỐ)
            var extractedIds = await page.EvaluateAsync<string[]>(@"() => {
                const mainEl = document.querySelector('div[role=""main""]') || document.querySelector('main') || document.body;
                const result = [];
                const seen = new Set();
                
                // Loại trừ toàn bộ link bài viết, ảnh, video, story, settings, v.v.
                const excludeKeywords = /friends|watch|marketplace|groups|gaming|bookmarks|events|saved|memories|messages|notifications|settings|privacy|policies|help|create|invites|category=your_pages|ads|business|login|recover|user|\/posts\/|\/photos\/|\/videos\/|story_fbid|set=a\.|fbid=|\/stories\/|\/permalink\//i;

                // 1. Quét TẤT CẢ các thẻ <a> bên trong khu vực nội dung chính
                const rawLinks = Array.from(mainEl.querySelectorAll('a[href]'));
                const linkItems = [];

                for (const a of rawLinks) {
                    const href = a.getAttribute('href') || '';
                    if (!href || excludeKeywords.test(href)) continue;

                    // TUYỆT ĐỐI CHỈ lấy ID bắt đầu bằng 615... và có độ dài 14-15 chữ số
                    const m615 = href.match(/(?:id=|\/profile\.php\?id=|\/pages\/[^\/]+\/|\/pages\/|\/)(615\d{11,12})(?:[/?#&]|$)/);
                    if (m615 && m615[1]) {
                        const pageId = m615[1];
                        const rect = a.getBoundingClientRect();
                        linkItems.push({
                            id: pageId,
                            top: Math.round(rect.top + window.scrollY)
                        });
                    }
                }

                if (linkItems.length > 0) {
                    // Sắp xếp các link theo chiều dọc từ trên xuống dưới
                    linkItems.sort((x, y) => x.top - y.top);

                    for (const item of linkItems) {
                        if (!seen.has(item.id)) {
                            seen.add(item.id);
                            result.push(item.id);
                        }
                    }
                }

                return result;
            }").ConfigureAwait(false);

            string cleanViaUid = viaUid.Trim();

            if (extractedIds != null && extractedIds.Length > 0)
            {
                foreach (var pid in extractedIds)
                {
                    if (string.IsNullOrWhiteSpace(pid) || !pid.All(char.IsDigit))
                        continue;
                    if (!pid.StartsWith("615") || (pid.Length != 14 && pid.Length != 15))
                        continue;
                    if (pid.Equals(cleanViaUid, StringComparison.OrdinalIgnoreCase))
                        continue;
                    pageIds.Add(pid);
                }
            }

            // Dự phòng: nếu evaluate theo card không ra ID nào, quét theo link trực tiếp
            if (pageIds.Count == 0)
            {
                var mainLocator = page.Locator("div[role='main'] a[href], main a[href]");
                int linkCount = await mainLocator.CountAsync();
                string[] hrefs = linkCount > 0
                    ? await mainLocator.EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href') || '')")
                    : await page.Locator("a[href]").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href') || '')");

                var idRegex615 = new System.Text.RegularExpressions.Regex(
                    @"(?:id=|\/profile\.php\?id=|\/pages\/[^\/]+\/|\/pages\/|\/)(615\d{11,12})(?:[/?#&]|$)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

                var excludedUrlKeywords = new[]
                {
                    "/friends", "/watch", "/marketplace", "/groups", "/gaming", "/bookmarks",
                    "/events", "/saved", "/memories", "/messages", "/notifications", "/settings",
                    "/privacy", "/policies", "/help", "/create", "/invites", "category=your_pages",
                    "/ads", "/business", "/login", "/recover", "/user/",
                    "/posts/", "/photos/", "/videos/", "story_fbid", "set=a.", "fbid=", "/stories/", "/permalink/"
                };

                foreach (var rawHref in hrefs)
                {
                    if (string.IsNullOrWhiteSpace(rawHref)) continue;
                    string href = rawHref.Trim();
                    if (excludedUrlKeywords.Any(kw => href.Contains(kw, StringComparison.OrdinalIgnoreCase))) continue;

                    var m615 = idRegex615.Match(href);
                    if (m615.Success)
                    {
                        string pid = m615.Groups[1].Value.Trim();
                        if (pid.StartsWith("615") && (pid.Length == 14 || pid.Length == 15) && pid.All(char.IsDigit) && !pid.Equals(cleanViaUid, StringComparison.OrdinalIgnoreCase))
                        {
                            pageIds.Add(pid);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Lỗi quét Pages: {ex.Message}");
        }

        return DeduplicatePageIdPairs(pageIds.ToList());
    }

    /// <summary>
    /// Lọc danh sách Page ID theo yêu cầu cơ chế dự phòng trang /pages:
    /// TUYỆT ĐỐI CHỈ lấy các ID thỏa mãn 2 điều kiện:
    /// 1. Bắt đầu bằng 615...
    /// 2. Có độ dài 14-15 chữ số.
    /// (Toàn bộ ID đầu 179... hoặc 12... bị loại bỏ hoàn toàn).
    /// </summary>
    private static List<string> DeduplicatePageIdPairs(List<string> rawIds)
    {
        if (rawIds == null || rawIds.Count == 0)
            return new List<string>();

        return rawIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Where(id => id.StartsWith("615") && (id.Length == 14 || id.Length == 15) && id.All(char.IsDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
