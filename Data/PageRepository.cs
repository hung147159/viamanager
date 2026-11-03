using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ViaManager.Models;
using ViaManager.Utilities;

namespace ViaManager.Data
{
    public class PageRepository
    {
        private readonly string _connectionString;

        public PageRepository(string connectionString)
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

        public void AssignPagesAtomic(string bm, string viaUid, IEnumerable<ManagedPage> pages)
        {
            using var conn = CreateOpenConnection();
            using var transaction = conn.BeginTransaction();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO AssignedPages (
                        Page_ID, BM, Via_UID, Session_Data,
                        VerificationStatus, VerifiedAt, VerificationSignals,
                        CreatedAt, UpdatedAt
                    )
                    VALUES (
                        $pageId, $bm, $viaUid, $sessionData,
                        $verificationStatus, $verifiedAt, $signals,
                        $createdAt, $updatedAt
                    )
                    ON CONFLICT(Page_ID) DO UPDATE SET
                        BM = excluded.BM,
                        Via_UID = excluded.Via_UID,
                        Session_Data = excluded.Session_Data,
                        VerificationStatus = excluded.VerificationStatus,
                        VerifiedAt = excluded.VerifiedAt,
                        VerificationSignals = excluded.VerificationSignals,
                        UpdatedAt = excluded.UpdatedAt;
                ";

                var pPageId = cmd.Parameters.Add("$pageId", SqliteType.Text);
                var pBm = cmd.Parameters.Add("$bm", SqliteType.Text);
                var pViaUid = cmd.Parameters.Add("$viaUid", SqliteType.Text);
                var pSessionData = cmd.Parameters.Add("$sessionData", SqliteType.Text);
                var pStatus = cmd.Parameters.Add("$verificationStatus", SqliteType.Integer);
                var pVerifiedAt = cmd.Parameters.Add("$verifiedAt", SqliteType.Text);
                var pSignals = cmd.Parameters.Add("$signals", SqliteType.Text);
                var pCreatedAt = cmd.Parameters.Add("$createdAt", SqliteType.Text);
                var pUpdatedAt = cmd.Parameters.Add("$updatedAt", SqliteType.Text);

                string nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                pBm.Value = string.IsNullOrWhiteSpace(bm) ? "Bmv1" : bm;
                pViaUid.Value = viaUid;

                foreach (var page in pages)
                {
                    if (string.IsNullOrWhiteSpace(page.PageId))
                        continue;

                    pPageId.Value = page.PageId.Trim();
                    pSessionData.Value = page.RawCookieSnapshot ?? string.Empty;
                    pStatus.Value = (int)page.VerificationStatus;
                    pVerifiedAt.Value = (object?)page.VerifiedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value;
                    pSignals.Value = page.VerificationSignals != null && page.VerificationSignals.Count > 0
                        ? JsonSerializer.Serialize(page.VerificationSignals)
                        : string.Empty;
                    pCreatedAt.Value = nowStr;
                    pUpdatedAt.Value = nowStr;

                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                AppLogger.Error("PageRepository", "AssignPagesAtomic failed, transaction rolled back.", ex);
                throw;
            }
        }

        public HashSet<string> GetAllAssignedPageIds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = CreateOpenConnection();
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

        public HashSet<string> GetAssignedPageIdsInBM(string bm, string currentViaUid)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var conn = CreateOpenConnection();
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

        public List<ManagedPage> GetPagesByViaUid(string viaUid)
        {
            var list = new List<ManagedPage>();
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Page_ID, BM, Via_UID, Session_Data, VerificationStatus, VerifiedAt, VerificationSignals
                FROM AssignedPages
                WHERE Via_UID = $viaUid;
            ";
            cmd.Parameters.AddWithValue("$viaUid", viaUid);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var page = new ManagedPage
                {
                    PageId = reader.GetString(0),
                    BusinessManager = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ParentViaUid = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    RawCookieSnapshot = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    VerificationStatus = (VerificationStatus)(reader.IsDBNull(4) ? 0 : reader.GetInt32(4))
                };

                if (!reader.IsDBNull(5) && DateTime.TryParse(reader.GetString(5), out var va))
                {
                    page.VerifiedAt = va;
                }

                if (!reader.IsDBNull(6))
                {
                    try
                    {
                        var sigJson = reader.GetString(6);
                        if (!string.IsNullOrWhiteSpace(sigJson))
                        {
                            var sigs = JsonSerializer.Deserialize<List<VerificationSignal>>(sigJson);
                            if (sigs != null) page.VerificationSignals = sigs;
                        }
                    }
                    catch { }
                }

                list.Add(page);
            }

            return list;
        }

        public void ReleaseAssignedPages(string viaUid)
        {
            using var conn = CreateOpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM AssignedPages WHERE Via_UID = $viaUid;";
            cmd.Parameters.AddWithValue("$viaUid", viaUid);
            cmd.ExecuteNonQuery();
        }
    }
}
