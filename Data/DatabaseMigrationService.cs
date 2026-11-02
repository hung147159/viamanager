using System;
using Microsoft.Data.Sqlite;
using ViaManager.Utilities;

namespace ViaManager.Data
{
    public static class DatabaseMigrationService
    {
        private const int CurrentSchemaVersion = 2;

        public static void Migrate(string connectionString)
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            // Enforce WAL, foreign keys, and busy timeout
            using (var pragmaCmd = conn.CreateCommand())
            {
                pragmaCmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA foreign_keys = ON;
                    PRAGMA busy_timeout = 15000;
                ";
                pragmaCmd.ExecuteNonQuery();
            }

            int userVersion = 0;
            using (var versionCmd = conn.CreateCommand())
            {
                versionCmd.CommandText = "PRAGMA user_version;";
                var res = versionCmd.ExecuteScalar();
                if (res != null && int.TryParse(res.ToString(), out int ver))
                {
                    userVersion = ver;
                }
            }

            AppLogger.Info("DatabaseMigration", $"Current DB schema user_version: {userVersion}. Target: {CurrentSchemaVersion}");

            if (userVersion < 1)
            {
                ApplyMigrationV1(conn);
            }

            if (userVersion < 2)
            {
                ApplyMigrationV2(conn);
            }

            using (var setVerCmd = conn.CreateCommand())
            {
                setVerCmd.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
                setVerCmd.ExecuteNonQuery();
            }

            AppLogger.Info("DatabaseMigration", "Database schema migration completed successfully.");
        }

        private static void ApplyMigrationV1(SqliteConnection conn)
        {
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Accounts (
                        ID TEXT PRIMARY KEY,
                        PASS TEXT,
                        Ma2FA TEXT,
                        MAIL TEXT,
                        PASS_MAIL TEXT,
                        TrangThai TEXT DEFAULT 'live',
                        VaiTro TEXT DEFAULT 'Run',
                        TenTaiKhoan TEXT,
                        BM TEXT DEFAULT 'Bmv1',
                        BM_Mail TEXT,
                        Cookie TEXT,
                        Token TEXT,
                        LastRunTime TEXT,
                        PageCount INTEGER DEFAULT 0,
                        CreatedAt TEXT,
                        UpdatedAt TEXT
                    );

                    CREATE TABLE IF NOT EXISTS AssignedPages (
                        Page_ID TEXT PRIMARY KEY,
                        BM TEXT,
                        Via_UID TEXT,
                        Session_Data TEXT,
                        VerificationStatus INTEGER DEFAULT 0,
                        VerifiedAt TEXT,
                        VerificationSignals TEXT,
                        CreatedAt TEXT,
                        UpdatedAt TEXT,
                        FOREIGN KEY (Via_UID) REFERENCES Accounts(ID) ON DELETE SET NULL
                    );

                    CREATE INDEX IF NOT EXISTS IX_AssignedPages_Via_UID ON AssignedPages(Via_UID);
                    CREATE INDEX IF NOT EXISTS IX_AssignedPages_BM ON AssignedPages(BM);
                ";
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static void ApplyMigrationV2(SqliteConnection conn)
        {
            using var tx = conn.BeginTransaction();
            try
            {
                // Ensure columns exist on Accounts
                EnsureColumnExists(conn, tx, "Accounts", "CreatedAt", "TEXT");
                EnsureColumnExists(conn, tx, "Accounts", "UpdatedAt", "TEXT");

                // Ensure columns exist on AssignedPages
                EnsureColumnExists(conn, tx, "AssignedPages", "VerificationStatus", "INTEGER DEFAULT 0");
                EnsureColumnExists(conn, tx, "AssignedPages", "VerifiedAt", "TEXT");
                EnsureColumnExists(conn, tx, "AssignedPages", "VerificationSignals", "TEXT");
                EnsureColumnExists(conn, tx, "AssignedPages", "CreatedAt", "TEXT");
                EnsureColumnExists(conn, tx, "AssignedPages", "UpdatedAt", "TEXT");

                // Ensure indices exist
                using var idxCmd = conn.CreateCommand();
                idxCmd.Transaction = tx;
                idxCmd.CommandText = @"
                    CREATE INDEX IF NOT EXISTS IX_AssignedPages_Via_UID ON AssignedPages(Via_UID);
                    CREATE INDEX IF NOT EXISTS IX_AssignedPages_BM ON AssignedPages(BM);
                ";
                idxCmd.ExecuteNonQuery();

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        private static void EnsureColumnExists(SqliteConnection conn, SqliteTransaction tx, string table, string column, string definition)
        {
            bool exists = false;
            using var checkCmd = conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = checkCmd.ExecuteReader();
            while (reader.Read())
            {
                string colName = reader.GetString(1);
                if (colName.Equals(column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.Transaction = tx;
                alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                alterCmd.ExecuteNonQuery();
            }
        }
    }
}
