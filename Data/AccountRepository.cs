using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using ViaManager.Models;
using ViaManager.Utilities;

namespace ViaManager.Data
{
    public class AccountRepository
    {
        private readonly string _connectionString;

        public AccountRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        private SqliteConnection CreateOpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA busy_timeout = 30000;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA journal_mode = WAL;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA synchronous = NORMAL;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA foreign_keys = ON;";
                cmd.ExecuteNonQuery();
            }
            return conn;
        }

        public int BatchUpsert(IEnumerable<ViaAccount> accounts, bool encryptSecrets = false)
        {
            using var conn = CreateOpenConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO Accounts (
                        ID, PASS, Ma2FA, Cookie, Token, MAIL, PASS_MAIL,
                        TrangThai, VaiTro, TenTaiKhoan, BM, BM_Mail,
                        LastRunTime, PageCount, CreatedAt, UpdatedAt
                    )
                    VALUES (
                        $id, $pass, $ma2fa, $cookie, $token, $mail, $passMail,
                        $trangThai, $vaiTro, $tenTaiKhoan, $bm, $bmMail,
                        $lastRunTime, $pageCount, $createdAt, $updatedAt
                    )
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
                        BM_Mail = CASE WHEN excluded.BM_Mail <> '' THEN excluded.BM_Mail ELSE Accounts.BM_Mail END,
                        UpdatedAt = excluded.UpdatedAt;
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
                var pLastRun = cmd.Parameters.Add("$lastRunTime", SqliteType.Text);
                var pPageCount = cmd.Parameters.Add("$pageCount", SqliteType.Integer);
                var pCreatedAt = cmd.Parameters.Add("$createdAt", SqliteType.Text);
                var pUpdatedAt = cmd.Parameters.Add("$updatedAt", SqliteType.Text);

                string nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                int count = 0;

                foreach (var acc in accounts)
                {
                    if (string.IsNullOrWhiteSpace(acc.Uid))
                        continue;

                    string passToStore = encryptSecrets ? SecureDataProtector.Protect(acc.Password) : acc.Password;
                    string ma2faToStore = encryptSecrets ? SecureDataProtector.Protect(acc.TwoFactorSecret) : acc.TwoFactorSecret;

                    pId.Value = acc.Uid.Trim();
                    pPass.Value = passToStore;
                    pMa2fa.Value = ma2faToStore;
                    pCookie.Value = acc.Cookie;
                    pToken.Value = acc.Token;
                    pMail.Value = acc.Email;
                    pPassMail.Value = acc.EmailPassword;
                    pTrangThai.Value = string.IsNullOrWhiteSpace(acc.Status) ? "live" : acc.Status;
                    pVaiTro.Value = string.IsNullOrWhiteSpace(acc.Role) ? "Run" : acc.Role;
                    pTenTaiKhoan.Value = acc.AccountName;
                    pBm.Value = string.IsNullOrWhiteSpace(acc.BusinessManager) ? "Bmv1" : acc.BusinessManager;
                    pBmMail.Value = acc.BusinessManagerMail;
                    pLastRun.Value = (object?)acc.LastRunTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value;
                    pPageCount.Value = acc.PageCount;
                    pCreatedAt.Value = nowStr;
                    pUpdatedAt.Value = nowStr;

                    cmd.ExecuteNonQuery();
                    count++;
                }

                transaction.Commit();
                return count;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                AppLogger.Error("AccountRepository", "BatchUpsert failed, transaction rolled back.", ex);
                throw;
            }
        }

        public List<ViaAccount> GetAllAccounts(bool decryptSecrets = false)
        {
            var list = new List<ViaAccount>();
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ID, PASS, Ma2FA, Cookie, Token, MAIL, PASS_MAIL,
                       TrangThai, VaiTro, TenTaiKhoan, BM, BM_Mail,
                       LastRunTime, PageCount, CreatedAt, UpdatedAt
                FROM Accounts;
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string pass = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string ma2fa = reader.IsDBNull(2) ? "" : reader.GetString(2);

                if (decryptSecrets)
                {
                    pass = SecureDataProtector.Unprotect(pass);
                    ma2fa = SecureDataProtector.Unprotect(ma2fa);
                }

                var acc = new ViaAccount
                {
                    Uid = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Password = pass,
                    TwoFactorSecret = ma2fa,
                    Cookie = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Token = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Email = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    EmailPassword = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    Status = reader.IsDBNull(7) ? "live" : reader.GetString(7),
                    Role = reader.IsDBNull(8) ? "Run" : reader.GetString(8),
                    AccountName = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    BusinessManager = reader.IsDBNull(10) ? "Bmv1" : reader.GetString(10),
                    BusinessManagerMail = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    PageCount = reader.IsDBNull(13) ? 0 : reader.GetInt32(13)
                };

                if (!reader.IsDBNull(12) && DateTime.TryParse(reader.GetString(12), out var lrt))
                {
                    acc.LastRunTime = lrt;
                }

                list.Add(acc);
            }

            return list;
        }

        public void UpdateAccountStatus(string uid, string status)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Accounts
                SET TrangThai = $status, UpdatedAt = $now
                WHERE ID = $id;
            ";
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$id", uid);
            cmd.ExecuteNonQuery();
        }

        public void UpdateAccountCookie(string uid, string cookie)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Accounts
                SET Cookie = $cookie, UpdatedAt = $now
                WHERE ID = $id;
            ";
            cmd.Parameters.AddWithValue("$cookie", cookie);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$id", uid);
            cmd.ExecuteNonQuery();
        }

        public void UpdateLastRunTime(string uid, DateTime lastRunTime)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Accounts
                SET LastRunTime = $time, UpdatedAt = $now
                WHERE ID = $id;
            ";
            cmd.Parameters.AddWithValue("$time", lastRunTime.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$id", uid);
            cmd.ExecuteNonQuery();
        }

        public void UpdateAccountPageCount(string uid, int pageCount)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Accounts
                SET PageCount = $count, UpdatedAt = $now
                WHERE ID = $id;
            ";
            cmd.Parameters.AddWithValue("$count", pageCount);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$id", uid);
            cmd.ExecuteNonQuery();
        }

        public int GetTotalPageCount()
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(PageCount), 0)
                FROM Accounts
                WHERE TrangThai IS NULL OR (TrangThai <> 'die' AND TrangThai <> 'check');
            ";
            var result = cmd.ExecuteScalar();
            return (result != null && int.TryParse(result.ToString(), out int cnt)) ? cnt : 0;
        }

        public int DeleteAccounts(IEnumerable<string> uids)
        {
            if (uids == null) return 0;
            var uidList = uids.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (uidList.Count == 0) return 0;

            const int maxRetries = 3;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var conn = CreateOpenConnection();
                    using var transaction = conn.BeginTransaction(deferred: false);
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

                        int count = 0;
                        foreach (var id in uidList)
                        {
                            pAssignedId.Value = id;
                            cmdAssigned.ExecuteNonQuery();

                            pAccountId.Value = id;
                            cmdAccount.ExecuteNonQuery();

                            count++;
                        }

                        transaction.Commit();
                        return count;
                    }
                    catch (Exception ex)
                    {
                        try { transaction.Rollback(); } catch { }
                        if (attempt == maxRetries)
                        {
                            AppLogger.Error("AccountRepository", "DeleteAccounts failed after retries, transaction rolled back.", ex);
                            throw;
                        }
                    }
                }
                catch (SqliteException sex) when (sex.SqliteErrorCode == 5 && attempt < maxRetries)
                {
                    System.Threading.Thread.Sleep(500 * attempt);
                    continue;
                }
            }
            return 0;
        }
    }
}
