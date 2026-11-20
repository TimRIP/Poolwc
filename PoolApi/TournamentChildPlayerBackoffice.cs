using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace DrukDatabaseLayer
{
    public class TournamentParentWithChildren
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public List<TournamentChildPlayer> Children { get; set; } = new List<TournamentChildPlayer>();
    }

    public class TournamentChildPlayer
    {
        public int PlayerId { get; set; }
        public int ParentPlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    // Child players are separate Player records linked by ParentPlayerId.
    // Creating a child does not assign it to a Seat or create a login account.
    public class TournamentChildPlayerBackoffice
    {
        private readonly string _connectionString;
        public TournamentChildPlayerBackoffice()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).Build();
            _connectionString = configuration["connectionstring"];
        }

        private const string TournamentTree = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId FROM Tournament WHERE Id = @TournamentId;
;WITH TournamentTree AS
(
  SELECT M.Id AS MatchId, CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
  FROM [Match] M WHERE M.Id = @RootMatchId
  UNION ALL
  SELECT M.Id, CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
  FROM TournamentTree TT JOIN [Match] M ON M.ParentMatchId = TT.MatchId
  WHERE CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
";

        private const string ParentsSql = TournamentTree + @"
SELECT DISTINCT P.Id AS PlayerId, P.Name AS PlayerName
FROM TournamentTree TT
JOIN Seat S ON S.MatchId = TT.MatchId
JOIN Player P ON P.Id = S.PlayerId
WHERE P.ParentPlayerId IS NULL
ORDER BY P.Name, P.Id
OPTION (MAXRECURSION 1000);";

        private const string EligibleParentSql = TournamentTree + @"
SELECT COUNT(1)
FROM Player P
WHERE P.Id = @ParentPlayerId AND P.ParentPlayerId IS NULL
AND EXISTS
(
 SELECT 1 FROM TournamentTree TT
 JOIN Seat S ON S.MatchId = TT.MatchId
 WHERE S.PlayerId = P.Id
)
OPTION (MAXRECURSION 1000);";

        private static bool IsOwner(SqlConnection connection, SqlTransaction transaction, int tournamentId, int userId)
        {
            using (var cmd = new SqlCommand("SELECT COUNT(1) FROM Tournament WHERE Id=@TournamentId AND [Admin]=@UserId", connection, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        public List<TournamentParentWithChildren> GetParents(int tournamentId, int userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                if (!IsOwner(conn, null, tournamentId, userId))
                    throw new UnauthorizedAccessException("Only the tournament owner can manage child players.");
                var parents = new List<TournamentParentWithChildren>();
                using (var cmd = new SqlCommand(ParentsSql, conn))
                {
                    cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                    using (var reader = cmd.ExecuteReader())
                        while (reader.Read())
                            parents.Add(new TournamentParentWithChildren {
                                PlayerId = reader.GetInt32(0), PlayerName = reader.GetString(1)
                            });
                }
                using (var cmd = new SqlCommand(@"
SELECT Id, ParentPlayerId, Name
FROM Player
WHERE ParentPlayerId = @ParentPlayerId
ORDER BY Name, Id", conn))
                {
                    var parentParameter = cmd.Parameters.Add("@ParentPlayerId", SqlDbType.Int);
                    foreach (var parent in parents)
                    {
                        parentParameter.Value = parent.PlayerId;
                        using (var reader = cmd.ExecuteReader())
                            while (reader.Read())
                                parent.Children.Add(new TournamentChildPlayer {
                                    PlayerId = reader.GetInt32(0),
                                    ParentPlayerId = reader.GetInt32(1),
                                    PlayerName = reader.GetString(2)
                                });
                    }
                }
                return parents;
            }
        }

        public TournamentChildPlayer CreateChild(int tournamentId, int userId, int parentPlayerId, string name)
        {
            var cleaned = (name ?? "").Trim();
            if (cleaned.Length == 0 || cleaned.Length > 255)
                throw new InvalidOperationException("Enter a child name between 1 and 255 characters.");
            if (parentPlayerId <= 0) throw new InvalidOperationException("Select a parent player.");
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        if (!IsOwner(conn, tx, tournamentId, userId))
                            throw new UnauthorizedAccessException("Only the tournament owner can manage child players.");
                        using (var check = new SqlCommand(EligibleParentSql, conn, tx))
                        {
                            check.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                            check.Parameters.Add("@ParentPlayerId", SqlDbType.Int).Value = parentPlayerId;
                            if (Convert.ToInt32(check.ExecuteScalar()) != 1)
                                throw new InvalidOperationException("The selected top-level player is not in this tournament.");
                        }
                        using (var insert = new SqlCommand(@"
IF EXISTS(SELECT 1 FROM Player WITH (UPDLOCK, HOLDLOCK)
          WHERE ParentPlayerId=@ParentPlayerId AND Name=@Name)
  THROW 50001, 'This parent already has a child with that name.', 1;
INSERT INTO Player (RegisteredUserID, Name, ParentPlayerId, CreatedAt, LastUsedAt)
OUTPUT INSERTED.Id
VALUES (NULL, @Name, @ParentPlayerId, GETUTCDATE(), GETUTCDATE());", conn, tx))
                        {
                            insert.Parameters.Add("@Name", SqlDbType.NVarChar, 255).Value = cleaned;
                            insert.Parameters.Add("@ParentPlayerId", SqlDbType.Int).Value = parentPlayerId;
                            int playerId = Convert.ToInt32(insert.ExecuteScalar());
                            tx.Commit();
                            return new TournamentChildPlayer { PlayerId = playerId, ParentPlayerId = parentPlayerId, PlayerName = cleaned };
                        }
                    }
                    catch { tx.Rollback(); throw; }
                }
            }
        }
    }
}
