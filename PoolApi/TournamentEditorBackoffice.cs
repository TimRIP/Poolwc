using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace DrukDatabaseLayer
{
    public class TournamentEditorAccess
    {
        public int TournamentId { get; set; }
        public bool IsAdmin { get; set; }
        public bool IsMatchEditor { get; set; }
        public bool CanEditMatches { get; set; }
    }

    public class TournamentEditorDetails
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string RegisteredName { get; set; }
        public string RegisteredEmail { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TournamentEditorBackoffice
    {
        private readonly IConfigurationRoot _configuration;

        public TournamentEditorBackoffice()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public bool IsTournamentAdmin(int tournamentId, int userId)
        {
            const string sql = @"
SELECT COUNT(1)
FROM Tournament
WHERE Id = @TournamentId
  AND Admin = @UserId;";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public TournamentEditorAccess GetAccess(int tournamentId, int userId)
        {
            const string sql = @"
SELECT
    T.Id,
    CASE WHEN T.Admin = @UserId THEN 1 ELSE 0 END AS IsAdmin,
    CASE WHEN E.RegisteredUserId IS NULL THEN 0 ELSE 1 END AS IsMatchEditor
FROM Tournament T
LEFT JOIN TournamentMatchEditor E
    ON E.TournamentId = T.Id
   AND E.RegisteredUserId = @UserId
WHERE T.Id = @TournamentId;";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read()) return null;

                    bool isAdmin = Convert.ToBoolean(reader["IsAdmin"]);
                    bool isEditor = Convert.ToBoolean(reader["IsMatchEditor"]);
                    return new TournamentEditorAccess
                    {
                        TournamentId = Convert.ToInt32(reader["Id"]),
                        IsAdmin = isAdmin,
                        IsMatchEditor = isEditor,
                        CanEditMatches = isAdmin || isEditor
                    };
                }
            }
        }

        public List<TournamentEditorDetails> GetEditors(int tournamentId, int adminUserId)
        {
            EnsureTournamentAdmin(tournamentId, adminUserId);

            const string sql = @"
SELECT
    U.RegisteredUserID,
    U.UserName,
    U.RegisteredName,
    U.RegisteredEMail,
    E.CreatedAt
FROM TournamentMatchEditor E
INNER JOIN RegisteredUsers U ON U.RegisteredUserID = E.RegisteredUserId
WHERE E.TournamentId = @TournamentId
ORDER BY U.UserName;";

            List<TournamentEditorDetails> editors = new List<TournamentEditorDetails>();

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        editors.Add(new TournamentEditorDetails
                        {
                            UserId = Convert.ToInt32(reader["RegisteredUserID"]),
                            UserName = Convert.ToString(reader["UserName"]),
                            RegisteredName = reader["RegisteredName"] == DBNull.Value ? null : Convert.ToString(reader["RegisteredName"]),
                            RegisteredEmail = reader["RegisteredEMail"] == DBNull.Value ? null : Convert.ToString(reader["RegisteredEMail"]),
                            CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
                        });
                    }
                }
            }

            return editors;
        }

        public TournamentEditorDetails AddEditor(int tournamentId, int adminUserId, string userName)
        {
            EnsureTournamentAdmin(tournamentId, adminUserId);

            string normalized = (userName ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                throw new ArgumentException("Username is required.");
            }

            const string findSql = @"
SELECT TOP 1
    RegisteredUserID,
    UserName,
    RegisteredName,
    RegisteredEMail
FROM RegisteredUsers
WHERE UserName = @UserName;";

            int editorUserId;
            string foundUserName;
            string registeredName;
            string registeredEmail;

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(findSql, conn))
            {
                cmd.Parameters.Add("@UserName", SqlDbType.VarChar, 30).Value = normalized;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException("No registered user was found with username '" + normalized + "'.");
                    }

                    editorUserId = Convert.ToInt32(reader["RegisteredUserID"]);
                    foundUserName = Convert.ToString(reader["UserName"]);
                    registeredName = reader["RegisteredName"] == DBNull.Value ? null : Convert.ToString(reader["RegisteredName"]);
                    registeredEmail = reader["RegisteredEMail"] == DBNull.Value ? null : Convert.ToString(reader["RegisteredEMail"]);
                }
            }

            if (editorUserId == adminUserId)
            {
                throw new InvalidOperationException("The tournament administrator already has match-edit access.");
            }

            const string insertSql = @"
IF NOT EXISTS (
    SELECT 1
    FROM TournamentMatchEditor
    WHERE TournamentId = @TournamentId
      AND RegisteredUserId = @EditorUserId
)
BEGIN
    INSERT INTO TournamentMatchEditor
        (TournamentId, RegisteredUserId, AddedByUserId, CreatedAt)
    VALUES
        (@TournamentId, @EditorUserId, @AdminUserId, GETUTCDATE());
END

SELECT CreatedAt
FROM TournamentMatchEditor
WHERE TournamentId = @TournamentId
  AND RegisteredUserId = @EditorUserId;";

            DateTime createdAt;
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(insertSql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@EditorUserId", SqlDbType.Int).Value = editorUserId;
                cmd.Parameters.Add("@AdminUserId", SqlDbType.Int).Value = adminUserId;
                conn.Open();
                createdAt = Convert.ToDateTime(cmd.ExecuteScalar());
            }

            return new TournamentEditorDetails
            {
                UserId = editorUserId,
                UserName = foundUserName,
                RegisteredName = registeredName,
                RegisteredEmail = registeredEmail,
                CreatedAt = createdAt
            };
        }

        public void RemoveEditor(int tournamentId, int adminUserId, int editorUserId)
        {
            EnsureTournamentAdmin(tournamentId, adminUserId);

            const string sql = @"
DELETE FROM TournamentMatchEditor
WHERE TournamentId = @TournamentId
  AND RegisteredUserId = @EditorUserId;";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@EditorUserId", SqlDbType.Int).Value = editorUserId;
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureTournamentAdmin(int tournamentId, int userId)
        {
            if (!IsTournamentAdmin(tournamentId, userId))
            {
                throw new UnauthorizedAccessException("Only the tournament administrator can manage match editors.");
            }
        }
    }
}
