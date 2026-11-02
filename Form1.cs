using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;

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

    // Khởi tạo điều phối đa luồng động (Dynamic Threading) theo tổng số Page quét được
    private SemaphoreSlim _concurrencySemaphore = new SemaphoreSlim(1, 1);

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
        _connectionString = $"Data Source={_dbPath}";

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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khởi tạo cơ sở dữ liệu: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ==========================================
    // 1. HÀM TÍNH TỔNG LUỒNG & CẬP NHẬT GIAO DIỆN (UI)
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
            btnCampaign.Text = $"Chạy {totalPageCount} Luồng";
            return totalPageCount;
        }
        catch
        {
            int fallback = GlobalState.ProcessedPageIds.Count;
            btnCampaign.Text = $"Chạy {fallback} Luồng";
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
        _ = RunViaProfileAsync(row, id, cookie, pass, ma2fa, bm, showDialog: true, isHeadless: false);
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
        _ = RunViaProfileAsync(row, id, cookie, pass, ma2fa, bm, showDialog: true, isHeadless: false);
    }

    // ==========================================
    // CẬP NHẬT HÀM CHẠY VIA (RunViaProfileAsync) THEO 3 YÊU CẦU CỐT LÕI
    // ==========================================
    private async Task RunViaProfileAsync(DataGridViewRow row, string id, string rawCookie, string pass, string ma2fa, string bm, bool showDialog = false, bool isHeadless = true)
    {
        SetRowStatus(row, id, "running");
        _accountRepository.UpdateAccountStatus(id, "running");
        LogConsole(id, $"Bắt đầu chạy VIA. Trạng thái: running. Headless={isHeadless}");
        UpdateStatusLabel($"UID: {id} - Đang khởi động trình duyệt...");

        var account = new ViaManager.Models.ViaAccount
        {
            Uid = id,
            Password = pass,
            TwoFactorSecret = ma2fa,
            Cookie = rawCookie,
            BusinessManager = bm,
            Status = "running"
        };

        try
        {
            await using var session = await _browserProfileService.AcquireSessionAsync(id, isHeadless).ConfigureAwait(false);

            var authResult = await _viaAuthService.EnsureAuthenticatedAsync(session, account).ConfigureAwait(false);
            if (!authResult.IsSuccess)
            {
                string statusToSet = authResult.ErrorCode == "CHECKPOINT" ? "check" : "check";
                SetRowStatus(row, id, statusToSet);
                _accountRepository.UpdateAccountStatus(id, statusToSet);
                _pageRepository.ReleaseAssignedPages(id);
                LogConsole(id, $"[XÁC THỰC THẤT BẠI] {authResult.Message}");
                UpdateStatusLabel($"UID: {id} - Xác thực thất bại: {authResult.Message}");
                if (showDialog)
                {
                    this.Invoke(() => MessageBox.Show($"Xác thực thất bại cho UID {id}: {authResult.Message}", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                }
                return;
            }

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
                .Where(r => r.IsSuccess && r.Snapshot != null)
                .Select(r => $"{r.PageId} | {r.Snapshot!.RawCookieHeader}")
                .ToList();

            if (exportedCookies.Count > 0)
            {
                string firstCookie = switchResults.First(r => r.IsSuccess).Snapshot!.RawCookieHeader;
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

    // ==========================================
    // 3. KHỞI TẠO ĐIỀU PHỐI ĐA LUỒNG ĐỘNG (StartCampaignAsync)
    // ==========================================
    private async Task StartCampaignAsync(List<DataGridViewRow> targetVias)
    {
        if (targetVias == null || targetVias.Count == 0)
        {
            MessageBox.Show("Không có tài khoản Via nào khả dụng để chạy chiến dịch (tất cả đang running, die, check hoặc lock chưa đủ 2 tiếng)!",
                "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // [YÊU CẦU 3]: Tính toán lại totalPageCount trước khi bắt đầu vòng lặp chạy Task
        int totalPageCount = UpdateDynamicThreadCount();

        // Khởi tạo lại Semaphore với số lượng động thay vì fix cứng con số 70
        try { _concurrencySemaphore.Dispose(); } catch { }
        _concurrencySemaphore = new SemaphoreSlim(totalPageCount > 0 ? totalPageCount : 1);

        int dynamicThreadCount = totalPageCount > 0 ? totalPageCount : 1;

        btnCampaign.Enabled = false;
        btnCampaign.Text = $"⏳ Đang chạy {dynamicThreadCount}L...";
        UpdateStatusLabel($"Bắt đầu chiến dịch: {targetVias.Count} Via (Điều phối động tối đa {dynamicThreadCount} luồng song song theo {totalPageCount} Page)...");

        var tasks = new List<Task>();

        foreach (var row in targetVias)
        {
            string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
            string pass = row.Cells["PASS"].Value?.ToString()?.Trim() ?? "";
            string ma2fa = row.Cells["Ma2FA"].Value?.ToString()?.Trim() ?? "";
            string bm = row.Cells["BM"].Value?.ToString()?.Trim() ?? "Bmv1";
            string cookie = row.Cells["Cookie"].Value?.ToString()?.Trim() ?? "";

            var task = Task.Run(async () =>
            {
                // Bắt buộc chờ Semaphore cấp phép theo số lượng luồng động
                await _concurrencySemaphore.WaitAsync();
                try
                {
                    // Chạy hàng loạt (Chạy X Luồng) -> isHeadless: true để chạy ngầm tiết kiệm RAM
                    await RunViaProfileAsync(row, id, cookie, pass, ma2fa, bm, showDialog: false, isHeadless: true);
                }
                finally
                {
                    // Luôn giải phóng Semaphore trong khối finally của Task
                    _concurrencySemaphore.Release();
                }
            });

            tasks.Add(task);
        }

        try
        {
            await Task.WhenAll(tasks);
            UpdateStatusLabel($"Chiến dịch hoàn tất! Đã xử lý {targetVias.Count} tài khoản.");
            MessageBox.Show($"Đã hoàn thành chạy chiến dịch cho {targetVias.Count} tài khoản!",
                "Chiến dịch hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UpdateStatusLabel($"Lỗi chiến dịch: {ex.Message}");
            MessageBox.Show($"Có lỗi trong quá trình thực hiện chiến dịch: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnCampaign.Enabled = true;
            UpdateDynamicThreadCount();
            UpdateCountLabels();
        }
    }

    private async void btnCampaign_Click(object? sender, EventArgs e)
    {
        var availableVias = GetAvailableVias();
        if (availableVias.Count == 0)
        {
            MessageBox.Show("Không tìm thấy tài khoản nào khả dụng để chạy!\n\n(Tất cả tài khoản đang ở trạng thái 'running', 'check', 'die' hoặc đang 'lock' nghỉ chưa đủ 2 tiếng).",
                "Không có Via khả dụng", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int totalPageCount = UpdateDynamicThreadCount();
        int dynamicThreadCount = totalPageCount > 0 ? totalPageCount : 1;

        var confirm = MessageBox.Show(
            $"Tìm thấy {availableVias.Count} tài khoản khả dụng (bao gồm các Via 'lock' đã nghỉ trên 2 tiếng được tự động mở khóa sang 'live').\n\nTổng số Page ghi nhận: {totalPageCount} Page.\nSố luồng song song tự động khởi tạo: {dynamicThreadCount} Luồng.\n\nBạn có muốn bắt đầu chạy chiến dịch không?",
            "Xác nhận chạy chiến dịch", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        await StartCampaignAsync(availableVias);
    }

    // ==========================================
    // 2 & 3. TÍNH NĂNG "QUÉT TỔNG PAGE" CHO VIA RUN
    // ==========================================
    private async void btnScanTotalPages_Click(object? sender, EventArgs e)
    {
        // [YÊU CẦU 1]: RESET BỘ NHỚ ẢO TRƯỚC KHI QUÉT BATCH
        // Làm mới bộ nhớ để chia lại Page từ đầu mỗi khi bấm nút quét
        GlobalState.ProcessedPageIds.Clear();

        // BỘ LỌC NGHIÊM NGẶT:
        // 1. Cột 'Vai Trò' có giá trị là "Run" (Bỏ qua hoàn toàn giá trị "Reg").
        // 2. Cột 'Trạng Thái' khác "die" và khác "check".
        var targetRows = new List<DataGridViewRow>();

        foreach (DataGridViewRow row in dgvAccounts.Rows)
        {
            if (row.IsNewRow) continue;

            string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(id)) continue;

            string vaiTro = row.Cells["VaiTro"].Value?.ToString()?.Trim() ?? "";
            string trangThai = row.Cells["TrangThai"].Value?.ToString()?.Trim() ?? "";

            // Điều kiện 1: Vai Trò phải là "Run" (bỏ qua hoàn toàn "Reg")
            if (!vaiTro.Equals("Run", StringComparison.OrdinalIgnoreCase))
                continue;

            // Điều kiện 2: Trạng Thái khác "die" và khác "check"
            if (trangThai.Equals("die", StringComparison.OrdinalIgnoreCase) ||
                trangThai.Equals("check", StringComparison.OrdinalIgnoreCase))
                continue;

            targetRows.Add(row);
        }

        if (targetRows.Count == 0)
        {
            MessageBox.Show("Không tìm thấy tài khoản nào thỏa mãn bộ lọc để quét Page!\n\nĐiều kiện lọc:\n- Vai Trò = 'Run' (bỏ qua 'Reg')\n- Trạng Thái KHÁC 'die' và KHÁC 'check'.",
                "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Tìm thấy {targetRows.Count} tài khoản thỏa mãn bộ lọc (Vai Trò = 'Run' và Trạng Thái != 'die' / 'check').\n\nBạn có muốn bắt đầu quét tổng số Page lần lượt cho các Via này không?",
            "Xác nhận quét tổng Page", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        btnScanTotalPages.Enabled = false;
        int currentIndex = 0;
        int totalPagesAll = 0;

        try
        {
            foreach (var row in targetRows)
            {
                currentIndex++;
                string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
                string pass = row.Cells["PASS"].Value?.ToString()?.Trim() ?? "";
                string ma2fa = row.Cells["Ma2FA"].Value?.ToString()?.Trim() ?? "";
                string cookie = row.Cells["Cookie"].Value?.ToString()?.Trim() ?? "";

                btnScanTotalPages.Text = $"⏳ Quét ({currentIndex}/{targetRows.Count})...";
                UpdateStatusLabel($"Đang quét tổng số Page cho UID {id} ({currentIndex}/{targetRows.Count})...");

                // Chạy quét hàng loạt -> isHeadless: true để chạy ngầm tiết kiệm RAM
                int pageCount = await ScanViaPageCountAsync(row, id, cookie, pass, ma2fa, isHeadless: true);
                totalPagesAll += pageCount;

                // Cập nhật real-time số luồng trên nút Chạy sau khi mỗi Via quét xong
                UpdateDynamicThreadCount();
            }

            UpdateStatusLabel($"Hoàn tất quét tổng Page! Đã xử lý {targetRows.Count} tài khoản, tổng số Page: {totalPagesAll}.");
            MessageBox.Show($"Đã quét thành công {targetRows.Count} tài khoản!\n\nTổng số Page ghi nhận: {totalPagesAll} Page.",
                "Hoàn tất quét tổng Page", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UpdateStatusLabel($"Lỗi khi quét tổng Page: {ex.Message}");
            MessageBox.Show($"Có lỗi trong quá trình quét: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnScanTotalPages.Enabled = true;
            btnScanTotalPages.Text = "📊 Quét Tổng Page";
            UpdateDynamicThreadCount();
        }
    }

    private async Task<int> ScanViaPageCountAsync(DataGridViewRow row, string id, string rawCookie, string pass, string ma2fa, bool isHeadless = true)
    {
        IBrowserContext? context = null;
        IPlaywright? playwright = null;
        string currentViaId = id?.Trim() ?? "";

        try
        {
            // [RÀNG BUỘC 1]: RESET BIẾN ĐẾM TRONG TỪNG LUỒNG
            // Ngay khi bắt đầu xử lý một Via mới (đầu khối try của hàm quét), bắt buộc khởi tạo int validPageCount = 0;
            // Không dùng biến toàn cục cho việc đếm này để tránh dính số liệu của Via trước.
            int validPageCount = 0;

            // Khởi tạo thư mục Profile cố định cho UID
            string profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserProfiles", currentViaId);
            Directory.CreateDirectory(profilePath);

            playwright = await Playwright.CreateAsync();

            string channel = File.Exists(@"C:\Program Files\Google\Chrome\Application\chrome.exe") ||
                             File.Exists(@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe")
                             ? "chrome" : "msedge";

            context = await playwright.Chromium.LaunchPersistentContextAsync(
                profilePath,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = isHeadless,
                    Channel = channel,
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--start-maximized",
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--ignore-certificate-errors",
                        "--enable-features=NetworkService,NetworkServiceInProcess",
                        "--dns-prefetch-disable"
                    },
                    ViewportSize = ViewportSize.NoViewport
                }
            );

            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

            // 1. Tiêm Cookie vào trình duyệt nếu có
            if (!string.IsNullOrWhiteSpace(rawCookie))
            {
                var cookieList = CookieHelper.ParseCookieString(rawCookie);
                if (cookieList.Count > 0)
                {
                    await context.AddCookiesAsync(cookieList);
                }
            }

            // 2. Điều hướng tới Facebook
            await page.GotoAsync("https://www.facebook.com", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
            await Task.Delay(3000);

            string currentUrl = page.Url;
            if (currentUrl.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                SetRowStatus(row, currentViaId, "check");
                AccountRepository.UpdateAccountStatus(_connectionString, currentViaId, "check");
                AccountRepository.ReleaseAssignedPages(_connectionString, currentViaId);
                SetRowPageCount(row, validPageCount);
                AccountRepository.UpdateAccountPageCount(_connectionString, currentViaId, validPageCount);
                return validPageCount;
            }

            var cookies = await context.CookiesAsync();
            bool hasCUser = cookies.Any(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
            string cookieCUser = cookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

            // Nếu cookie die thì fallback đăng nhập bằng ID / PASS / 2FA
            if (!hasCUser && !string.IsNullOrWhiteSpace(pass))
            {
                var emailInput = page.Locator("input[name='email'], #email");
                if (await emailInput.CountAsync() > 0)
                {
                    await emailInput.First.FillAsync(currentViaId);
                    await page.FillAsync("input[name='pass'], #pass", pass);

                    var loginBtn = page.Locator("button[name='login'], button[type='submit'], button:has-text('Đăng nhập'), button:has-text('Log In')");
                    if (await loginBtn.CountAsync() > 0)
                        await loginBtn.First.ClickAsync();
                    else
                        await page.Keyboard.PressAsync("Enter");

                    try
                    {
                        await page.WaitForURLAsync(u => u.Contains("checkpoint") || u.Contains("home") || u.Contains("two_factor") || u.Contains("login"), new() { Timeout = 15000 });
                    }
                    catch { }

                    await Task.Delay(3000);

                    var otpInputs = page.Locator("input[name='approvals_code'], input[type='tel'], input[placeholder*='code'], input[placeholder*='mã']");
                    if (await otpInputs.CountAsync() > 0 && !string.IsNullOrWhiteSpace(ma2fa))
                    {
                        string otp = TotpGenerator.GenerateTotp(ma2fa);
                        if (!string.IsNullOrEmpty(otp))
                        {
                            await otpInputs.First.FillAsync(otp);
                            var submitBtn = page.Locator("button[type='submit'], #checkpointSubmitButton, button[id*='checkpoint']");
                            if (await submitBtn.CountAsync() > 0)
                                await submitBtn.First.ClickAsync();
                            else
                                await page.Keyboard.PressAsync("Enter");

                            await Task.Delay(4000);
                        }
                    }
                }

                cookies = await context.CookiesAsync();
                hasCUser = cookies.Any(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                cookieCUser = cookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

                if (page.Url.Contains("checkpoint", StringComparison.OrdinalIgnoreCase) || !hasCUser)
                {
                    SetRowStatus(row, currentViaId, "check");
                    AccountRepository.UpdateAccountStatus(_connectionString, currentViaId, "check");
                    AccountRepository.ReleaseAssignedPages(_connectionString, currentViaId);
                    SetRowPageCount(row, validPageCount);
                    AccountRepository.UpdateAccountPageCount(_connectionString, currentViaId, validPageCount);
                    return validPageCount;
                }
            }

            // Cập nhật lại cookie mới nếu có
            string fullCookie = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
            SetRowCookie(row, fullCookie);
            AccountRepository.UpdateAccountCookie(_connectionString, currentViaId, fullCookie);

            // 3. Quét danh sách Page_ID của con Via này
            var managedPages = await ExtractManagedPageIdsAsync(page, currentViaId);

            // [RÀNG BUỘC 2]: RÀNG BUỘC ĐIỀU KIỆN LỌC PAGE ID (RẤT QUAN TRỌNG)
            foreach (var rawPid in managedPages)
            {
                if (string.IsNullOrWhiteSpace(rawPid))
                    continue;

                string extractedId = rawPid.Trim();

                // Kiểm tra tính hợp lệ: chuỗi số dài hơn 10 ký tự
                if (extractedId.Length <= 10 || !extractedId.All(char.IsDigit))
                    continue;

                // Màng lọc 1: Tuyệt đối không đếm ID của chính con Via đang chạy
                if (extractedId == currentViaId || 
                    string.Equals(extractedId, currentViaId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(cookieCUser) && string.Equals(extractedId, cookieCUser, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Màng lọc 2: Chống trùng ID với Via khác
                if (!GlobalState.TryAddPage(extractedId))
                {
                    continue;
                }

                // Chỉ khi qua lọt 2 màng lọc này mới thực hiện validPageCount++;
                validPageCount++;
            }

            // [RÀNG BUỘC 3]: RÀNG BUỘC CẬP NHẬT UI (DATAGRIDVIEW)
            // Dù validPageCount là 0 hay 100, BẮT BUỘC phải dùng chính biến validPageCount này để gán lại giá trị cho cột 'PageCount' của dòng đó trên DataGridView.
            // Lệnh cập nhật: row.Cells["PageCount"].Value = validPageCount;
            // Cập nhật tương ứng xuống CSDL SQLite (báo đúng số 0 nếu không có).
            SetRowPageCount(row, validPageCount);
            AccountRepository.UpdateAccountPageCount(_connectionString, currentViaId, validPageCount);

            // [QUAN TRỌNG]: Kết thúc quá trình quét của một Via, phải GIỮ NGUYÊN trạng thái là 'live' trên UI và Database
            // (TUYỆT ĐỐI KHÔNG chuyển thành 'lock' hay cập nhật LastRunTime ở bước này).
            SetRowStatus(row, currentViaId, "live");
            AccountRepository.UpdateAccountStatus(_connectionString, currentViaId, "live");

            UpdateStatusLabel($"UID: {currentViaId} - Quét thành công: {validPageCount} Page thực tế không trùng (Giữ trạng thái LIVE).");
            return validPageCount;
        }
        catch (Exception ex)
        {
            UpdateStatusLabel($"Lỗi khi quét Page UID {id}: {ex.Message}");
            SetRowPageCount(row, 0);
            AccountRepository.UpdateAccountPageCount(_connectionString, currentViaId, 0);
            return 0;
        }
        finally
        {
            // Đóng trình duyệt an toàn (context.CloseAsync) và giải phóng RAM để chạy tiếp Via khác
            try
            {
                if (context != null)
                {
                    await context.CloseAsync();
                    await context.DisposeAsync();
                }
            }
            catch { }

            try
            {
                playwright?.Dispose();
            }
            catch { }
        }
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
            await Task.Delay(3500);

            // [YÊU CẦU 1]: SỬA BỘ LỌC TÌM PAGE ID (THU HẸP VÙNG QUÉT LOCATOR)
            // Thu hẹp vùng quét, chỉ trích xuất từ những thẻ <a> có đường dẫn href hợp lệ trỏ về Profile Page trong vùng nội dung chính
            var mainLocator = page.Locator("div[role='main'] a[href], main a[href]");
            int linkCount = await mainLocator.CountAsync();

            string[] hrefs;
            if (linkCount > 0)
            {
                hrefs = await mainLocator.EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href') || '')");
            }
            else
            {
                // Dự phòng nếu giao diện Facebook không có role='main': chỉ trích xuất thẻ <a> chứa profile.php?id= hoặc /pages/
                hrefs = await page.Locator("a[href*='profile.php?id='], a[href*='/pages/category/'], a[href*='/pages/']").EvaluateAllAsync<string[]>(
                    "els => els.map(e => e.getAttribute('href') || '')"
                );
            }

            // Bắt buộc dùng Regex để lọc chuỗi ID: ID phải là chuỗi số dài hơn 10 ký tự
            // NPE Page luôn có dạng 615... (14-16 số) hoặc ID số dài > 10 trong URL profile.php / pages
            var idRegex = new System.Text.RegularExpressions.Regex(
                @"(?:id=|\/profile\.php\?id=|\/pages\/[^\/]+\/|\/pages\/|\/)(615\d{11,13}|\d{11,20})(?:[/?#&]|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );

            // Bỏ qua các URL menu điều hướng hệ thống để chống nhận diện nhầm
            var excludedUrlKeywords = new[]
            {
                "/friends", "/watch", "/marketplace", "/groups", "/gaming", "/bookmarks",
                "/events", "/saved", "/memories", "/messages", "/notifications", "/settings",
                "/privacy", "/policies", "/help", "/create", "/invites", "category=your_pages",
                "/ads", "/business", "/login", "/recover", "/user/"
            };

            string cleanViaUid = viaUid.Trim();

            foreach (var rawHref in hrefs)
            {
                if (string.IsNullOrWhiteSpace(rawHref))
                    continue;

                string href = rawHref.Trim();

                if (excludedUrlKeywords.Any(kw => href.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var match = idRegex.Match(href);
                if (match.Success)
                {
                    string pid = match.Groups[1].Value.Trim();

                    // Bắt buộc kiểm tra: ID phải là chuỗi số dài hơn 10 ký tự
                    if (pid.Length > 10 && pid.All(char.IsDigit))
                    {
                        // Bỏ qua nếu là UID cá nhân 1000... không thuộc đường dẫn /pages/
                        if (pid.StartsWith("1000") && !href.Contains("/pages/") && !href.Contains("profile.php?id="))
                            continue;

                        // Đặc biệt, nếu ID bắt được trùng với ID của Via gốc, BẮT BUỘC bỏ qua để chống đếm ảo
                        if (!pid.Equals(cleanViaUid, StringComparison.OrdinalIgnoreCase))
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

        return pageIds.ToList();
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
                    AccountRepository.ReleaseAssignedPages(_connectionString, id);
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
            using var cmdAssigned = conn.CreateCommand();
            cmdAssigned.Transaction = transaction;
            cmdAssigned.CommandText = "DELETE FROM AssignedPages WHERE Via_UID = $id;";
            var pAssignedId = cmdAssigned.Parameters.Add("$id", SqliteType.Text);

            using var cmdAccount = conn.CreateCommand();
            cmdAccount.Transaction = transaction;
            cmdAccount.CommandText = "DELETE FROM Accounts WHERE ID = $id;";
            var pAccountId = cmdAccount.Parameters.Add("$id", SqliteType.Text);

            int deletedCount = 0;
            foreach (var row in rowsToDelete)
            {
                string id = (row.Tag as string) ?? row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(id))
                {
                    pAssignedId.Value = id;
                    cmdAssigned.ExecuteNonQuery();

                    pAccountId.Value = id;
                    cmdAccount.ExecuteNonQuery();

                    deletedCount++;
                }
            }

            transaction.Commit();

            statusLabel.Text = $"Đã xóa {deletedCount} tài khoản 'die'.";
            UpdateCountLabels();
            LoadAccountsFromDb(txtSearch.Text);
            UpdateDynamicThreadCount();

            MessageBox.Show($"Đã xóa thành công {deletedCount} tài khoản 'die' khỏi SQLite!",
                "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            MessageBox.Show($"Lỗi khi xóa dòng: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnGetCookie_Click(object? sender, EventArgs e)
    {
        dgvAccounts.EndEdit();

        var targetRows = new List<DataGridViewRow>();
        foreach (DataGridViewRow row in dgvAccounts.SelectedRows)
        {
            if (!row.IsNewRow && !string.IsNullOrWhiteSpace(row.Cells["ID"].Value?.ToString()))
                targetRows.Add(row);
        }

        if (targetRows.Count == 0)
        {
            foreach (DataGridViewRow row in dgvAccounts.Rows)
            {
                if (!row.IsNewRow && !string.IsNullOrWhiteSpace(row.Cells["ID"].Value?.ToString()))
                    targetRows.Add(row);
            }
        }

        if (targetRows.Count == 0)
        {
            MessageBox.Show("Không có tài khoản nào trong bảng để chạy lấy Cookie!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnGetCookie.Enabled = false;
        int successCount = 0;

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            foreach (var row in targetRows)
            {
                string id = row.Cells["ID"].Value?.ToString()?.Trim() ?? "";

                statusLabel.Text = $"Đang cập nhật Cookie cho UID: {id}...";
                await Task.Delay(200);

                string existingCookie = row.Cells["Cookie"].Value?.ToString()?.Trim() ?? "";
                if (string.IsNullOrEmpty(existingCookie))
                {
                    LogConsole(id, "Tài khoản chưa có Cookie trong DB. Không tạo cookie giả (Zero Synthetic Invariant).");
                    continue;
                }

                string validCookie = existingCookie;
                row.Cells["Cookie"].Value = validCookie;
                if (string.IsNullOrEmpty(row.Cells["TrangThai"].Value?.ToString()))
                {
                    row.Cells["TrangThai"].Value = "live";
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE Accounts
                    SET Cookie = $cookie,
                        TrangThai = CASE WHEN TrangThai IS NULL OR TrangThai = '' THEN 'live' ELSE TrangThai END
                    WHERE ID = $id;
                ";
                cmd.Parameters.AddWithValue("$cookie", validCookie);
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();

                dgvAccounts.InvalidateRow(row.Index);
                successCount++;
            }

            statusLabel.Text = $"Hoàn tất lấy Cookie cho {successCount} tài khoản!";
            MessageBox.Show($"Đã cập nhật Cookie và lưu vào SQLite cho {successCount} tài khoản!",
                "Hoàn tất", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi chạy lấy Cookie: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnGetCookie.Enabled = true;
        }
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
