using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace DrukDatabaseLayer
{
    public class TournamentUnregisteredPlayer
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    public class TournamentNameShuffleAssignment
    {
        public int PlayerId { get; set; }
        public int SourcePlayerId { get; set; }
    }

    public class TournamentPlayerBackoffice
    {
        private readonly IConfigurationRoot _configuration;

        public TournamentPlayerBackoffice()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public List<TournamentUnregisteredPlayer> GetUnregisteredPlayers(int tournamentId, int adminUserId)
        {
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                if (!IsTournamentAdmin(conn, null, tournamentId, adminUserId))
                {
                    throw new UnauthorizedAccessException("Only the tournament administrator can rename unregistered players.");
                }

                const string sql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId FROM Tournament WHERE Id = @TournamentId;

IF @RootMatchId IS NULL
BEGIN
    RETURN;
END;

;WITH TournamentTree AS
(
    SELECT M.Id AS MatchId, CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @RootMatchId

    UNION ALL

    SELECT M.Id, CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> @RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
),
TournamentPlayers AS
(
    SELECT DISTINCT P.Id AS PlayerId, P.Name AS PlayerName
    FROM TournamentTree TT
    INNER JOIN Seat S ON S.MatchId = TT.MatchId
    INNER JOIN Player P ON P.Id = S.PlayerId
    WHERE P.RegisteredUserID IS NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM TournamentRegistration TR
          WHERE TR.PlayerId = P.Id
            AND TR.Status = 'registered'
      )
)
SELECT PlayerId, PlayerName
FROM TournamentPlayers
ORDER BY
    CASE WHEN PlayerName LIKE 'Player:%' THEN 0 ELSE 1 END,
    CASE WHEN PlayerName LIKE 'Player:%' THEN TRY_CONVERT(INT, REPLACE(PlayerName, 'Player:', '')) ELSE NULL END,
    PlayerName,
    PlayerId
OPTION (MAXRECURSION 1000);";

                var players = new List<TournamentUnregisteredPlayer>();
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            players.Add(new TournamentUnregisteredPlayer
                            {
                                PlayerId = Convert.ToInt32(reader["PlayerId"]),
                                PlayerName = Convert.ToString(reader["PlayerName"])
                            });
                        }
                    }
                }

                return players;
            }
        }

        public TournamentUnregisteredPlayer RenamePlayer(int tournamentId, int adminUserId, int playerId, string playerName)
        {
            string cleanName = (playerName ?? string.Empty).Trim();
            if (cleanName.Length == 0)
            {
                throw new InvalidOperationException("Player name cannot be empty.");
            }

            if (cleanName.Length > 255)
            {
                throw new InvalidOperationException("Player name cannot be longer than 255 characters.");
            }

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        if (!IsTournamentAdmin(conn, transaction, tournamentId, adminUserId))
                        {
                            throw new UnauthorizedAccessException("Only the tournament administrator can rename unregistered players.");
                        }

                        const string eligibilitySql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId
FROM Tournament WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @TournamentId;

IF @RootMatchId IS NULL
BEGIN
    SELECT CAST(0 AS INT);
    RETURN;
END;

;WITH TournamentTree AS
(
    SELECT M.Id AS MatchId, CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @RootMatchId

    UNION ALL

    SELECT M.Id, CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> @RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT COUNT(1)
FROM Player P WITH (UPDLOCK, HOLDLOCK)
WHERE P.Id = @PlayerId
  AND P.RegisteredUserID IS NULL
  AND EXISTS
  (
      SELECT 1
      FROM TournamentTree TT
      INNER JOIN Seat S ON S.MatchId = TT.MatchId
      WHERE S.PlayerId = P.Id
  )
  AND NOT EXISTS
  (
      SELECT 1
      FROM TournamentRegistration TR WITH (UPDLOCK, HOLDLOCK)
      WHERE TR.PlayerId = P.Id
        AND TR.Status = 'registered'
  )
OPTION (MAXRECURSION 1000);";

                        int eligible;
                        using (SqlCommand eligibility = new SqlCommand(eligibilitySql, conn, transaction))
                        {
                            eligibility.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                            eligibility.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                            eligible = Convert.ToInt32(eligibility.ExecuteScalar());
                        }

                        if (eligible != 1)
                        {
                            throw new InvalidOperationException("This player cannot be renamed because it does not belong to the tournament or is linked to a registered user.");
                        }

                        using (SqlCommand update = new SqlCommand(@"
UPDATE Player
SET Name = @PlayerName,
    LastUsedAt = GETUTCDATE()
WHERE Id = @PlayerId
  AND RegisteredUserID IS NULL;", conn, transaction))
                        {
                            update.Parameters.Add("@PlayerName", SqlDbType.NVarChar, 255).Value = cleanName;
                            update.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;

                            if (update.ExecuteNonQuery() != 1)
                            {
                                throw new InvalidOperationException("The player name could not be changed.");
                            }
                        }

                        transaction.Commit();
                        return new TournamentUnregisteredPlayer
                        {
                            PlayerId = playerId,
                            PlayerName = cleanName
                        };
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        // Copy existing player names to a permutation of Player IDs, and move
        // child ParentPlayerId links with the original names, atomically.
        public void RandomizeNamesWithParents(int tournamentId, int adminUserId, List<TournamentNameShuffleAssignment> assignments)
        {
            if (assignments == null || assignments.Count < 2)
                throw new InvalidOperationException("Choose at least two players.");

            using (var conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        if (!IsTournamentAdmin(conn, tx, tournamentId, adminUserId))
                            throw new UnauthorizedAccessException("Only the tournament owner can randomize player names.");

                        // Keep the operation independent of which player records
                        // happen to be displayed in an older browser session.
                        // This selection excludes registered players and child players;
                        // registered account names and nested parent trees must not be moved.
                        const string eligibleSql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId FROM Tournament WITH (UPDLOCK, HOLDLOCK) WHERE Id = @TournamentId;
;WITH TournamentTree AS
(
 SELECT M.Id AS MatchId, CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
 FROM [Match] M WHERE M.Id = @RootMatchId
 UNION ALL
 SELECT M.Id, CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
 FROM TournamentTree TT JOIN [Match] M ON M.ParentMatchId = TT.MatchId
 WHERE CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT DISTINCT P.Id, P.Name
FROM TournamentTree TT
JOIN Seat S ON S.MatchId = TT.MatchId
JOIN Player P WITH (UPDLOCK, HOLDLOCK) ON P.Id = S.PlayerId
WHERE P.RegisteredUserID IS NULL AND P.ParentPlayerId IS NULL
 AND NOT EXISTS (SELECT 1 FROM TournamentRegistration TR WHERE TR.PlayerId=P.Id AND TR.Status='registered')
OPTION (MAXRECURSION 1000);";
                        var names = new Dictionary<int, string>();
                        using (var cmd = new SqlCommand(eligibleSql, conn, tx))
                        {
                            cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                            using (var reader = cmd.ExecuteReader())
                                while (reader.Read()) names.Add(reader.GetInt32(0), reader.GetString(1));
                        }

                        // Require an exact permutation of all eligible top-level
                        // player IDs; never accept arbitrary IDs or partial remaps.
                        if (assignments.Count != names.Count)
                            throw new InvalidOperationException("The eligible players changed. Refresh and try again.");
                        var targets = new HashSet<int>();
                        var sources = new HashSet<int>();
                        foreach (var assignment in assignments)
                        {
                            if (assignment == null || !names.ContainsKey(assignment.PlayerId) ||
                                !names.ContainsKey(assignment.SourcePlayerId) ||
                                !targets.Add(assignment.PlayerId) || !sources.Add(assignment.SourcePlayerId))
                                throw new InvalidOperationException("The randomization contains invalid or duplicate player IDs.");
                        }
                        if (targets.Count != names.Count || sources.Count != names.Count)
                            throw new InvalidOperationException("The randomization is not a complete permutation.");

                        // Read the entire mapping before writing names. SourcePlayerId
                        // is the old holder of the name; PlayerId is the new holder.
                        foreach (var assignment in assignments)
                        {
                            using (var cmd = new SqlCommand(@"
UPDATE Player SET Name=@Name, LastUsedAt=GETUTCDATE()
WHERE Id=@PlayerId AND RegisteredUserID IS NULL AND ParentPlayerId IS NULL;", conn, tx))
                            {
                                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 255).Value = names[assignment.SourcePlayerId];
                                cmd.Parameters.Add("@PlayerId", SqlDbType.Int).Value = assignment.PlayerId;
                                if (cmd.ExecuteNonQuery() != 1)
                                    throw new InvalidOperationException("Could not update all player names.");
                            }
                        }

                        // Reparent all direct children in a *single* UPDATE from
                        // original source IDs to new holder IDs. Sequential updates
                        // would cascade and attach children to the wrong parent.
                        var whenParts = new List<string>();
                        var idParts = new List<string>();
                        using (var cmd = new SqlCommand())
                        {
                            cmd.Connection = conn;
                            cmd.Transaction = tx;
                            for (int i = 0; i < assignments.Count; i++)
                            {
                                var a = assignments[i];
                                whenParts.Add("WHEN @Old" + i + " THEN @New" + i);
                                idParts.Add("@Old" + i);
                                cmd.Parameters.Add("@Old" + i, SqlDbType.Int).Value = a.SourcePlayerId;
                                cmd.Parameters.Add("@New" + i, SqlDbType.Int).Value = a.PlayerId;
                            }
                            cmd.CommandText = "UPDATE Player SET ParentPlayerId = CASE ParentPlayerId " +
                                string.Join(" ", whenParts) + " ELSE ParentPlayerId END " +
                                "WHERE ParentPlayerId IN (" + string.Join(",", idParts) + ");";
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                    }
                    catch { tx.Rollback(); throw; }
                }
            }
        }

        private static bool IsTournamentAdmin(SqlConnection conn, SqlTransaction transaction, int tournamentId, int userId)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM Tournament
WHERE Id = @TournamentId
  AND [Admin] = @UserId;", conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }
    }
}
