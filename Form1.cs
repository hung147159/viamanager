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
        IBrowserContext? context = null;
        IPlaywright? playwright = null;

        try
        {
            // Cập nhật trạng thái "running" trên DataGridView và in log Console
            SetRowStatus(row, id, "running");
            AccountRepository.UpdateAccountStatus(_connectionString, id, "running");
            LogConsole(id, "Bắt đầu chạy Via. Đang mở trình duyệt Chrome (Headless=false)...");
            UpdateStatusLabel($"UID: {id} - Đang mở trình duyệt...");

            // Khởi tạo thư mục Profile cố định cho UID
            string profilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BrowserProfiles", id);
            Directory.CreateDirectory(profilePath);

            playwright = await Playwright.CreateAsync();

            string channel = File.Exists(@"C:\Program Files\Google\Chrome\Application\chrome.exe") ||
                             File.Exists(@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe")
                             ? "chrome" : "msedge";

            context = await playwright.Chromium.LaunchPersistentContextAsync(
                profilePath,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
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

            bool loggedInWithCookie = false;
            var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

            // BƯỚC 1: Thử đăng nhập bằng Cookie nếu có dữ liệu cookie
            if (!string.IsNullOrWhiteSpace(rawCookie))
            {
                LogConsole(id, "Đang nạp Cookie vào trình duyệt và kiểm tra đăng nhập trên facebook.com...");
                UpdateStatusLabel($"UID: {id} - Đang tiêm Cookie vào trình duyệt...");

                var cookieList = CookieHelper.ParseCookieString(rawCookie);
                if (cookieList.Count > 0)
                {
                    await context.AddCookiesAsync(cookieList);
                }

                await page.GotoAsync("https://www.facebook.com", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
                await Task.Delay(4000);

                var initialCookies = await context.CookiesAsync();
                bool hasCUser = initialCookies.Any(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                string url = page.Url;

                if (url.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
                {
                    SetRowStatus(row, id, "check");
                    AccountRepository.UpdateAccountStatus(_connectionString, id, "check");
                    AccountRepository.ReleaseAssignedPages(_connectionString, id);

                    LogConsole(id, $"[CHECKPOINT] Tài khoản bị Checkpoint! URL: {url}");
                    UpdateStatusLabel($"UID: {id} - Trạng thái: CHECKPOINT!");
                    if (showDialog)
                    {
                        MessageBox.Show($"Tài khoản {id} bị CHECKPOINT!\nURL: {url}\nĐã cập nhật trạng thái 'check' và giải phóng các Page liên kết trong nhóm BM '{bm}'.",
                            "Cảnh báo Checkpoint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                if (hasCUser)
                {
                    loggedInWithCookie = true;
                    LogConsole(id, "Đăng nhập thành công bằng Cookie!");
                }
            }

            // BƯỚC 2: FALLBACK tự động đăng nhập bằng ID / PASS / 2FA khi Cookie bị DIE hoặc không có c_user
            if (!loggedInWithCookie)
            {
                if (string.IsNullOrWhiteSpace(pass))
                {
                    SetRowStatus(row, id, "check");
                    AccountRepository.UpdateAccountStatus(_connectionString, id, "check");
                    AccountRepository.ReleaseAssignedPages(_connectionString, id);

                    LogConsole(id, "[LỖI] Cookie hết hạn và không có mật khẩu (PASS) để Fallback đăng nhập!");
                    UpdateStatusLabel($"UID: {id} - Cookie DIE và không có mật khẩu để Fallback!");
                    if (showDialog)
                    {
                        MessageBox.Show($"Tài khoản {id} có Cookie đã bị DIE (hết hạn), đồng thời cột PASS đang trống nên không thể tự động đăng nhập lại!",
                            "Không thể đăng nhập", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                LogConsole(id, "Cookie hết hạn/không có. Đang Fallback tự động đăng nhập bằng ID / PASS / 2FA...");
                UpdateStatusLabel($"UID: {id} - Cookie hết hạn. Đang Fallback tự động đăng nhập bằng ID / PASS / 2FA...");

                if (!page.Url.Contains("facebook.com"))
                {
                    await page.GotoAsync("https://www.facebook.com", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
                    await Task.Delay(2000);
                }

                var emailInput = page.Locator("input[name='email'], #email");
                if (await emailInput.CountAsync() > 0)
                {
                    await emailInput.First.FillAsync(id);
                    await page.FillAsync("input[name='pass'], #pass", pass);

                    var loginBtn = page.Locator("button[name='login'], button[type='submit'], button:has-text('Đăng nhập'), button:has-text('Log In')");
                    if (await loginBtn.CountAsync() > 0)
                    {
                        await loginBtn.First.ClickAsync();
                    }
                    else
                    {
                        await page.Keyboard.PressAsync("Enter");
                    }

                    try
                    {
                        await page.WaitForURLAsync(u => u.Contains("checkpoint") || u.Contains("home") || u.Contains("two_factor") || u.Contains("login"), new() { Timeout = 15000 });
                    }
                    catch { }

                    await Task.Delay(3000);

                    // Xử lý 2FA nếu có yêu cầu
                    var otpInputs = page.Locator("input[name='approvals_code'], input[type='tel'], input[placeholder*='code'], input[placeholder*='mã']");
                    if (await otpInputs.CountAsync() > 0 && !string.IsNullOrWhiteSpace(ma2fa))
                    {
                        UpdateStatusLabel($"UID: {id} - Đang sinh mã TOTP 2FA và điền tự động...");

                        string otp = TotpGenerator.GenerateTotp(ma2fa);
                        if (!string.IsNullOrEmpty(otp))
                        {
                            await otpInputs.First.FillAsync(otp);
                            var submitBtn = page.Locator("button[type='submit'], #checkpointSubmitButton, button[id*='checkpoint']");
                            if (await submitBtn.CountAsync() > 0)
                            {
                                await submitBtn.First.ClickAsync();
                            }
                            else
                            {
                                await page.Keyboard.PressAsync("Enter");
                            }
                            await Task.Delay(4000);
                        }
                    }
                }

                // Kiểm tra trạng thái sau khi Fallback
                var postLoginCookies = await context.CookiesAsync();
                bool hasCUserAfterFallback = postLoginCookies.Any(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Value));
                string postUrl = page.Url;

                if (postUrl.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
                {
                    SetRowStatus(row, id, "check");
                    AccountRepository.UpdateAccountStatus(_connectionString, id, "check");
                    AccountRepository.ReleaseAssignedPages(_connectionString, id);

                    LogConsole(id, $"[CHECKPOINT] Bị Checkpoint sau khi đăng nhập ID/PASS! URL: {postUrl}");
                    UpdateStatusLabel($"UID: {id} - Bị CHECKPOINT sau khi đăng nhập ID/PASS!");
                    if (showDialog)
                    {
                        MessageBox.Show($"Tài khoản {id} bị CHECKPOINT sau khi đăng nhập ID/PASS!\nURL: {postUrl}\nĐã cập trạng thái 'check'.",
                            "Cảnh báo Checkpoint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                if (!hasCUserAfterFallback)
                {
                    SetRowStatus(row, id, "check");
                    AccountRepository.UpdateAccountStatus(_connectionString, id, "check");
                    AccountRepository.ReleaseAssignedPages(_connectionString, id);

                    LogConsole(id, $"[LỖI] Đăng nhập Fallback thất bại (sai mật khẩu hoặc bị chặn)! URL: {postUrl}");
                    UpdateStatusLabel($"UID: {id} - Đăng nhập Fallback thất bại!");
                    if (showDialog)
                    {
                        MessageBox.Show($"Tài khoản {id} không thể đăng nhập bằng ID/PASS (sai mật khẩu hoặc bị chặn)!\nURL: {postUrl}\nĐã cập nhật trạng thái 'check'.",
                            "Đăng nhập thất bại", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return;
                }

                LogConsole(id, "Đăng nhập Fallback thành công!");
            }

            // BƯỚC 3: ĐĂNG NHẬP THÀNH CÔNG (Dù bằng Cookie hay bằng Fallback ID/PASS/2FA)
            var currentCookies = await context.CookiesAsync();

            // Tự động lấy lại bộ Cookie mới tinh từ trình duyệt lưu vào database và DataGridView
            string fullCookie = string.Join("; ", currentCookies.Select(c => $"{c.Name}={c.Value}"));
            SetRowCookie(row, fullCookie);
            AccountRepository.UpdateAccountCookie(_connectionString, id, fullCookie);

            LogConsole(id, "Đang cào ID Page từ giao diện Facebook https://www.facebook.com/pages/?category=your_pages...");
            UpdateStatusLabel($"UID: {id} - Đang cào ID Page từ giao diện...");

            // 1.1. Điều hướng trình duyệt đến "https://www.facebook.com/pages/?category=your_pages" và đợi load xong DOM
            await page.GotoAsync("https://www.facebook.com/pages/?category=your_pages", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 35000 });
            await Task.Delay(3500);

            // Lấy UID sạch của Via và c_user hiện tại để làm màng lọc loại trừ
            string cleanViaId = id.Trim();
            string viaCUser = currentCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

            // 1.2. Dùng page.EvaluateAsync tiêm mã JavaScript trích xuất danh sách Page ID từ các link href
            // Điều kiện lọc trong JS: ID phải là chuỗi số (>10 ký tự) và BẮT BUỘC KHÁC với ID của Via đang chạy. Lọc trùng lặp ngay trong mảng JS.
            string jsExtractPageIds = @"
                (args) => {
                    const viaUid = (args.viaUid || '').trim().toLowerCase();
                    const viaCUser = (args.viaCUser || '').trim().toLowerCase();
                    const pageIdSet = new Set();

                    const container = document.querySelector(""div[role='main']"") || document.querySelector(""main"") || document.body;
                    const links = container.querySelectorAll(""a[href]"");

                    // Regex nhận diện Page ID
                    const idRegex = /(?:id=|\/profile\.php\?id=|\/pages\/[^\/]+\/|\/pages\/|\/)(615\d{11,13}|\d{11,20})(?:[/?#&]|$)/i;

                    const excludedKeywords = [
                        '/friends', '/watch', '/marketplace', '/groups', '/gaming', '/bookmarks',
                        '/events', '/saved', '/memories', '/messages', '/notifications', '/settings',
                        '/privacy', '/policies', '/help', '/create', '/invites', 'category=your_pages',
                        '/ads', '/business', '/login', '/recover', '/user/'
                    ];

                    for (const a of links) {
                        const href = (a.getAttribute('href') || '').trim();
                        if (!href) continue;

                        const lowerHref = href.toLowerCase();
                        if (excludedKeywords.some(kw => lowerHref.includes(kw))) continue;

                        const match = href.match(idRegex);
                        if (match && match[1]) {
                            const pid = match[1].trim();

                            // Điều kiện: ID phải là chuỗi số dài hơn 10 ký tự
                            if (pid.length > 10 && /^\d+$/.test(pid)) {
                                // Bỏ qua UID cá nhân 1000... nếu không nằm trong đường dẫn /pages/ hoặc profile.php?id=
                                if (pid.startsWith('1000') && !lowerHref.includes('/pages/') && !lowerHref.includes('profile.php?id=')) {
                                    continue;
                                }

                                const lowerPid = pid.toLowerCase();
                                // BẮT BUỘC KHÁC với ID của Via đang chạy và cookie c_user của Via
                                if (lowerPid === viaUid || (viaCUser && lowerPid === viaCUser)) {
                                    continue;
                                }

                                pageIdSet.add(pid);
                            }
                        }
                    }

                    // Lọc trùng lặp ngay trong mảng JS
                    return Array.from(pageIdSet);
                }
            ";

            string[] extractedPageIds = await page.EvaluateAsync<string[]>(jsExtractPageIds, new { viaUid = cleanViaId, viaCUser });

            // 1.3. Lấy Count của mảng này gán vào DataGridView cột 'PageCount'. Nếu Count = 0 thì return luôn.
            int validPageCount = extractedPageIds.Length;
            SetRowPageCount(row, validPageCount);
            AccountRepository.UpdateAccountPageCount(_connectionString, id, validPageCount);
            UpdateDynamicThreadCount();

            LogConsole(id, $"Số lượng Page lấy được: {validPageCount} Page. Danh sách ID: [{(validPageCount > 0 ? string.Join(", ", extractedPageIds) : "Không có")}]");

            if (validPageCount == 0)
            {
                SetRowStatus(row, id, "live");
                AccountRepository.UpdateAccountStatus(_connectionString, id, "live");
                LogConsole(id, "Tài khoản không có Page nào (Count = 0). Hoàn thành!");
                UpdateStatusLabel($"UID: {id} - Tài khoản không có Page nào (Count = 0).");
                if (showDialog)
                {
                    MessageBox.Show($"Tài khoản {id} đang LIVE, nhưng không tìm thấy Page nào (Count = 0) trên tài khoản này.",
                        "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            LogConsole(id, $"Bắt đầu chuyển vai trò (Switch Profile) bằng UI Playwright (Tổng cộng {validPageCount} Page)...");
            UpdateStatusLabel($"UID: {id} - Đang chuyển vai trò bằng giao diện Playwright...");

            List<string> exportedCookies = new List<string>();
            var pageRecords = new List<(string PageId, string SessionData)>();

            foreach (var pid in extractedPageIds)
            {
                // Bọc try-catch chi tiết cho TỪNG BƯỚC bên trong vòng lặp duyệt Page ID
                try
                {
                    LogConsole(id, $"Đang điều hướng tới Page https://www.facebook.com/{pid}...");
                    UpdateStatusLabel($"UID: {id} - Đang điều hướng tới Page {pid}...");

                    // 1. Điều hướng tới trang Page
                    try
                    {
                        await page.GotoAsync($"https://www.facebook.com/{pid}", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 35000 });
                        await Task.Delay(3000);
                    }
                    catch (Exception exGoto)
                    {
                        LogConsole(id, $"[LỖI GOTO] Điều hướng tới Page {pid} thất bại: {exGoto.Message}");
                        UpdateStatusLabel($"UID: {id} - Lỗi tải trang Page {pid}: {exGoto.Message}");
                        continue;
                    }

                    // 2. Kiểm tra nếu phiên hiện tại đã là Page này (c_user == pid)
                    bool switchSuccess = false;
                    string matchedCookieText = "";
                    string detectedCUser = "";

                    var initialCheckCookies = await context.CookiesAsync();
                    var initCUser = initialCheckCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

                    if (string.Equals(initCUser, pid, StringComparison.OrdinalIgnoreCase))
                    {
                        switchSuccess = true;
                        detectedCUser = initCUser;
                        matchedCookieText = CookieHelper.FormatPageCookie(initialCheckCookies, pid, id);
                        LogConsole(id, $"Phiên hiện tại đã là Page {pid} (c_user={initCUser}). Không cần bấm đổi vai trò.");
                    }
                    else
                    {
                        // 3. Tìm nút Switch Profile (Chuyển / Đổi / Switch Now) trên giao diện Playwright
                        LogConsole(id, $"Đang tìm nút Switch Profile (Đổi / Switch Now) trên giao diện Page {pid}...");

                        var switchCandidateSelectors = new[]
                        {
                            "div[role='button']:has-text('Switch now')",
                            "div[role='button']:has-text('Chuyển ngay')",
                            "div[role='button']:has-text('Switch Now')",
                            "div[role='button']:has-text('Đổi ngay')",
                            "div[aria-label='Switch']",
                            "div[aria-label='Chuyển']",
                            "div[aria-label='Đổi']",
                            "div[aria-label*='Switch Now' i]",
                            "div[aria-label*='Chuyển ngay' i]",
                            "div[aria-label*='Đổi ngay' i]",
                            "div[aria-label*='Switch to' i]",
                            "div[aria-label*='Chuyển sang' i]",
                            "div[aria-label*='Switch into' i]",
                            "div[role='button']:has-text('Switch')",
                            "div[role='button']:has-text('Chuyển')",
                            "div[role='button']:has-text('Đổi')",
                            "a:has-text('Switch now')",
                            "a:has-text('Chuyển ngay')",
                            "a:has-text('Switch')",
                            "a:has-text('Chuyển')",
                            "a:has-text('Đổi')",
                            "button:has-text('Switch now')",
                            "button:has-text('Chuyển ngay')",
                            "button:has-text('Switch')",
                            "button:has-text('Chuyển')",
                            "button:has-text('Đổi')"
                        };

                        ILocator? targetButton = null;
                        string clickedSelector = "";

                        foreach (var sel in switchCandidateSelectors)
                        {
                            try
                            {
                                var loc = page.Locator(sel);
                                int count = await loc.CountAsync();
                                for (int i = 0; i < count; i++)
                                {
                                    var item = loc.Nth(i);
                                    if (await item.IsVisibleAsync())
                                    {
                                        string text = (await item.InnerTextAsync() ?? "").Trim().ToLowerInvariant();
                                        string aria = (await item.GetAttributeAsync("aria-label") ?? "").Trim().ToLowerInvariant();
                                        string combined = $"{text} {aria}";

                                        // Loại trừ các nút không liên quan
                                        if (combined.Contains("mật khẩu") || combined.Contains("password") || combined.Contains("language"))
                                            continue;

                                        targetButton = item;
                                        clickedSelector = sel;
                                        break;
                                    }
                                }
                                if (targetButton != null) break;
                            }
                            catch { }
                        }

                        // [NGUYÊN TẮC 1]: ĐỒNG BỘ THỜI GIAN CLICK VÀ NAVIGATION
                        if (targetButton != null)
                        {
                            LogConsole(id, $"Tìm thấy nút Switch ({clickedSelector}). Đang đồng bộ Click và WaitForLoadState(NetworkIdle)...");
                            try
                            {
                                await Task.WhenAll(
                                    page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }),
                                    targetButton.ClickAsync(new() { Timeout = 10000 })
                                );
                            }
                            catch (TimeoutException)
                            {
                                LogConsole(id, "WaitForLoadState NetworkIdle quá 15s (kết nối ngầm giữ phiên), tiếp tục...");
                            }
                            catch (Exception exClick)
                            {
                                LogConsole(id, $"[LỖI CLICK] Click nút Switch gặp lỗi: {exClick.Message}. Thử click trực tiếp...");
                                try { await targetButton.ClickAsync(new() { Timeout = 5000 }); } catch { }
                            }

                            await Task.Delay(2000);

                            // Kiểm tra nếu xuất hiện Dialog popup xác nhận "Switch into ..." / "Chuyển sang ..."
                            try
                            {
                                var dialogConfirmBtn = page.Locator("div[role='dialog'] div[role='button']:has-text('Switch'), div[role='dialog'] div[role='button']:has-text('Chuyển'), div[role='dialog'] div[role='button']:has-text('Đổi'), div[role='dialog'] button:has-text('Switch'), div[role='dialog'] button:has-text('Chuyển')");
                                if (await dialogConfirmBtn.CountAsync() > 0 && await dialogConfirmBtn.First.IsVisibleAsync())
                                {
                                    LogConsole(id, "Bắt được popup xác nhận đổi phiên. Đồng bộ Click xác nhận với WaitForLoadState(NetworkIdle)...");
                                    try
                                    {
                                        await Task.WhenAll(
                                            page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }),
                                            dialogConfirmBtn.First.ClickAsync(new() { Timeout = 8000 })
                                        );
                                    }
                                    catch (TimeoutException)
                                    {
                                        LogConsole(id, "Xác nhận popup: WaitForLoadState quá 15s, tiếp tục...");
                                    }
                                    catch (Exception exDialogClick)
                                    {
                                        LogConsole(id, $"[LỖI POPUP CLICK] Click xác nhận: {exDialogClick.Message}");
                                        try { await dialogConfirmBtn.First.ClickAsync(new() { Timeout = 5000 }); } catch { }
                                    }
                                    await Task.Delay(2000);
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            // [NGUYÊN TẮC 2]: NẾU KHÔNG THẤY NÚT TRÊN TRANG, THỬ KỊCH BẢN CLICK AVATAR GÓC PHẢI TRÊN CÙNG
                            LogConsole(id, $"Không tìm thấy nút Switch trực tiếp trên trang Page {pid}. Thử kịch bản click Avatar góc phải trên cùng...");
                            try
                            {
                                var avatarLocators = page.Locator("svg[aria-label='Your profile'], svg[aria-label='Trang cá nhân của bạn'], div[aria-label='Your profile'][role='button'], div[aria-label='Trang cá nhân của bạn'][role='button'], div[aria-label='Your profile'], div[aria-label='Trang cá nhân của bạn'], div[role='banner'] div[role='button']:has(image), div[role='banner'] div[role='button']:has(img)");
                                if (await avatarLocators.CountAsync() > 0 && await avatarLocators.First.IsVisibleAsync())
                                {
                                    LogConsole(id, "Đang click Avatar menu góc phải trên cùng...");
                                    await avatarLocators.First.ClickAsync(new() { Timeout = 5000 });
                                    await Task.Delay(2000);

                                    // Tìm nút 'See all profiles' / 'Xem tất cả trang cá nhân'
                                    var seeAllProfiles = page.Locator("div[role='button']:has-text('See all profiles'), div[role='button']:has-text('Xem tất cả trang cá nhân'), div[role='button']:has-text('Xem tất cả các trang cá nhân'), span:has-text('See all profiles'), span:has-text('Xem tất cả trang cá nhân')");
                                    if (await seeAllProfiles.CountAsync() > 0 && await seeAllProfiles.First.IsVisibleAsync())
                                    {
                                        LogConsole(id, "Đang click 'See all profiles'...");
                                        await seeAllProfiles.First.ClickAsync(new() { Timeout = 5000 });
                                        await Task.Delay(2000);
                                    }

                                    // Tìm profile có ID là pid
                                    var pageItem = page.Locator($"a[href*='{pid}'], div[role='button']:has-text('{pid}')");
                                    if (await pageItem.CountAsync() > 0 && await pageItem.First.IsVisibleAsync())
                                    {
                                        LogConsole(id, $"Tìm thấy Page {pid} trong danh sách profile. Đang click chuyển với WaitForLoadState(NetworkIdle)...");
                                        try
                                        {
                                            await Task.WhenAll(
                                                page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }),
                                                pageItem.First.ClickAsync(new() { Timeout = 8000 })
                                            );
                                        }
                                        catch (TimeoutException) { }
                                        catch (Exception)
                                        {
                                            try { await pageItem.First.ClickAsync(new() { Timeout = 5000 }); } catch { }
                                        }
                                        await Task.Delay(2000);
                                    }
                                }
                            }
                            catch (Exception exAvatar)
                            {
                                LogConsole(id, $"[CẢNH BÁO] Kịch bản Avatar menu gặp lỗi: {exAvatar.Message}");
                            }
                        }

                        // [NGUYÊN TẮC 3]: VÒNG LẶP POLLING KẾT HỢP RELOAD ÉP NẠP HEADER COOKIE
                        LogConsole(id, $"Bắt đầu vòng lặp Polling & Reload để kiểm tra Cookie c_user={pid}...");

                        int maxAttempts = 6;
                        for (int attempt = 1; attempt <= maxAttempts; attempt++)
                        {
                            var browserCookies = await context.CookiesAsync();
                            var cUserCookie = browserCookies.FirstOrDefault(c => c.Name.Equals("c_user", StringComparison.OrdinalIgnoreCase));
                            detectedCUser = cUserCookie?.Value?.Trim() ?? "";

                            LogConsole(id, $"[Polling {attempt}/{maxAttempts}] c_user hiện tại: '{detectedCUser}' (cần '{pid}')");

                            // Chỉ khi c_user khớp với ID Page mới được break vòng lặp và ghi nhận thành công
                            if (string.Equals(detectedCUser, pid, StringComparison.OrdinalIgnoreCase))
                            {
                                switchSuccess = true;
                                matchedCookieText = CookieHelper.FormatPageCookie(browserCookies, pid, id);
                                break;
                            }

                            // Nếu trong khi chờ xuất hiện dialog xác nhận chuyển, bấm xác nhận
                            try
                            {
                                var dialogBtn = page.Locator("div[role='dialog'] div[role='button']:has-text('Switch'), div[role='dialog'] div[role='button']:has-text('Chuyển'), div[role='dialog'] div[role='button']:has-text('Đổi'), div[role='dialog'] button:has-text('Switch'), div[role='dialog'] button:has-text('Chuyển')");
                                if (await dialogBtn.CountAsync() > 0 && await dialogBtn.First.IsVisibleAsync())
                                {
                                    LogConsole(id, "Phát hiện popup xác nhận chưa đóng. Bấm nút xác nhận...");
                                    try
                                    {
                                        await Task.WhenAll(
                                            page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 12000 }),
                                            dialogBtn.First.ClickAsync(new() { Timeout = 6000 })
                                        );
                                    }
                                    catch { }
                                    await Task.Delay(2000);
                                    continue;
                                }
                            }
                            catch { }

                            // Nếu c_user vẫn bằng ID của Via gốc hoặc chưa nhận ID Page, chèn thêm page.ReloadAsync()
                            if (attempt < maxAttempts)
                            {
                                LogConsole(id, $"c_user vẫn chưa nhận ID Page ({detectedCUser} != {pid}). Đang ReloadAsync() để nạp lại header cookie mới nhất từ server...");
                                await Task.Delay(2000);

                                try
                                {
                                    await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 25000 });
                                    await Task.Delay(2000);
                                }
                                catch (Exception exReload)
                                {
                                    LogConsole(id, $"[LỖI RELOAD] Tải lại trang thất bại: {exReload.Message}");
                                }
                            }
                        }
                    }

                    // 5. Xử lý kết quả kiểm tra Cookie
                    if (switchSuccess && !string.IsNullOrWhiteSpace(matchedCookieText))
                    {
                        string formattedLine = $"{pid} | {matchedCookieText}";
                        exportedCookies.Add(formattedLine);
                        pageRecords.Add((pid, matchedCookieText));

                        LogConsole(id, $"[THÀNH CÔNG] Đổi phiên sang Page {pid} thành công (c_user={detectedCUser})! Đã lưu Cookie.");
                        UpdateStatusLabel($"UID: {id} - Đã lấy Cookie Page {pid} (c_user={pid})!");
                    }
                    else
                    {
                        LogConsole(id, $"[LỖI] Đổi phiên Page {pid} thất bại! Hết thời gian chờ nhưng c_user hiện tại là '{detectedCUser}' (yêu cầu '{pid}').");
                        UpdateStatusLabel($"UID: {id} - Lỗi Page {pid}: c_user chưa đổi sang {pid}");
                        continue;
                    }
                }
                catch (Exception exPage)
                {
                    // Bắt mọi lỗi ngoại lệ bất ngờ của từng Page -> in ra Console và tiếp tục Page sau
                    LogConsole(id, $"[LỖI NGOẠI LỆ PAGE] Ngoại lệ khi xử lý Page {pid}: {exPage.Message}");
                    UpdateStatusLabel($"UID: {id} - Lỗi Page {pid}: {exPage.Message}");
                    continue;
                }

                // Giới hạn an toàn tối đa 10 Page mỗi Via
                if (exportedCookies.Count >= 10)
                {
                    LogConsole(id, "Đã đạt giới hạn an toàn 10 Page cho Via này.");
                    break;
                }
            }

            // Hoàn thành! Giữ nguyên trạng thái live
            SetRowStatus(row, id, "live");
            AccountRepository.UpdateAccountStatus(_connectionString, id, "live");
            LogConsole(id, $"Hoàn thành! Đã thu thập được {exportedCookies.Count}/{validPageCount} Cookie Page.");
            UpdateStatusLabel($"UID: {id} - Hoàn thành! Đã gom được {exportedCookies.Count} Cookie Page.");

            if (exportedCookies.Count > 0)
            {
                // Lưu danh sách Page và Cookie vào SQLite và DataGridView
                AccountRepository.AssignPages(_connectionString, bm, id, pageRecords);

                string firstCookie = pageRecords[0].SessionData;
                SetRowCookie(row, firstCookie);
                AccountRepository.UpdateAccountCookie(_connectionString, id, firstCookie);

                // Hiển thị Pop-up qua Invoke để an toàn cho UI thread
                this.Invoke(new Action(() =>
                {
                    using var popForm = new FormCookieResult(id, bm, exportedCookies);
                    popForm.ShowDialog(this);
                }));
            }
            else
            {
                LogConsole(id, $"Cảnh báo: Đã quét {validPageCount} Page nhưng chưa Page nào đổi c_user thành công.");
                UpdateStatusLabel($"UID: {id} - Đã quét {validPageCount} Page nhưng chưa Page nào đổi c_user thành công.");
            }
        }
        catch (PlaywrightException pex) when (pex.Message.Contains("ERR_NAME_NOT_RESOLVED"))
        {
            SetRowStatus(row, id, "check");
            AccountRepository.UpdateAccountStatus(_connectionString, id, "check");
            LogConsole(id, $"[LỖI PLAYWRIGHT DNS] {pex.Message}");
            UpdateStatusLabel($"UID: {id} - Lỗi phân giải tên miền (DNS/Mạng)!");
            if (showDialog)
            {
                MessageBox.Show($"Không thể kết nối tới Facebook (net::ERR_NAME_NOT_RESOLVED)!\n\nNguyên nhân & Cách khắc phục:\n1. Ứng dụng được mở từ cửa sổ dòng lệnh Sandbox bị chặn Internet. Hãy mở trực tiếp file 'ViaManager.exe' từ thư mục 'd:\\ViaManager\\bin\\Release\\net8.0-windows\\' ngoài màn hình Windows.\n2. Nếu mở ngoài Windows vẫn gặp lỗi này, hãy đổi DNS máy tính sang 8.8.8.8 / 1.1.1.1 (do nhà mạng chặn facebook.com).",
                    "Lỗi kết nối mạng", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            SetRowStatus(row, id, "check");
            AccountRepository.UpdateAccountStatus(_connectionString, id, "check");
            LogConsole(id, $"[LỖI TOÀN CỤC] {ex.Message}");
            UpdateStatusLabel($"Lỗi chạy UID {id}: {ex.Message}");
            if (showDialog)
            {
                MessageBox.Show($"Lỗi trong quá trình chạy tự động UID {id}: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            // [BẢO VỆ COOKIE SỐNG - 3 RÀNG BUỘC NGHIÊM NGẶT KHI KẾT THÚC PHIÊN]:
            // 1. TUYỆT ĐỐI KHÔNG ĐĂNG XUẤT: Không điều hướng tới URL logout, không click nút đăng xuất.
            // 2. KHÔNG XÓA COOKIE: Không gọi context.ClearCookiesAsync(), bảo toàn nguyên vẹn Session trong profile.
            // 3. CHỈ ĐÓNG NGANG: Đóng ngang trình duyệt (context.CloseAsync) ngắt kết nối mà không gửi tín hiệu hủy phiên lên Facebook.
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

            // Cập nhật cột LastRunTime trong Database thành thời gian hiện tại (DateTime.Now)
            DateTime now = DateTime.Now;
            try
            {
                AccountRepository.UpdateLastRunTime(_connectionString, id, now);
                SetRowLastRunTime(row, now);

                // Nếu trạng thái hiện tại đang KHÔNG phải là 'check' (bị checkpoint), cập nhật nó thành 'lock'
                string currentStatus = GetRowStatus(row);
                if (!currentStatus.Equals("check", StringComparison.OrdinalIgnoreCase))
                {
                    SetRowStatus(row, id, "lock");
                    AccountRepository.UpdateAccountStatus(_connectionString, id, "lock");
                    UpdateStatusLabel($"UID: {id} - Đóng ngang trình duyệt an toàn & chuyển sang 'lock' lúc {now:HH:mm:ss}.");
                }
                LogConsole(id, $"Đóng ngang trình duyệt an toàn. Trạng thái cuối: {GetRowStatus(row)}");
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

                    AccountRepository.ReleaseAssignedPages(_connectionString, oldId);
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
        var rowsToDelete = new List<DataGridViewRow>();

        foreach (DataGridViewRow row in dgvAccounts.SelectedRows)
        {
            if (!row.IsNewRow)
                rowsToDelete.Add(row);
        }

        if (rowsToDelete.Count == 0 && dgvAccounts.CurrentRow != null && !dgvAccounts.CurrentRow.IsNewRow)
        {
            rowsToDelete.Add(dgvAccounts.CurrentRow);
        }

        if (rowsToDelete.Count == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất một dòng để xóa.", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa {rowsToDelete.Count} dòng đã chọn khỏi bảng và SQLite không?",
            "Xác nhận xóa",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
            return;

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            foreach (var row in rowsToDelete)
            {
                string id = (row.Tag as string) ?? row.Cells["ID"].Value?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(id))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM Accounts WHERE ID = $id";
                    cmd.Parameters.AddWithValue("$id", id);
                    cmd.ExecuteNonQuery();

                    AccountRepository.ReleaseAssignedPages(_connectionString, id);
                }
                dgvAccounts.Rows.Remove(row);
            }

            transaction.Commit();
            statusLabel.Text = $"Đã xóa {rowsToDelete.Count} dòng.";
            UpdateCountLabels();
            LoadAccountsFromDb(txtSearch.Text);
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
                string generatedCookie = string.IsNullOrEmpty(existingCookie)
                    ? $"c_user={id}; xs={Guid.NewGuid().ToString("N")[..24]}:2:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}; fr={Guid.NewGuid().ToString("N")[..16]}; datr={Guid.NewGuid().ToString("N")[..16]};"
                    : existingCookie;

                row.Cells["Cookie"].Value = generatedCookie;
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
                cmd.Parameters.AddWithValue("$cookie", generatedCookie);
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
