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
