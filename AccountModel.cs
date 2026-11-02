using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Playwright;

namespace ViaManager;

public static class GlobalState
{
    public static readonly ConcurrentDictionary<string, byte> ProcessedPageIds = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryAddPageId(string pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
            return false;

        return ProcessedPageIds.TryAdd(pageId.Trim(), 0);
    }

    public static bool TryAddPage(string pageId) => TryAddPageId(pageId);

    public static void InitializeFromDatabase(string connectionString)
    {
        try
        {
            var existing = AccountRepository.GetAllAssignedPageIds(connectionString);
            foreach (var id in existing)
            {
                ProcessedPageIds.TryAdd(id, 0);
            }
        }
        catch { }
    }
}

public static class TotpGenerator
{
    public static string GenerateTotp(string base32Secret)
    {
        if (string.IsNullOrWhiteSpace(base32Secret))
            return "";

        try
        {
            string cleanSecret = base32Secret.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
            byte[] key = Base32Decode(cleanSecret);
            if (key.Length == 0)
                return "";

            long timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
            byte[] timeBytes = BitConverter.GetBytes(timeStep);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timeBytes);

            using var hmac = new HMACSHA1(key);
            byte[] hash = hmac.ComputeHash(timeBytes);

            int offset = hash[^1] & 0x0F;
            int binaryCode = ((hash[offset] & 0x7F) << 24)
                           | ((hash[offset + 1] & 0xFF) << 16)
                           | ((hash[offset + 2] & 0xFF) << 8)
                           | (hash[offset + 3] & 0xFF);

            int otp = binaryCode % 1000000;
            return otp.ToString("D6");
        }
        catch
        {
            return "";
        }
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0;
        int bitsLeft = 0;

        foreach (char c in input)
        {
            if (c == '=') break;
            int val = alphabet.IndexOf(c);
            if (val < 0) continue;

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }
}

public static class CookieHelper
{
    /// <summary>
    /// Formats page cookie using verbatim serialization directly from the browser context.
    /// Absolute Invariant: Zero Cookie Mutation. Never inject synthetic i_user or override c_user.
    /// </summary>
    public static string FormatPageCookie(IReadOnlyList<BrowserContextCookiesResult> cookies, string pageId, string fallbackUid)
    {
        return ViaManager.Utilities.CookieSerializer.SerializeVerbatim(cookies);
    }

    public static string ExtractCookieValue(string cookieString, string key)
    {
        if (string.IsNullOrWhiteSpace(cookieString) || string.IsNullOrWhiteSpace(key))
            return "";

        var match = System.Text.RegularExpressions.Regex.Match(
            cookieString,
            $@"(?:^|;\s*){System.Text.RegularExpressions.Regex.Escape(key)}=([^;]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return "";
    }

    public static List<Microsoft.Playwright.Cookie> ParseCookieString(string cookieString)
    {
        var list = new List<Microsoft.Playwright.Cookie>();
        if (string.IsNullOrWhiteSpace(cookieString))
            return list;

        var pairs = cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawPair in pairs)
        {
            string pair = rawPair.Trim();
            if (string.IsNullOrEmpty(pair))
                continue;

            int eqIdx = pair.IndexOf('=');
            if (eqIdx <= 0)
                continue;

            string name = pair.Substring(0, eqIdx).Trim();
            string val = pair.Substring(eqIdx + 1).Trim();

            if (string.IsNullOrEmpty(name))
                continue;

            list.Add(new Microsoft.Playwright.Cookie
            {
                Name = name,
                Value = val,
                Domain = ".facebook.com",
                Path = "/",
                Secure = true,
                SameSite = Microsoft.Playwright.SameSiteAttribute.None
            });
        }

        return list;
    }
}

public class AccountItem
{
    public string ID { get; set; } = "";
    public string PASS { get; set; } = "";
    public string Ma2FA { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string Token { get; set; } = "";
    public string MAIL { get; set; } = "";
    public string PASS_MAIL { get; set; } = "";
    public string TrangThai { get; set; } = "live";
    public string VaiTro { get; set; } = "Run";
    public string TenTaiKhoan { get; set; } = "";
    public string BM { get; set; } = "Bmv1";
    public string BM_Mail { get; set; } = "";
    public string? LastRunTime { get; set; }
    public int PageCount { get; set; } = 0;
}

public static class AccountParser
{
    public static AccountItem? ParseLine(string line, string defaultTrangThai = "live", string defaultVaiTro = "Run", string defaultBM = "Bmv1")
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        string trimmed = line.Trim();
        char separator = trimmed.Contains('|') ? '|' : (trimmed.Contains('\t') ? '\t' : '|');
        string[] rawParts = trimmed.Split(separator);

        if (rawParts.Length == 0)
            return null;

        string first = rawParts[0].Trim().ToUpperInvariant();
        if (first is "ID" or "UID" or "STT" or "TAIKHOAN")
        {
            if (rawParts.Length > 1 && rawParts[1].Trim().ToUpperInvariant() is "PASS" or "PASSWORD")
                return null;
        }

        string id = "";
        string pass = "";
        string ma2fa = "";
        string cookie = "";
        string token = "";
        string mail = "";
        string passMail = "";
        string trangThai = defaultTrangThai;
        string vaiTro = defaultVaiTro;
        string bm = defaultBM;
        string tenTaiKhoan = "";
        string bmMail = "";

        if (separator == '|')
        {
            if (rawParts.Length >= 7)
            {
                id = rawParts[0].Trim();
                pass = rawParts[1].Trim();
                ma2fa = rawParts[2].Trim();
                cookie = rawParts[3].Trim();
                token = rawParts[4].Trim();
                mail = rawParts[5].Trim();
                passMail = rawParts[6].Trim();

                if (rawParts.Length > 7) trangThai = string.IsNullOrWhiteSpace(rawParts[7]) ? defaultTrangThai : rawParts[7].Trim();
                if (rawParts.Length > 8) vaiTro = string.IsNullOrWhiteSpace(rawParts[8]) ? defaultVaiTro : rawParts[8].Trim();
                if (rawParts.Length > 9) tenTaiKhoan = rawParts[9].Trim();
                if (rawParts.Length > 10) bm = string.IsNullOrWhiteSpace(rawParts[10]) ? defaultBM : rawParts[10].Trim();
                if (rawParts.Length > 11) bmMail = rawParts[11].Trim();
            }
            else
            {
                var parts = rawParts.Select(p => p.Trim()).ToList();

                int cookieIdx = parts.FindIndex(p => p.Contains("c_user=", StringComparison.OrdinalIgnoreCase) || p.Contains("datr=", StringComparison.OrdinalIgnoreCase));
                if (cookieIdx >= 0)
                {
                    cookie = parts[cookieIdx];
                    parts.RemoveAt(cookieIdx);
                }

                int tokenIdx = parts.FindIndex(p => p.StartsWith("EAAA", StringComparison.OrdinalIgnoreCase) || p.StartsWith("EAAB", StringComparison.OrdinalIgnoreCase) || p.StartsWith("EAAG", StringComparison.OrdinalIgnoreCase));
                if (tokenIdx >= 0)
                {
                    token = parts[tokenIdx];
                    parts.RemoveAt(tokenIdx);
                }

                int mailIdx = parts.FindIndex(p => p.Contains('@') && !p.Contains('='));
                if (mailIdx >= 0)
                {
                    mail = parts[mailIdx];
                    parts.RemoveAt(mailIdx);
                }

                if (parts.Count > 0)
                {
                    id = parts[0];
                    parts.RemoveAt(0);
                }

                if (parts.Count > 0)
                {
                    pass = parts[0];
                    parts.RemoveAt(0);
                }

                if (parts.Count >= 2)
                {
                    ma2fa = parts[0];
                    passMail = parts[1];
                }
                else if (parts.Count == 1)
                {
                    if (!string.IsNullOrEmpty(mail) && string.IsNullOrEmpty(passMail) && (parts[0].Length != 16 && parts[0].Length != 32))
                    {
                        passMail = parts[0];
                    }
                    else
                    {
                        ma2fa = parts[0];
                    }
                }
            }
        }
        else
        {
            if (rawParts.Length > 0) id = rawParts[0].Trim();
            if (rawParts.Length > 1) pass = rawParts[1].Trim();
            if (rawParts.Length > 2) ma2fa = rawParts[2].Trim();
            if (rawParts.Length > 3) mail = rawParts[3].Trim();
            if (rawParts.Length > 4) passMail = rawParts[4].Trim();
            if (rawParts.Length > 5 && !string.IsNullOrWhiteSpace(rawParts[5])) trangThai = rawParts[5].Trim();
            if (rawParts.Length > 6 && !string.IsNullOrWhiteSpace(rawParts[6])) vaiTro = rawParts[6].Trim();
            if (rawParts.Length > 7) tenTaiKhoan = rawParts[7].Trim();
            if (rawParts.Length > 8 && !string.IsNullOrWhiteSpace(rawParts[8])) bm = rawParts[8].Trim();
            if (rawParts.Length > 9) bmMail = rawParts[9].Trim();
            if (rawParts.Length > 10) cookie = rawParts[10].Trim();
            if (rawParts.Length > 11) token = rawParts[11].Trim();
        }

        if (string.IsNullOrWhiteSpace(id))
            return null;

        return new AccountItem
        {
            ID = id,
            PASS = pass,
            Ma2FA = ma2fa,
            Cookie = cookie,
            Token = token,
            MAIL = mail,
            PASS_MAIL = passMail,
            TrangThai = string.IsNullOrWhiteSpace(trangThai) ? defaultTrangThai : trangThai,
            VaiTro = string.IsNullOrWhiteSpace(vaiTro) ? defaultVaiTro : vaiTro,
            BM = string.IsNullOrWhiteSpace(bm) ? defaultBM : bm,
            TenTaiKhoan = tenTaiKhoan,
            BM_Mail = bmMail
        };
    }
}

public static class AccountRepository
{
    public static void InitializeDatabase(string connectionString)
    {
        ViaManager.Data.DatabaseMigrationService.Migrate(connectionString);
    }

    public static int BatchUpsert(string connectionString, IEnumerable<AccountItem> accounts)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO Accounts (ID, PASS, Ma2FA, Cookie, Token, MAIL, PASS_MAIL, TrangThai, VaiTro, TenTaiKhoan, BM, BM_Mail)
                VALUES ($id, $pass, $ma2fa, $cookie, $token, $mail, $passMail, $trangThai, $vaiTro, $tenTaiKhoan, $bm, $bmMail)
                ON CONFLICT(ID) DO UPDATE SET
                    PASS = CASE WHEN excluded.PASS <> '' THEN excluded.PASS ELSE Accounts.PASS END,
                    Ma2FA = CASE WHEN excluded.Ma2FA <> '' THEN excluded.Ma2FA ELSE Accounts.Ma2FA END,
                    Cookie = CASE WHEN excluded.Cookie <> '' THEN excluded.Cookie ELSE Accounts.Cookie END,
                    Token = CASE WHEN excluded.Token <> '' THEN excluded.Token ELSE Accounts.Token END,
                    MAIL = CASE WHEN excluded.MAIL <> '' THEN excluded.MAIL ELSE Accounts.MAIL END,
                    PASS_MAIL = CASE WHEN excluded.PASS_MAIL <> '' THEN excluded.PASS_MAIL ELSE Accounts.PASS_MAIL END,
                    TrangThai = CASE WHEN excluded.TrangThai <> '' THEN excluded.TrangThai ELSE Accounts.TrangThai END,
                    VaiTro = CASE WHEN excluded.VaiTro <> '' THEN excluded.VaiTro ELSE Accounts.VaiTro END,
                    TenTaiKhoan = CASE WHEN excluded.TenTaiKhoan <> '' THEN excluded.TenTaiKhoan ELSE Accounts.TenTaiKhoan END,
                    BM = CASE WHEN excluded.BM <> '' THEN excluded.BM ELSE Accounts.BM END,
                    BM_Mail = CASE WHEN excluded.BM_Mail <> '' THEN excluded.BM_Mail ELSE Accounts.BM_Mail END;
            ";

            var pId = cmd.Parameters.Add("$id", SqliteType.Text);
            var pPass = cmd.Parameters.Add("$pass", SqliteType.Text);
            var pMa2fa = cmd.Parameters.Add("$ma2fa", SqliteType.Text);
            var pCookie = cmd.Parameters.Add("$cookie", SqliteType.Text);
            var pToken = cmd.Parameters.Add("$token", SqliteType.Text);
            var pMail = cmd.Parameters.Add("$mail", SqliteType.Text);
            var pPassMail = cmd.Parameters.Add("$passMail", SqliteType.Text);
            var pTrangThai = cmd.Parameters.Add("$trangThai", SqliteType.Text);
            var pVaiTro = cmd.Parameters.Add("$vaiTro", SqliteType.Text);
            var pTenTaiKhoan = cmd.Parameters.Add("$tenTaiKhoan", SqliteType.Text);
            var pBm = cmd.Parameters.Add("$bm", SqliteType.Text);
            var pBmMail = cmd.Parameters.Add("$bmMail", SqliteType.Text);

            int count = 0;
            foreach (var acc in accounts)
            {
                if (string.IsNullOrWhiteSpace(acc.ID))
                    continue;

                pId.Value = acc.ID;
                pPass.Value = acc.PASS;
                pMa2fa.Value = acc.Ma2FA;
                pCookie.Value = acc.Cookie;
                pToken.Value = acc.Token;
                pMail.Value = acc.MAIL;
                pPassMail.Value = acc.PASS_MAIL;
                pTrangThai.Value = acc.TrangThai;
                pVaiTro.Value = acc.VaiTro;
                pTenTaiKhoan.Value = acc.TenTaiKhoan;
                pBm.Value = acc.BM;
                pBmMail.Value = acc.BM_Mail;

                cmd.ExecuteNonQuery();
                count++;
            }

            transaction.Commit();
            return count;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static void UpdateAccountStatus(string connectionString, string id, string status)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Accounts SET TrangThai = $status WHERE ID = $id;";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();

        if (status.Equals("die", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("check", StringComparison.OrdinalIgnoreCase))
        {
            ReleaseAssignedPages(connectionString, id);
        }
    }

    public static void UpdateAccountCookie(string connectionString, string id, string cookie)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Accounts SET Cookie = $cookie WHERE ID = $id;";
        cmd.Parameters.AddWithValue("$cookie", cookie);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateLastRunTime(string connectionString, string id, DateTime lastRunTime)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Accounts SET LastRunTime = $time WHERE ID = $id;";
        cmd.Parameters.AddWithValue("$time", lastRunTime.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static DateTime? GetLastRunTime(string connectionString, string id)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT LastRunTime FROM Accounts WHERE ID = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        var result = cmd.ExecuteScalar();
        if (result != null && DateTime.TryParse(result.ToString(), out var dt))
        {
            return dt;
        }
        return null;
    }

    public static void UpdateAccountPageCount(string connectionString, string id, int pageCount)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Accounts SET PageCount = $count WHERE ID = $id;";
        cmd.Parameters.AddWithValue("$count", pageCount);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public static void UpdatePageCount(string connectionString, string id, int count) => UpdateAccountPageCount(connectionString, id, count);

    public static int GetTotalPageCount(string connectionString)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(PageCount), 0) FROM Accounts WHERE TrangThai IS NULL OR (TrangThai <> 'die' AND TrangThai <> 'check');";
            var result = cmd.ExecuteScalar();
            int count = (result != null && int.TryParse(result.ToString(), out int cnt)) ? cnt : 0;

            if (count == 0)
            {
                using var cmd2 = conn.CreateCommand();
                cmd2.CommandText = "SELECT COUNT(DISTINCT Page_ID) FROM AssignedPages;";
                var res2 = cmd2.ExecuteScalar();
                if (res2 != null && int.TryParse(res2.ToString(), out int cnt2))
                {
                    count = cnt2;
                }
            }

            if (count == 0 && GlobalState.ProcessedPageIds.Count > 0)
            {
                count = GlobalState.ProcessedPageIds.Count;
            }

            return count;
        }
        catch
        {
            return GlobalState.ProcessedPageIds.Count;
        }
    }

    public static HashSet<string> GetAllAssignedPageIds(string connectionString)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Page_ID FROM AssignedPages;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                set.Add(reader.GetString(0));
        }

        return set;
    }

    public static HashSet<string> GetAssignedPageIdsInBM(string connectionString, string bm, string currentViaUid)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ap.Page_ID
            FROM AssignedPages ap
            LEFT JOIN Accounts acc ON ap.Via_UID = acc.ID
            WHERE ap.BM = $bm
              AND ap.Via_UID <> $currentViaUid
              AND (acc.TrangThai = 'live' OR acc.TrangThai = 'running' OR acc.TrangThai = 'lock' OR acc.TrangThai IS NULL);
        ";
        cmd.Parameters.AddWithValue("$bm", bm);
        cmd.Parameters.AddWithValue("$currentViaUid", currentViaUid);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
                set.Add(reader.GetString(0));
        }

        return set;
    }

    public static void AssignPages(string connectionString, string bm, string viaUid, IEnumerable<(string PageId, string SessionData)> pages)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO AssignedPages (Page_ID, BM, Via_UID, Session_Data)
                VALUES ($pageId, $bm, $viaUid, $sessionData)
                ON CONFLICT(Page_ID) DO UPDATE SET
                    BM = excluded.BM,
                    Via_UID = excluded.Via_UID,
                    Session_Data = excluded.Session_Data;
            ";

            var pPageId = cmd.Parameters.Add("$pageId", SqliteType.Text);
            var pBm = cmd.Parameters.Add("$bm", SqliteType.Text);
            var pViaUid = cmd.Parameters.Add("$viaUid", SqliteType.Text);
            var pSessionData = cmd.Parameters.Add("$sessionData", SqliteType.Text);

            pBm.Value = bm;
            pViaUid.Value = viaUid;

            foreach (var (pageId, sessionData) in pages)
            {
                pPageId.Value = pageId;
                pSessionData.Value = sessionData;
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static void ReleaseAssignedPages(string connectionString, string viaUid)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM AssignedPages WHERE Via_UID = $viaUid;";
        cmd.Parameters.AddWithValue("$viaUid", viaUid);
        cmd.ExecuteNonQuery();
    }
}
