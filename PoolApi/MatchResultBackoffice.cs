using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DrukDatabaseLayer
{
    public class MatchRuleDetails
    {
        public int MatchRulesId { get; set; }
        public int? PlayStyleId { get; set; }
        public string PlayStyle { get; set; }
        public int? PlayFrom { get; set; }
        public int? PlayTo { get; set; }
        public string Description { get; set; }
    }


    public class MatchVenueDetails
    {
        public int FacilityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int AssignedOnMatchId { get; set; }
    }

    public class MatchScheduleDetails
    {
        public int ScheduleId { get; set; }
        public DateTime? FromTime { get; set; }
        public DateTime? ToTime { get; set; }
        public int AssignedOnMatchId { get; set; }
    }

    public class MatchSeatDetails
    {
        public int SeatId { get; set; }
        public int? PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int? ResultMatchPlace { get; set; }
        public int? ResultPoints { get; set; }
        public int? Mmr { get; set; }
    }

    public class MatchDetails
    {
        public int MatchId { get; set; }
        public string MatchName { get; set; }
        public bool IndividualMatch { get; set; }
        public MatchRuleDetails MatchRules { get; set; }
        public MatchVenueDetails Venue { get; set; }
        public MatchScheduleDetails Schedule { get; set; }
        public List<MatchSeatDetails> Seats { get; set; } = new List<MatchSeatDetails>();
    }

    public class MatchResultUpdate
    {
        public int SeatId { get; set; }
        public int ResultMatchPlace { get; set; }
        public int? ResultPoints { get; set; }
    }

    public class MmrChangeDetails
    {
        public int RegisteredUserId { get; set; }
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int Before { get; set; }
        public int Delta { get; set; }
        public int After { get; set; }
    }

    public class AdvancedPlayerDetails
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int Place { get; set; }
        public int DestinationSeatId { get; set; }
        public int DestinationMatchId { get; set; }
        public string DestinationMatchName { get; set; }
    }

    public class StageAdvanceResult
    {
        public int? StageMatchId { get; set; }
        public string StageName { get; set; }
        public bool StageComplete { get; set; }
        public bool PlacementResolved { get; set; }
        public string Message { get; set; }
        public List<AdvancedPlayerDetails> AdvancedPlayers { get; set; } = new List<AdvancedPlayerDetails>();
        public List<MmrChangeDetails> MmrChanges { get; set; } = new List<MmrChangeDetails>();
        public string MmrMessage { get; set; }
    }

    internal class StageSeatScore
    {
        public int SeatId { get; set; }
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int Wins { get; set; }
        public int? ScoreDifference { get; set; }
        public int HeadToHeadWins { get; set; }
        public int Buchholz { get; set; }
        public bool HadBye { get; set; }
        public int? DirectPlace { get; set; }
        public int? DirectPoints { get; set; }
    }

    internal class ChildSeatResult
    {
        public int MatchId { get; set; }
        public int StageSeatId { get; set; }
        public int PlayerId { get; set; }
        public int? ResultMatchPlace { get; set; }
        public int? ResultPoints { get; set; }
    }

    internal class SwissMatchInfo
    {
        public int MatchId { get; set; }
        public int Round { get; set; }
        public int MatchRulesId { get; set; }
        public string MatchName { get; set; }
    }

    internal class SwissPair
    {
        public StageSeatScore First { get; set; }
        public StageSeatScore Second { get; set; }
    }

    internal class DestinationSeat
    {
        public int SeatId { get; set; }
        public int MatchId { get; set; }
        public string MatchName { get; set; }
        public int? PlayerId { get; set; }
    }

    public class MatchResultBackoffice
    {
        private readonly IConfigurationRoot _configuration;

        public MatchResultBackoffice()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public bool IsTournamentAdminForMatch(int matchId, int userId)
        {
            const string sql = @"
;WITH TournamentTree AS
(
    SELECT
        T.Id AS TournamentId,
        T.MatchId AS RootMatchId,
        T.Admin,
        M.Id AS MatchId,
        CAST('|'+ CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM Tournament T
    INNER JOIN [Match] M ON M.Id = T.MatchId
    WHERE T.Admin = @UserId

    UNION ALL

    SELECT
        TT.TournamentId,
        TT.RootMatchId,
        TT.Admin,
        M.Id,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> TT.RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT COUNT(1)
FROM TournamentTree
WHERE MatchId = @MatchId
OPTION (MAXRECURSION 1000);";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public bool CanEditMatchResults(int matchId, int userId)
        {
            const string sql = @"
;WITH TournamentTree AS
(
    SELECT
        T.Id AS TournamentId,
        T.MatchId AS RootMatchId,
        M.Id AS MatchId,
        CAST('|'+ CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM Tournament T
    INNER JOIN [Match] M ON M.Id = T.MatchId

    UNION ALL

    SELECT
        TT.TournamentId,
        TT.RootMatchId,
        M.Id,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> TT.RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT COUNT(1)
FROM TournamentTree TT
INNER JOIN Tournament T ON T.Id = TT.TournamentId
LEFT JOIN TournamentMatchEditor E
    ON E.TournamentId = T.Id
   AND E.RegisteredUserId = @UserId
WHERE TT.MatchId = @MatchId
  AND (T.Admin = @UserId OR E.RegisteredUserId IS NOT NULL)
OPTION (MAXRECURSION 1000);";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }


        public MatchDetails GetMatchDetails(int matchId)
        {
            const string sql = @"
SELECT
    M.Id AS MatchId,
    M.Name AS MatchName,
    M.IndividualMatch,
    MR.Id AS MatchRulesId,
    MR.PlayStyleId,
    PS.Description AS PlayStyle,
    MR.PlayFrom,
    MR.PlayTo,
    MR.Description AS MatchRuleDescription,
    SCH.Id AS ScheduleId,
    SCH.FromTime,
    SCH.ToTime,
    F.Id AS FacilityId,
    F.Name AS VenueName,
    F.Description AS VenueDescription,
    CASE
        WHEN M.ScheduleId IS NOT NULL THEN M.Id
        WHEN M.IndividualMatch = 1 AND PM.ScheduleId IS NOT NULL THEN PM.Id
        ELSE NULL
    END AS ScheduleAssignedOnMatchId,
    S.Id AS SeatId,
    S.PlayerId,
    P.Name AS PlayerName,
    S.ResultMatchPlace,
    S.ResultPoints,
    RU.Mmr AS PlayerMmr
FROM [Match] M
LEFT JOIN [Match] PM ON M.ParentMatchId = PM.Id
LEFT JOIN MatchRules MR ON M.MatchRulesId = MR.Id
LEFT JOIN PlayStyle PS ON MR.PlayStyleId = PS.Id
LEFT JOIN Schedule SCH
    ON SCH.Id = CASE
        WHEN M.ScheduleId IS NOT NULL THEN M.ScheduleId
        WHEN M.IndividualMatch = 1 THEN PM.ScheduleId
        ELSE NULL
    END
LEFT JOIN Facility F ON SCH.FacilityId = F.Id
LEFT JOIN Seat S ON S.MatchId = M.Id
LEFT JOIN Player P ON S.PlayerId = P.Id
OUTER APPLY
(
    SELECT TOP 1 TR.RegisteredUserId
    FROM TournamentRegistration TR
    WHERE TR.PlayerId = P.Id
      AND TR.Status = 'registered'
    ORDER BY TR.UpdatedAt DESC, TR.RegisteredAt DESC
) PLAYERREG
LEFT JOIN RegisteredUsers RU
    ON RU.RegisteredUserID = COALESCE(P.RegisteredUserID, PLAYERREG.RegisteredUserId)
WHERE M.Id = @MatchId
ORDER BY S.Id;";

            MatchDetails details = null;

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (details == null)
                        {
                            details = new MatchDetails
                            {
                                MatchId = Convert.ToInt32(reader["MatchId"]),
                                MatchName = Convert.ToString(reader["MatchName"]),
                                IndividualMatch = Convert.ToBoolean(reader["IndividualMatch"])
                            };

                            if (reader["MatchRulesId"] != DBNull.Value)
                            {
                                details.MatchRules = new MatchRuleDetails
                                {
                                    MatchRulesId = Convert.ToInt32(reader["MatchRulesId"]),
                                    PlayStyleId = ToNullableInt(reader["PlayStyleId"]),
                                    PlayStyle = ToNullableString(reader["PlayStyle"]),
                                    PlayFrom = ToNullableInt(reader["PlayFrom"]),
                                    PlayTo = ToNullableInt(reader["PlayTo"]),
                                    Description = ToNullableString(reader["MatchRuleDescription"])
                                };
                            }

                            if (reader["ScheduleId"] != DBNull.Value)
                            {
                                details.Schedule = new MatchScheduleDetails
                                {
                                    ScheduleId = Convert.ToInt32(reader["ScheduleId"]),
                                    FromTime = ToNullableDateTime(reader["FromTime"]),
                                    ToTime = ToNullableDateTime(reader["ToTime"]),
                                    AssignedOnMatchId = Convert.ToInt32(reader["ScheduleAssignedOnMatchId"])
                                };
                            }

                            if (reader["FacilityId"] != DBNull.Value)
                            {
                                details.Venue = new MatchVenueDetails
                                {
                                    FacilityId = Convert.ToInt32(reader["FacilityId"]),
                                    Name = ToNullableString(reader["VenueName"]),
                                    Description = ToNullableString(reader["VenueDescription"]),
                                    AssignedOnMatchId = Convert.ToInt32(reader["ScheduleAssignedOnMatchId"])
                                };
                            }
                        }

                        if (reader["SeatId"] != DBNull.Value)
                        {
                            details.Seats.Add(new MatchSeatDetails
                            {
                                SeatId = Convert.ToInt32(reader["SeatId"]),
                                PlayerId = ToNullableInt(reader["PlayerId"]),
                                PlayerName = ToNullableString(reader["PlayerName"]),
                                ResultMatchPlace = ToNullableInt(reader["ResultMatchPlace"]),
                                ResultPoints = ToNullableInt(reader["ResultPoints"]),
                                Mmr = ToNullableInt(reader["PlayerMmr"])
                            });
                        }
                    }
                }
            }

            return details;
        }

        public StageAdvanceResult SaveMatchResultsAndAdvance(int matchId, IList<MatchResultUpdate> results)
        {
            if (results == null || results.Count == 0)
            {
                throw new ArgumentException("No results were supplied.");
            }

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // If an earlier Swiss round is edited, every later Swiss pairing
                        // is no longer trustworthy. Remove later rounds first so they can
                        // be generated again from the corrected standings.
                        InvalidateFutureSwissRounds(conn, transaction, matchId);

                        // Re-saving a match must never award MMR twice. Undo any previous
                        // MMR change for this match before applying the new result.
                        UndoMmrForMatch(conn, transaction, matchId);
                        SaveMatchResults(conn, transaction, matchId, results);
                        string mmrMessage;
                        List<MmrChangeDetails> mmrChanges = ApplyMmrForMatch(conn, transaction, matchId, out mmrMessage);

                        StageAdvanceResult advancement = RecalculateParentStage(conn, transaction, matchId);
                        advancement.MmrChanges = mmrChanges;
                        advancement.MmrMessage = mmrMessage;
                        transaction.Commit();
                        return advancement;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public StageAdvanceResult ClearMatchResultsAndDownstream(int matchId)
        {
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        int? stageMatchId = GetParentStageMatchId(conn, transaction, matchId);
                        InvalidateFutureSwissRounds(conn, transaction, matchId);
                        UndoMmrForMatch(conn, transaction, matchId);

                        using (SqlCommand cmd = new SqlCommand(@"
UPDATE Seat
SET ResultMatchPlace = NULL,
    ResultPoints = NULL
WHERE MatchId = @MatchId;", conn, transaction))
                        {
                            cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                            cmd.ExecuteNonQuery();
                        }

                        StageAdvanceResult result = new StageAdvanceResult
                        {
                            StageMatchId = stageMatchId,
                            StageComplete = false,
                            PlacementResolved = false,
                            Message = "Result cleared."
                        };

                        if (stageMatchId.HasValue)
                        {
                            result.StageName = GetMatchName(conn, transaction, stageMatchId.Value);
                            ClearStageOutcomeAndDestinations(
                                conn,
                                transaction,
                                stageMatchId.Value,
                                false,
                                new HashSet<int>());
                            result.Message = "Result cleared. The stage is no longer complete, so its advanced players were removed from later seats.";
                        }

                        transaction.Commit();
                        return result;
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private static void SaveMatchResults(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId,
            IList<MatchResultUpdate> results)
        {
            const string sql = @"
UPDATE Seat
SET ResultMatchPlace = @ResultMatchPlace,
    ResultPoints = @ResultPoints
WHERE Id = @SeatId
  AND MatchId = @MatchId;";

            foreach (MatchResultUpdate result in results)
            {
                using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
                {
                    cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                    cmd.Parameters.Add("@SeatId", SqlDbType.Int).Value = result.SeatId;
                    cmd.Parameters.Add("@ResultMatchPlace", SqlDbType.Int).Value = result.ResultMatchPlace;

                    SqlParameter points = cmd.Parameters.Add("@ResultPoints", SqlDbType.Int);
                    points.Value = result.ResultPoints.HasValue
                        ? (object)result.ResultPoints.Value
                        : DBNull.Value;

                    int changed = cmd.ExecuteNonQuery();
                    if (changed != 1)
                    {
                        throw new InvalidOperationException("A seat does not belong to the selected match.");
                    }
                }
            }
        }

        private static List<MmrChangeDetails> ApplyMmrForMatch(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId,
            out string mmrMessage)
        {
            const int kFactor = 32;
            const double ratingScale = 400.0;

            List<(int PlayerId, int? UserId, string PlayerName, int Place, int? Mmr)> players =
                new List<(int PlayerId, int? UserId, string PlayerName, int Place, int? Mmr)>();

            // Prefer Player.RegisteredUserID, but also fall back to the active
            // TournamentRegistration. This makes MMR work for older tournaments
            // where the registration exists but the Player row was not linked correctly.
            using (SqlCommand cmd = new SqlCommand(@"
SELECT
    P.Id AS PlayerId,
    COALESCE(P.RegisteredUserID, REG.RegisteredUserId) AS RegisteredUserID,
    P.Name AS PlayerName,
    S.ResultMatchPlace,
    RU.Mmr
FROM Seat S
INNER JOIN Player P ON P.Id = S.PlayerId
OUTER APPLY
(
    SELECT TOP 1 TR.RegisteredUserId
    FROM TournamentRegistration TR
    WHERE TR.PlayerId = P.Id
      AND TR.Status = 'registered'
    ORDER BY TR.UpdatedAt DESC, TR.RegisteredAt DESC
) REG
LEFT JOIN RegisteredUsers RU
    ON RU.RegisteredUserID = COALESCE(P.RegisteredUserID, REG.RegisteredUserId)
WHERE S.MatchId = @MatchId
  AND S.PlayerId IS NOT NULL
  AND S.ResultMatchPlace IS NOT NULL
ORDER BY S.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        players.Add((
                            Convert.ToInt32(reader["PlayerId"]),
                            reader["RegisteredUserID"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["RegisteredUserID"]),
                            Convert.ToString(reader["PlayerName"]),
                            Convert.ToInt32(reader["ResultMatchPlace"]),
                            reader["Mmr"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["Mmr"])));
                    }
                }
            }

            if (players.Count != 2)
            {
                mmrMessage = "MMR was not changed because MMR is only calculated for 1v1 matches.";
                return new List<MmrChangeDetails>();
            }

            if (players.Any(x => !x.UserId.HasValue))
            {
                mmrMessage = "MMR was not changed because both players must be registered users.";
                return new List<MmrChangeDetails>();
            }

            if (players.Select(x => x.UserId.Value).Distinct().Count() != 2)
            {
                mmrMessage = "MMR was not changed because a player cannot play an MMR match against the same user account.";
                return new List<MmrChangeDetails>();
            }

            if (players.Any(x => !x.Mmr.HasValue))
            {
                mmrMessage = "MMR was not changed because one or both registered users do not have an MMR value.";
                return new List<MmrChangeDetails>();
            }

            var winner = players.SingleOrDefault(x => x.Place == 1);
            if (!winner.UserId.HasValue)
            {
                mmrMessage = "MMR was not changed because the match does not have exactly one winner.";
                return new List<MmrChangeDetails>();
            }

            var loser = players.Single(x => x.UserId.Value != winner.UserId.Value);

            // Elo-style MMR. A win over a stronger opponent is worth more; a win
            // over a weaker opponent is worth less. K=32 gives:
            // 1000 vs 1000  -> about +/-16
            // 1000 beats 1200 -> about +24/-24
            // 1200 beats 1000 -> about +8/-8
            double expectedWinner = 1.0 /
                (1.0 + Math.Pow(10.0, (loser.Mmr.Value - winner.Mmr.Value) / ratingScale));

            int ratingChange = (int)Math.Round(
                kFactor * (1.0 - expectedWinner),
                MidpointRounding.AwayFromZero);

            // A completed win/loss should always move the rating at least one point.
            ratingChange = Math.Max(1, ratingChange);

            List<MmrChangeDetails> changes = new List<MmrChangeDetails>();
            changes.Add(ApplyMmrDelta(
                conn, transaction, matchId, winner.PlayerId, winner.UserId.Value,
                winner.PlayerName, ratingChange));
            changes.Add(ApplyMmrDelta(
                conn, transaction, matchId, loser.PlayerId, loser.UserId.Value,
                loser.PlayerName, -ratingChange));

            mmrMessage = "MMR updated using Elo (K=32). The rating change is based on the two players' MMR before the match.";
            return changes;
        }

        private static MmrChangeDetails ApplyMmrDelta(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId,
            int playerId,
            int userId,
            string playerName,
            int requestedDelta)
        {
            int before;
            using (SqlCommand cmd = new SqlCommand(@"
SELECT Mmr
FROM RegisteredUsers WITH (UPDLOCK, HOLDLOCK)
WHERE RegisteredUserID = @UserId;", conn, transaction))
            {
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                object value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                {
                    throw new InvalidOperationException("A registered player could not be found while updating MMR.");
                }
                before = Convert.ToInt32(value);
            }

            int after = before + requestedDelta;
            int actualDelta = after - before;

            using (SqlCommand cmd = new SqlCommand(@"
UPDATE RegisteredUsers
SET Mmr = @After
WHERE RegisteredUserID = @UserId;

INSERT INTO MatchMmrChange
(
    MatchId,
    RegisteredUserId,
    PlayerId,
    Delta,
    MmrBefore,
    MmrAfter,
    CreatedAt
)
VALUES
(
    @MatchId,
    @UserId,
    @PlayerId,
    @Delta,
    @Before,
    @After,
    SYSDATETIME()
);", conn, transaction))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                cmd.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                cmd.Parameters.Add("@Delta", SqlDbType.Int).Value = actualDelta;
                cmd.Parameters.Add("@Before", SqlDbType.Int).Value = before;
                cmd.Parameters.Add("@After", SqlDbType.Int).Value = after;
                cmd.ExecuteNonQuery();
            }

            return new MmrChangeDetails
            {
                RegisteredUserId = userId,
                PlayerId = playerId,
                PlayerName = playerName,
                Before = before,
                Delta = actualDelta,
                After = after
            };
        }

        private static void UndoMmrForMatch(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId)
        {
            List<(int UserId, int Delta)> changes = new List<(int UserId, int Delta)>();

            using (SqlCommand cmd = new SqlCommand(@"
SELECT RegisteredUserId, Delta
FROM MatchMmrChange WITH (UPDLOCK, HOLDLOCK)
WHERE MatchId = @MatchId;", conn, transaction))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        changes.Add((
                            Convert.ToInt32(reader["RegisteredUserId"]),
                            Convert.ToInt32(reader["Delta"])));
                    }
                }
            }

            foreach (var change in changes)
            {
                using (SqlCommand cmd = new SqlCommand(@"
UPDATE RegisteredUsers
SET Mmr = Mmr - @Delta
WHERE RegisteredUserID = @UserId;", conn, transaction))
                {
                    cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = change.UserId;
                    cmd.Parameters.Add("@Delta", SqlDbType.Int).Value = change.Delta;
                    cmd.ExecuteNonQuery();
                }
            }

            if (changes.Count > 0)
            {
                using (SqlCommand cmd = new SqlCommand(@"
DELETE FROM MatchMmrChange
WHERE MatchId = @MatchId;", conn, transaction))
                {
                    cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void UndoMmrForStageChildMatches(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            List<int> matchIds = new List<int>();
            using (SqlCommand cmd = new SqlCommand(@"
SELECT Id
FROM [Match]
WHERE ParentMatchId = @StageMatchId
  AND IndividualMatch = 1;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        matchIds.Add(Convert.ToInt32(reader["Id"]));
                    }
                }
            }

            foreach (int childMatchId in matchIds)
            {
                UndoMmrForMatch(conn, transaction, childMatchId);
            }
        }

        private StageAdvanceResult RecalculateParentStage(
            SqlConnection conn,
            SqlTransaction transaction,
            int individualMatchId)
        {
            int? stageMatchId = GetParentStageMatchId(conn, transaction, individualMatchId);
            if (!stageMatchId.HasValue)
            {
                return new StageAdvanceResult
                {
                    StageComplete = false,
                    PlacementResolved = false,
                    Message = "Result saved. This match has no parent stage to advance from."
                };
            }

            int stageId = stageMatchId.Value;
            string stageName = GetMatchName(conn, transaction, stageId);
            List<StageSeatScore> stageSeats = GetStageSeats(conn, transaction, stageId);
            List<ChildSeatResult> childResults = GetChildResults(conn, transaction, stageId);
            List<int> childMatchIds = GetChildMatchIds(conn, transaction, stageId);

            StageAdvanceResult response = new StageAdvanceResult
            {
                StageMatchId = stageId,
                StageName = stageName,
                StageComplete = false,
                PlacementResolved = false
            };

            if (IsSwissStage(conn, transaction, stageId))
            {
                return RecalculateSwissStage(conn, transaction, stageId, stageName);
            }

            if (stageSeats.Count < 2 || childMatchIds.Count == 0)
            {
                ClearStageOutcomeAndDestinations(conn, transaction, stageId, false, new HashSet<int>());
                response.Message = "Result saved. The stage is still waiting for players or matches.";
                return response;
            }

            foreach (int childMatchId in childMatchIds)
            {
                List<ChildSeatResult> seats = childResults
                    .Where(x => x.MatchId == childMatchId && x.PlayerId > 0)
                    .ToList();

                if (seats.Count < 2 || seats.Any(x => !x.ResultMatchPlace.HasValue))
                {
                    ClearStageOutcomeAndDestinations(conn, transaction, stageId, false, new HashSet<int>());
                    response.Message = "Result saved. The stage is not complete yet; no players are advanced until every match in the stage has a result.";
                    return response;
                }
            }

            response.StageComplete = true;

            int? playTo = GetPlayTo(conn, transaction, stageId);

            List<StageSeatScore> ranked;
            string unresolvedReason;
            if (!TryRankStage(stageSeats, childResults, childMatchIds, playTo, out ranked, out unresolvedReason))
            {
                ClearStageOutcomeAndDestinations(conn, transaction, stageId, false, new HashSet<int>());
                response.Message = "All matches are complete, but the stage placement is tied. " + unresolvedReason;
                return response;
            }

            response.PlacementResolved = true;
            SaveStagePlacements(conn, transaction, ranked);
            response.AdvancedPlayers = MovePlayersToDestinationSeats(conn, transaction, stageId, ranked);

            if (response.AdvancedPlayers.Count == 0)
            {
                response.Message = "Stage complete. Placements were saved; no configured seats receive players from this stage.";
            }
            else
            {
                response.Message = "Stage complete. Placements were saved and " +
                    response.AdvancedPlayers.Count + " player" +
                    (response.AdvancedPlayers.Count == 1 ? " was" : "s were") +
                    " moved to the configured next-stage seat" +
                    (response.AdvancedPlayers.Count == 1 ? "." : "s.");
            }

            return response;
        }


        private static bool IsSwissStage(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT TOP 1 1
FROM [Match] M
INNER JOIN MatchRules MR ON MR.Id = M.MatchRulesId
WHERE M.ParentMatchId = @StageMatchId
  AND M.IndividualMatch = 1
  AND MR.PlayStyleId = 2;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                object value = cmd.ExecuteScalar();
                return value != null && value != DBNull.Value;
            }
        }

        private static int GetSwissTotalRounds(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId,
            int playerCount)
        {
            string description = null;

            using (SqlCommand cmd = new SqlCommand(@"
SELECT TOP 1 MR.Description
FROM [Match] M
INNER JOIN MatchRules MR ON MR.Id = M.MatchRulesId
WHERE M.ParentMatchId = @StageMatchId
  AND M.IndividualMatch = 1
  AND MR.PlayStyleId = 2
ORDER BY M.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                object value = cmd.ExecuteScalar();
                if (value != null && value != DBNull.Value)
                {
                    description = Convert.ToString(value);
                }
            }

            if (!String.IsNullOrWhiteSpace(description))
            {
                System.Text.RegularExpressions.Match parsed = Regex.Match(
                    description,
                    @"Swiss\s+system:\s*(\d+)\s*rounds?",
                    RegexOptions.IgnoreCase);

                if (parsed.Success)
                {
                    int configured;
                    if (Int32.TryParse(parsed.Groups[1].Value, out configured) && configured > 0)
                    {
                        return configured;
                    }
                }
            }

            return Math.Max(
                1,
                (int)Math.Ceiling(Math.Log(Math.Max(2, playerCount), 2)));
        }

        private static int ParseSwissRound(string matchName)
        {
            if (String.IsNullOrWhiteSpace(matchName))
            {
                return 0;
            }

            System.Text.RegularExpressions.Match parsed = Regex.Match(
                matchName,
                @"Swiss\s+round\s+(\d+)",
                RegexOptions.IgnoreCase);

            int round;
            return parsed.Success && Int32.TryParse(parsed.Groups[1].Value, out round)
                ? round
                : 0;
        }

        private static List<SwissMatchInfo> GetSwissMatches(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            List<SwissMatchInfo> result = new List<SwissMatchInfo>();

            using (SqlCommand cmd = new SqlCommand(@"
SELECT M.Id, M.Name, M.MatchRulesId
FROM [Match] M
INNER JOIN MatchRules MR ON MR.Id = M.MatchRulesId
WHERE M.ParentMatchId = @StageMatchId
  AND M.IndividualMatch = 1
  AND MR.PlayStyleId = 2
ORDER BY M.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader["Name"] == DBNull.Value
                            ? null
                            : Convert.ToString(reader["Name"]);

                        int round = ParseSwissRound(name);
                        if (round <= 0)
                        {
                            continue;
                        }

                        result.Add(new SwissMatchInfo
                        {
                            MatchId = Convert.ToInt32(reader["Id"]),
                            MatchRulesId = Convert.ToInt32(reader["MatchRulesId"]),
                            MatchName = name,
                            Round = round
                        });
                    }
                }
            }

            return result;
        }

        private static int GetSwissRoundForMatch(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT Name
FROM [Match]
WHERE Id = @MatchId;", conn, transaction))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                object value = cmd.ExecuteScalar();
                return ParseSwissRound(value == null || value == DBNull.Value
                    ? null
                    : Convert.ToString(value));
            }
        }

        private static string GetSwissRoundStageName(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT COALESCE(P.Name, S.Name)
FROM [Match] S
LEFT JOIN [Match] P ON P.Id = S.ParentMatchId
WHERE S.Id = @StageMatchId;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? "Swiss"
                    : Convert.ToString(value);
            }
        }

        private static List<StageSeatScore> BuildSwissStandings(
            List<StageSeatScore> stageSeats,
            List<ChildSeatResult> childResults,
            List<SwissMatchInfo> swissMatches,
            int? playTo)
        {
            Dictionary<int, StageSeatScore> bySeat =
                stageSeats.ToDictionary(x => x.SeatId, x => x);

            Dictionary<int, List<int>> opponents =
                stageSeats.ToDictionary(x => x.SeatId, x => new List<int>());

            Dictionary<int, int> scoreDifferences =
                stageSeats.ToDictionary(x => x.SeatId, x => 0);

            Dictionary<int, bool> scoreAvailable =
                stageSeats.ToDictionary(x => x.SeatId, x => true);

            foreach (StageSeatScore seat in stageSeats)
            {
                seat.Wins = 0;
                seat.Buchholz = 0;
                seat.HeadToHeadWins = 0;
                seat.HadBye = false;
                seat.ScoreDifference = 0;
            }

            foreach (SwissMatchInfo match in swissMatches)
            {
                List<ChildSeatResult> seats = childResults
                    .Where(x => x.MatchId == match.MatchId && x.PlayerId > 0)
                    .ToList();

                if (seats.Count == 1)
                {
                    ChildSeatResult bye = seats[0];
                    StageSeatScore byeStanding;
                    if (bySeat.TryGetValue(bye.StageSeatId, out byeStanding))
                    {
                        if (bye.ResultMatchPlace == 1)
                        {
                            byeStanding.Wins++;
                        }
                        byeStanding.HadBye = true;
                    }
                    continue;
                }

                foreach (ChildSeatResult own in seats)
                {
                    StageSeatScore standing;
                    if (!bySeat.TryGetValue(own.StageSeatId, out standing))
                    {
                        continue;
                    }

                    if (own.ResultMatchPlace == 1)
                    {
                        standing.Wins++;
                    }

                    List<ChildSeatResult> matchOpponents = seats
                        .Where(x => x.StageSeatId != own.StageSeatId)
                        .ToList();

                    foreach (ChildSeatResult opponent in matchOpponents)
                    {
                        opponents[own.StageSeatId].Add(opponent.StageSeatId);
                    }

                    if (playTo.HasValue)
                    {
                        if (!own.ResultPoints.HasValue ||
                            matchOpponents.Any(x => !x.ResultPoints.HasValue))
                        {
                            scoreAvailable[own.StageSeatId] = false;
                        }
                        else
                        {
                            int ownDistance =
                                Math.Abs(own.ResultPoints.Value - playTo.Value);

                            foreach (ChildSeatResult opponent in matchOpponents)
                            {
                                int opponentDistance =
                                    Math.Abs(opponent.ResultPoints.Value - playTo.Value);

                                scoreDifferences[own.StageSeatId] +=
                                    opponentDistance - ownDistance;
                            }
                        }
                    }
                    else
                    {
                        scoreAvailable[own.StageSeatId] = false;
                    }
                }
            }

            foreach (StageSeatScore seat in stageSeats)
            {
                seat.ScoreDifference = scoreAvailable[seat.SeatId]
                    ? (int?)scoreDifferences[seat.SeatId]
                    : null;

                int buchholz = 0;
                foreach (int opponentSeatId in opponents[seat.SeatId])
                {
                    StageSeatScore opponent;
                    if (bySeat.TryGetValue(opponentSeatId, out opponent))
                    {
                        buchholz += opponent.Wins;
                    }
                }
                seat.Buchholz = buchholz;
            }

            return stageSeats;
        }

        private static bool TryRankSwissStage(
            List<StageSeatScore> standings,
            List<ChildSeatResult> childResults,
            List<SwissMatchInfo> swissMatches,
            out List<StageSeatScore> ranked,
            out string unresolvedReason)
        {
            ranked = new List<StageSeatScore>();
            unresolvedReason = null;

            bool scoreDifferenceAvailable =
                standings.All(x => x.ScoreDifference.HasValue);

            List<StageSeatScore> initial = scoreDifferenceAvailable
                ? standings
                    .OrderByDescending(x => x.Wins)
                    .ThenByDescending(x => x.Buchholz)
                    .ThenByDescending(x => x.ScoreDifference.Value)
                    .ToList()
                : standings
                    .OrderByDescending(x => x.Wins)
                    .ThenByDescending(x => x.Buchholz)
                    .ToList();

            int index = 0;
            while (index < initial.Count)
            {
                StageSeatScore first = initial[index];

                List<StageSeatScore> tied = initial
                    .Skip(index)
                    .TakeWhile(x =>
                        x.Wins == first.Wins &&
                        x.Buchholz == first.Buchholz &&
                        (!scoreDifferenceAvailable ||
                         x.ScoreDifference == first.ScoreDifference))
                    .ToList();

                if (tied.Count == 1)
                {
                    ranked.Add(tied[0]);
                    index++;
                    continue;
                }

                HashSet<int> tiedSeatIds =
                    new HashSet<int>(tied.Select(x => x.SeatId));

                foreach (StageSeatScore player in tied)
                {
                    player.HeadToHeadWins = 0;

                    foreach (SwissMatchInfo match in swissMatches)
                    {
                        List<ChildSeatResult> seats = childResults
                            .Where(x =>
                                x.MatchId == match.MatchId &&
                                x.PlayerId > 0)
                            .ToList();

                        if (seats.Count != 2)
                        {
                            continue;
                        }

                        bool containsPlayer =
                            seats.Any(x => x.StageSeatId == player.SeatId);

                        bool containsOtherTied =
                            seats.Any(x =>
                                x.StageSeatId != player.SeatId &&
                                tiedSeatIds.Contains(x.StageSeatId));

                        if (containsPlayer &&
                            containsOtherTied &&
                            seats.Any(x =>
                                x.StageSeatId == player.SeatId &&
                                x.ResultMatchPlace == 1))
                        {
                            player.HeadToHeadWins++;
                        }
                    }
                }

                List<StageSeatScore> tieOrdered = tied
                    .OrderByDescending(x => x.HeadToHeadWins)
                    .ToList();

                bool stillTied = tieOrdered
                    .GroupBy(x => x.HeadToHeadWins)
                    .Any(g => g.Count() > 1);

                if (stillTied)
                {
                    unresolvedReason = scoreDifferenceAvailable
                        ? "Players are still tied on wins, Buchholz, score difference and head-to-head. Play an extra deciding match before advancing them."
                        : "Players are still tied on wins, Buchholz and head-to-head. Enter scores for every match or play an extra deciding match.";
                    return false;
                }

                ranked.AddRange(tieOrdered);
                index += tied.Count;
            }

            return true;
        }

        private static string SwissPairKey(int seatA, int seatB)
        {
            int low = Math.Min(seatA, seatB);
            int high = Math.Max(seatA, seatB);
            return low.ToString() + ":" + high.ToString();
        }

        private static HashSet<string> GetPreviousSwissPairs(
            List<ChildSeatResult> childResults,
            List<SwissMatchInfo> swissMatches)
        {
            HashSet<string> pairs = new HashSet<string>();

            foreach (SwissMatchInfo match in swissMatches)
            {
                List<ChildSeatResult> seats = childResults
                    .Where(x => x.MatchId == match.MatchId && x.PlayerId > 0)
                    .ToList();

                if (seats.Count == 2)
                {
                    pairs.Add(SwissPairKey(
                        seats[0].StageSeatId,
                        seats[1].StageSeatId));
                }
            }

            return pairs;
        }

        private static bool TryPairSwissPlayersRecursive(
            List<StageSeatScore> remaining,
            HashSet<string> previousPairs,
            bool allowRematches,
            List<SwissPair> result)
        {
            if (remaining.Count == 0)
            {
                return true;
            }

            StageSeatScore first = remaining[0];

            List<StageSeatScore> candidates = remaining
                .Skip(1)
                .OrderBy(x => Math.Abs(x.Wins - first.Wins))
                .ThenBy(x => Math.Abs(x.Buchholz - first.Buchholz))
                .ThenByDescending(x => x.ScoreDifference ?? Int32.MinValue)
                .ThenBy(x => x.SeatId)
                .ToList();

            foreach (StageSeatScore candidate in candidates)
            {
                bool isRematch = previousPairs.Contains(
                    SwissPairKey(first.SeatId, candidate.SeatId));

                if (isRematch && !allowRematches)
                {
                    continue;
                }

                List<StageSeatScore> next = remaining
                    .Where(x =>
                        x.SeatId != first.SeatId &&
                        x.SeatId != candidate.SeatId)
                    .ToList();

                result.Add(new SwissPair
                {
                    First = first,
                    Second = candidate
                });

                if (TryPairSwissPlayersRecursive(
                    next,
                    previousPairs,
                    allowRematches,
                    result))
                {
                    return true;
                }

                result.RemoveAt(result.Count - 1);
            }

            return false;
        }

        private static List<SwissPair> BuildSwissPairings(
            List<StageSeatScore> standings,
            HashSet<string> previousPairs,
            out StageSeatScore? byePlayer)
        {
            List<StageSeatScore> ordered = standings
                .OrderByDescending(x => x.Wins)
                .ThenByDescending(x => x.Buchholz)
                .ThenByDescending(x => x.ScoreDifference ?? Int32.MinValue)
                .ThenBy(x => x.SeatId)
                .ToList();

            byePlayer = null;

            if (ordered.Count % 2 != 0)
            {
                StageSeatScore? selectedBye = ordered
                    .AsEnumerable()
                    .Reverse()
                    .FirstOrDefault(x => !x.HadBye);

                if (selectedBye == null)
                {
                    selectedBye = ordered.Last();
                }

                byePlayer = selectedBye;
                int byeSeatId = selectedBye.SeatId;
                ordered.RemoveAll(x => x.SeatId == byeSeatId);
            }

            List<SwissPair> pairings = new List<SwissPair>();

            if (!TryPairSwissPlayersRecursive(
                ordered,
                previousPairs,
                false,
                pairings))
            {
                pairings.Clear();

                if (!TryPairSwissPlayersRecursive(
                    ordered,
                    previousPairs,
                    true,
                    pairings))
                {
                    throw new InvalidOperationException(
                        "Swiss pairings could not be generated.");
                }
            }

            return pairings;
        }

        private static int InsertSwissMatch(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId,
            int matchRulesId,
            string matchName)
        {
            using (SqlCommand cmd = new SqlCommand(@"
INSERT INTO [Match]
(
    ParentMatchId,
    MatchRulesId,
    IndividualMatch,
    [Name]
)
VALUES
(
    @ParentMatchId,
    @MatchRulesId,
    1,
    @Name
);

SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, transaction))
            {
                cmd.Parameters.Add("@ParentMatchId", SqlDbType.Int).Value =
                    stageMatchId;
                cmd.Parameters.Add("@MatchRulesId", SqlDbType.Int).Value =
                    matchRulesId;
                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 255).Value =
                    matchName;

                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static int InsertSwissSeat(
            SqlConnection conn,
            SqlTransaction transaction,
            int parentSeatId,
            int playerId,
            int matchId,
            int? resultPlace,
            int? resultPoints)
        {
            using (SqlCommand cmd = new SqlCommand(@"
INSERT INTO Seat
(
    ParentSeatId,
    PlayerId,
    MatchId,
    ResultMatchPlace,
    ResultPoints
)
VALUES
(
    @ParentSeatId,
    @PlayerId,
    @MatchId,
    @ResultMatchPlace,
    @ResultPoints
);

SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, transaction))
            {
                cmd.Parameters.Add("@ParentSeatId", SqlDbType.Int).Value =
                    parentSeatId;
                cmd.Parameters.Add("@PlayerId", SqlDbType.Int).Value =
                    playerId;
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value =
                    matchId;

                SqlParameter place = cmd.Parameters.Add(
                    "@ResultMatchPlace",
                    SqlDbType.Int);
                place.Value = resultPlace.HasValue
                    ? (object)resultPlace.Value
                    : DBNull.Value;

                SqlParameter points = cmd.Parameters.Add(
                    "@ResultPoints",
                    SqlDbType.Int);
                points.Value = resultPoints.HasValue
                    ? (object)resultPoints.Value
                    : DBNull.Value;

                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static int CreateNextSwissRound(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId,
            int nextRound,
            List<StageSeatScore> standings,
            List<ChildSeatResult> childResults,
            List<SwissMatchInfo> swissMatches,
            int? playTo)
        {
            if (swissMatches.Any(x => x.Round == nextRound))
            {
                return 0;
            }

            int matchRulesId = swissMatches
                .Select(x => x.MatchRulesId)
                .FirstOrDefault();

            if (matchRulesId <= 0)
            {
                throw new InvalidOperationException(
                    "The Swiss stage does not have match rules.");
            }

            HashSet<string> previousPairs =
                GetPreviousSwissPairs(childResults, swissMatches);

            StageSeatScore? byePlayer;
            List<SwissPair> pairings = BuildSwissPairings(
                standings,
                previousPairs,
                out byePlayer);

            string roundStageName =
                GetSwissRoundStageName(conn, transaction, stageMatchId);

            string poolName =
                GetMatchName(conn, transaction, stageMatchId);

            int matchNumber = 1;

            foreach (SwissPair pairing in pairings)
            {
                string name =
                    "runde: " + roundStageName +
                    " " + poolName +
                    " Swiss round " + nextRound +
                    " Match " + matchNumber;

                int matchId = InsertSwissMatch(
                    conn,
                    transaction,
                    stageMatchId,
                    matchRulesId,
                    name);

                InsertSwissSeat(
                    conn,
                    transaction,
                    pairing.First.SeatId,
                    pairing.First.PlayerId,
                    matchId,
                    null,
                    null);

                InsertSwissSeat(
                    conn,
                    transaction,
                    pairing.Second.SeatId,
                    pairing.Second.PlayerId,
                    matchId,
                    null,
                    null);

                matchNumber++;
            }

            if (byePlayer != null)
            {
                string byeName =
                    "runde: " + roundStageName +
                    " " + poolName +
                    " Swiss round " + nextRound +
                    " BYE";

                int byeMatchId = InsertSwissMatch(
                    conn,
                    transaction,
                    stageMatchId,
                    matchRulesId,
                    byeName);

                InsertSwissSeat(
                    conn,
                    transaction,
                    byePlayer.SeatId,
                    byePlayer.PlayerId,
                    byeMatchId,
                    1,
                    playTo ?? 0);
            }

            return pairings.Count + (byePlayer == null ? 0 : 1);
        }

        private static StageAdvanceResult RecalculateSwissStage(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId,
            string stageName)
        {
            List<StageSeatScore> stageSeats =
                GetStageSeats(conn, transaction, stageMatchId);

            List<SwissMatchInfo> swissMatches =
                GetSwissMatches(conn, transaction, stageMatchId);

            List<ChildSeatResult> childResults =
                GetChildResults(conn, transaction, stageMatchId);

            StageAdvanceResult response = new StageAdvanceResult
            {
                StageMatchId = stageMatchId,
                StageName = stageName,
                StageComplete = false,
                PlacementResolved = false
            };

            if (stageSeats.Count < 2 || swissMatches.Count == 0)
            {
                ClearStageOutcomeAndDestinations(
                    conn,
                    transaction,
                    stageMatchId,
                    false,
                    new HashSet<int>());

                response.Message =
                    "Result saved. The Swiss stage is still waiting for players or pairings.";
                return response;
            }

            int currentRound = swissMatches.Max(x => x.Round);
            int totalRounds = GetSwissTotalRounds(
                conn,
                transaction,
                stageMatchId,
                stageSeats.Count);

            List<SwissMatchInfo> currentMatches = swissMatches
                .Where(x => x.Round == currentRound)
                .ToList();

            foreach (SwissMatchInfo match in currentMatches)
            {
                List<ChildSeatResult> seats = childResults
                    .Where(x =>
                        x.MatchId == match.MatchId &&
                        x.PlayerId > 0)
                    .ToList();

                bool complete =
                    seats.Count == 1
                        ? seats[0].ResultMatchPlace == 1
                        : seats.Count >= 2 &&
                          seats.All(x => x.ResultMatchPlace.HasValue);

                if (!complete)
                {
                    ClearStageOutcomeAndDestinations(
                        conn,
                        transaction,
                        stageMatchId,
                        false,
                        new HashSet<int>());

                    response.Message =
                        "Swiss round " + currentRound +
                        " is not complete yet. The next pairings will be created when every match in this round has a result.";
                    return response;
                }
            }

            int? playTo = GetPlayTo(
                conn,
                transaction,
                stageMatchId);

            List<StageSeatScore> standings = BuildSwissStandings(
                stageSeats,
                childResults,
                swissMatches,
                playTo);

            if (currentRound < totalRounds)
            {
                int nextRound = currentRound + 1;

                int created = CreateNextSwissRound(
                    conn,
                    transaction,
                    stageMatchId,
                    nextRound,
                    standings,
                    childResults,
                    swissMatches,
                    playTo);

                response.Message =
                    "Swiss round " + currentRound +
                    " complete. Swiss round " + nextRound +
                    " pairings were created" +
                    (created > 0
                        ? " (" + created + " match" +
                          (created == 1 ? "" : "es") + ")."
                        : ".");

                return response;
            }

            response.StageComplete = true;

            List<StageSeatScore> ranked;
            string unresolvedReason;

            if (!TryRankSwissStage(
                standings,
                childResults,
                swissMatches,
                out ranked,
                out unresolvedReason))
            {
                ClearStageOutcomeAndDestinations(
                    conn,
                    transaction,
                    stageMatchId,
                    false,
                    new HashSet<int>());

                response.Message =
                    "All Swiss rounds are complete, but the final placement is tied. " +
                    unresolvedReason;
                return response;
            }

            response.PlacementResolved = true;

            SaveStagePlacements(
                conn,
                transaction,
                ranked);

            response.AdvancedPlayers =
                MovePlayersToDestinationSeats(
                    conn,
                    transaction,
                    stageMatchId,
                    ranked);

            response.Message =
                "Swiss stage complete after " + totalRounds +
                " rounds. Final placements were saved" +
                (response.AdvancedPlayers.Count > 0
                    ? " and " + response.AdvancedPlayers.Count +
                      " player" +
                      (response.AdvancedPlayers.Count == 1 ? " was" : "s were") +
                      " moved to the configured next stage."
                    : ".");

            return response;
        }

        private static void DeleteSwissRoundsAfter(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId,
            int round)
        {
            List<SwissMatchInfo> laterMatches = GetSwissMatches(
                conn,
                transaction,
                stageMatchId)
                .Where(x => x.Round > round)
                .OrderByDescending(x => x.Round)
                .ThenByDescending(x => x.MatchId)
                .ToList();

            foreach (SwissMatchInfo match in laterMatches)
            {
                UndoMmrForMatch(
                    conn,
                    transaction,
                    match.MatchId);

                using (SqlCommand cmd = new SqlCommand(@"
DELETE FROM Seat
WHERE MatchId = @MatchId;

DELETE FROM [Match]
WHERE Id = @MatchId;", conn, transaction))
                {
                    cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value =
                        match.MatchId;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void RestoreSwissByeResults(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId,
            int round)
        {
            foreach (SwissMatchInfo match in GetSwissMatches(
                conn,
                transaction,
                stageMatchId).Where(x => x.Round == round))
            {
                int seatCount;

                using (SqlCommand cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM Seat
WHERE MatchId = @MatchId
  AND PlayerId IS NOT NULL;", conn, transaction))
                {
                    cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value =
                        match.MatchId;
                    seatCount = Convert.ToInt32(cmd.ExecuteScalar());
                }

                if (seatCount != 1)
                {
                    continue;
                }

                int? playTo = GetPlayTo(
                    conn,
                    transaction,
                    stageMatchId);

                using (SqlCommand cmd = new SqlCommand(@"
UPDATE Seat
SET ResultMatchPlace = 1,
    ResultPoints = @ResultPoints
WHERE MatchId = @MatchId
  AND PlayerId IS NOT NULL;", conn, transaction))
                {
                    cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value =
                        match.MatchId;
                    cmd.Parameters.Add("@ResultPoints", SqlDbType.Int).Value =
                        playTo ?? 0;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void InvalidateFutureSwissRounds(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId)
        {
            int? stageMatchId =
                GetParentStageMatchId(conn, transaction, matchId);

            if (!stageMatchId.HasValue ||
                !IsSwissStage(conn, transaction, stageMatchId.Value))
            {
                return;
            }

            int round = GetSwissRoundForMatch(
                conn,
                transaction,
                matchId);

            if (round <= 0)
            {
                return;
            }

            bool hasLaterRounds = GetSwissMatches(
                conn,
                transaction,
                stageMatchId.Value)
                .Any(x => x.Round > round);

            if (!hasLaterRounds)
            {
                return;
            }

            ClearStageOutcomeAndDestinations(
                conn,
                transaction,
                stageMatchId.Value,
                false,
                new HashSet<int>());

            DeleteSwissRoundsAfter(
                conn,
                transaction,
                stageMatchId.Value,
                round);
        }

        private static bool TryRankStage(
            List<StageSeatScore> stageSeats,
            List<ChildSeatResult> childResults,
            List<int> childMatchIds,
            int? playTo,
            out List<StageSeatScore> ranked,
            out string unresolvedReason)
        {
            ranked = new List<StageSeatScore>();
            unresolvedReason = null;

            // If the whole stage is one multi-player match, the entered place is the stage place.
            if (childMatchIds.Count == 1)
            {
                int onlyMatchId = childMatchIds[0];
                List<ChildSeatResult> directResults = childResults
                    .Where(x => x.MatchId == onlyMatchId && x.PlayerId > 0)
                    .ToList();

                if (directResults.Count == stageSeats.Count && directResults.All(x => x.ResultMatchPlace.HasValue))
                {
                    foreach (StageSeatScore stageSeat in stageSeats)
                    {
                        ChildSeatResult direct = directResults.FirstOrDefault(x => x.StageSeatId == stageSeat.SeatId);
                        if (direct == null)
                        {
                            unresolvedReason = "A stage seat could not be matched to the playable match.";
                            return false;
                        }

                        stageSeat.DirectPlace = direct.ResultMatchPlace;
                        stageSeat.DirectPoints = direct.ResultPoints;
                    }

                    if (stageSeats.Select(x => x.DirectPlace.Value).Distinct().Count() != stageSeats.Count)
                    {
                        unresolvedReason = "Each player must have a unique place.";
                        return false;
                    }

                    ranked = stageSeats.OrderBy(x => x.DirectPlace.Value).ToList();
                    return true;
                }
            }

            foreach (StageSeatScore stageSeat in stageSeats)
            {
                List<ChildSeatResult> playerResults = childResults
                    .Where(x => x.StageSeatId == stageSeat.SeatId && x.PlayerId == stageSeat.PlayerId)
                    .ToList();

                stageSeat.Wins = playerResults.Count(x => x.ResultMatchPlace == 1);

                // Score difference measures how far ahead/behind a player finished.
                // The winner always ends on PlayTo. Example with 70 -> 0:
                // 0-35 gives the winner +35 and the loser -35.
                // 0-22 gives +22/-22, so +35 is the better result.
                if (playTo.HasValue)
                {
                    int difference = 0;
                    bool completeScores = true;

                    foreach (ChildSeatResult ownResult in playerResults)
                    {
                        if (!ownResult.ResultPoints.HasValue)
                        {
                            completeScores = false;
                            break;
                        }

                        List<ChildSeatResult> opponents = childResults
                            .Where(x =>
                                x.MatchId == ownResult.MatchId &&
                                x.PlayerId > 0 &&
                                x.StageSeatId != stageSeat.SeatId)
                            .ToList();

                        if (opponents.Count == 0 || opponents.Any(x => !x.ResultPoints.HasValue))
                        {
                            completeScores = false;
                            break;
                        }

                        int ownDistanceToTarget = Math.Abs(ownResult.ResultPoints.Value - playTo.Value);
                        foreach (ChildSeatResult opponent in opponents)
                        {
                            int opponentDistanceToTarget = Math.Abs(opponent.ResultPoints.Value - playTo.Value);
                            difference += opponentDistanceToTarget - ownDistanceToTarget;
                        }
                    }

                    stageSeat.ScoreDifference = completeScores ? (int?)difference : null;
                }
                else
                {
                    stageSeat.ScoreDifference = null;
                }
            }

            bool scoreDifferenceAvailable = stageSeats.All(x => x.ScoreDifference.HasValue);

            List<StageSeatScore> initial = scoreDifferenceAvailable
                ? stageSeats
                    .OrderByDescending(x => x.Wins)
                    .ThenByDescending(x => x.ScoreDifference.Value)
                    .ToList()
                : stageSeats
                    .OrderByDescending(x => x.Wins)
                    .ToList();

            List<StageSeatScore> finalRanking = new List<StageSeatScore>();
            int index = 0;
            while (index < initial.Count)
            {
                StageSeatScore first = initial[index];
                List<StageSeatScore> tied = initial
                    .Skip(index)
                    .TakeWhile(x =>
                        x.Wins == first.Wins &&
                        (!scoreDifferenceAvailable || x.ScoreDifference == first.ScoreDifference))
                    .ToList();

                if (tied.Count == 1)
                {
                    finalRanking.Add(tied[0]);
                    index++;
                    continue;
                }

                HashSet<int> tiedSeatIds = new HashSet<int>(tied.Select(x => x.SeatId));
                foreach (StageSeatScore player in tied)
                {
                    HashSet<int> matchesAgainstTiedPlayers = new HashSet<int>();

                    foreach (int matchId in childMatchIds)
                    {
                        List<ChildSeatResult> matchSeats = childResults
                            .Where(x => x.MatchId == matchId && x.PlayerId > 0)
                            .ToList();

                        bool containsPlayer = matchSeats.Any(x => x.StageSeatId == player.SeatId);
                        bool containsAnotherTiedPlayer = matchSeats.Any(x =>
                            x.StageSeatId != player.SeatId && tiedSeatIds.Contains(x.StageSeatId));

                        if (containsPlayer && containsAnotherTiedPlayer)
                        {
                            matchesAgainstTiedPlayers.Add(matchId);
                        }
                    }

                    player.HeadToHeadWins = childResults.Count(x =>
                        x.StageSeatId == player.SeatId &&
                        x.ResultMatchPlace == 1 &&
                        matchesAgainstTiedPlayers.Contains(x.MatchId));
                }

                List<StageSeatScore> tieOrdered = tied
                    .OrderByDescending(x => x.HeadToHeadWins)
                    .ToList();

                bool stillTied = tieOrdered
                    .GroupBy(x => x.HeadToHeadWins)
                    .Any(g => g.Count() > 1);

                if (stillTied)
                {
                    unresolvedReason = scoreDifferenceAvailable
                        ? "The players are still tied on wins, score difference and head-to-head results. Play an extra deciding match before advancing them."
                        : "Scores are missing, so score difference cannot break the tie. Enter the final score for every match or play an extra deciding match.";
                    return false;
                }

                finalRanking.AddRange(tieOrdered);
                index += tied.Count;
            }

            ranked = finalRanking;
            return true;
        }

        private static void SaveStagePlacements(
            SqlConnection conn,
            SqlTransaction transaction,
            List<StageSeatScore> ranked)
        {
            const string sql = @"
UPDATE Seat
SET ResultMatchPlace = @Place,
    ResultPoints = @Points
WHERE Id = @SeatId;";

            for (int i = 0; i < ranked.Count; i++)
            {
                StageSeatScore player = ranked[i];
                int place = player.DirectPlace ?? (i + 1);
                int points = player.DirectPoints ?? player.ScoreDifference ?? 0;

                using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
                {
                    cmd.Parameters.Add("@SeatId", SqlDbType.Int).Value = player.SeatId;
                    cmd.Parameters.Add("@Place", SqlDbType.Int).Value = place;
                    cmd.Parameters.Add("@Points", SqlDbType.Int).Value = points;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static List<AdvancedPlayerDetails> MovePlayersToDestinationSeats(
            SqlConnection conn,
            SqlTransaction transaction,
            int sourceStageMatchId,
            List<StageSeatScore> ranked)
        {
            List<AdvancedPlayerDetails> advanced = new List<AdvancedPlayerDetails>();
            HashSet<int> invalidatedDestinationStages = new HashSet<int>();

            for (int i = 0; i < ranked.Count; i++)
            {
                StageSeatScore player = ranked[i];
                int place = player.DirectPlace ?? (i + 1);
                List<DestinationSeat> destinations = GetDestinationSeats(
                    conn,
                    transaction,
                    sourceStageMatchId,
                    place);

                foreach (DestinationSeat destination in destinations)
                {
                    if (!destination.PlayerId.HasValue || destination.PlayerId.Value != player.PlayerId)
                    {
                        if (!invalidatedDestinationStages.Contains(destination.MatchId))
                        {
                            ClearStageOutcomeAndDestinations(
                                conn,
                                transaction,
                                destination.MatchId,
                                true,
                                new HashSet<int>());
                            invalidatedDestinationStages.Add(destination.MatchId);
                        }

                        SetPlayerOnSeatAndChildren(
                            conn,
                            transaction,
                            destination.SeatId,
                            player.PlayerId);

                        if (IsSwissStage(conn, transaction, destination.MatchId))
                        {
                            RestoreSwissByeResults(
                                conn,
                                transaction,
                                destination.MatchId,
                                1);
                        }
                    }

                    advanced.Add(new AdvancedPlayerDetails
                    {
                        PlayerId = player.PlayerId,
                        PlayerName = player.PlayerName,
                        Place = place,
                        DestinationSeatId = destination.SeatId,
                        DestinationMatchId = destination.MatchId,
                        DestinationMatchName = destination.MatchName
                    });
                }
            }

            return advanced;
        }

        private static void ClearStageOutcomeAndDestinations(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId,
            bool clearChildMatchResults,
            HashSet<int> visitedStages)
        {
            if (!visitedStages.Add(stageMatchId))
            {
                return;
            }

            using (SqlCommand cmd = new SqlCommand(@"
UPDATE Seat
SET ResultMatchPlace = NULL,
    ResultPoints = NULL
WHERE MatchId = @StageMatchId;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                cmd.ExecuteNonQuery();
            }

            if (clearChildMatchResults)
            {
                UndoMmrForStageChildMatches(conn, transaction, stageMatchId);

                bool swissStage = IsSwissStage(conn, transaction, stageMatchId);
                if (swissStage)
                {
                    // Later Swiss rounds depend on earlier standings. If this stage is
                    // invalidated by an upstream change, keep only round 1 and let the
                    // following rounds be generated again from the new results.
                    DeleteSwissRoundsAfter(conn, transaction, stageMatchId, 1);
                }

                using (SqlCommand cmd = new SqlCommand(@"
UPDATE S
SET S.ResultMatchPlace = NULL,
    S.ResultPoints = NULL
FROM Seat S
INNER JOIN [Match] M ON M.Id = S.MatchId
WHERE M.ParentMatchId = @StageMatchId
  AND M.IndividualMatch = 1;", conn, transaction))
                {
                    cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                    cmd.ExecuteNonQuery();
                }

                if (swissStage)
                {
                    // A Swiss bye is an automatic win and must remain complete after
                    // results are reset.
                    RestoreSwissByeResults(conn, transaction, stageMatchId, 1);
                }
            }

            List<DestinationSeat> downstream = GetAllDestinationSeats(conn, transaction, stageMatchId);
            foreach (int nextStageId in downstream.Select(x => x.MatchId).Distinct())
            {
                ClearStageOutcomeAndDestinations(
                    conn,
                    transaction,
                    nextStageId,
                    true,
                    visitedStages);
            }

            foreach (DestinationSeat destination in downstream)
            {
                ClearPlayerFromSeatAndChildren(conn, transaction, destination.SeatId);
            }
        }

        private static void SetPlayerOnSeatAndChildren(
            SqlConnection conn,
            SqlTransaction transaction,
            int seatId,
            int playerId)
        {
            using (SqlCommand cmd = new SqlCommand(@"
UPDATE Seat
SET PlayerId = @PlayerId,
    ResultMatchPlace = NULL,
    ResultPoints = NULL
WHERE Id = @SeatId
   OR ParentSeatId = @SeatId;", conn, transaction))
            {
                cmd.Parameters.Add("@SeatId", SqlDbType.Int).Value = seatId;
                cmd.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                cmd.ExecuteNonQuery();
            }
        }

        private static void ClearPlayerFromSeatAndChildren(
            SqlConnection conn,
            SqlTransaction transaction,
            int seatId)
        {
            using (SqlCommand cmd = new SqlCommand(@"
UPDATE Seat
SET PlayerId = NULL,
    ResultMatchPlace = NULL,
    ResultPoints = NULL
WHERE Id = @SeatId
   OR ParentSeatId = @SeatId;", conn, transaction))
            {
                cmd.Parameters.Add("@SeatId", SqlDbType.Int).Value = seatId;
                cmd.ExecuteNonQuery();
            }
        }

        private static int? GetParentStageMatchId(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId)
        {
            using (SqlCommand cmd = new SqlCommand(@"
SELECT ParentMatchId
FROM [Match]
WHERE Id = @MatchId
  AND IndividualMatch = 1;", conn, transaction))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? (int?)null
                    : Convert.ToInt32(value);
            }
        }

        private static string GetMatchName(
            SqlConnection conn,
            SqlTransaction transaction,
            int matchId)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT Name FROM [Match] WHERE Id = @MatchId;", conn, transaction))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            }
        }

        private static List<StageSeatScore> GetStageSeats(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            List<StageSeatScore> result = new List<StageSeatScore>();
            using (SqlCommand cmd = new SqlCommand(@"
SELECT S.Id, S.PlayerId, P.Name AS PlayerName
FROM Seat S
LEFT JOIN Player P ON P.Id = S.PlayerId
WHERE S.MatchId = @StageMatchId
  AND S.PlayerId IS NOT NULL
ORDER BY S.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new StageSeatScore
                        {
                            SeatId = Convert.ToInt32(reader["Id"]),
                            PlayerId = Convert.ToInt32(reader["PlayerId"]),
                            PlayerName = reader["PlayerName"] == DBNull.Value ? null : Convert.ToString(reader["PlayerName"])
                        });
                    }
                }
            }
            return result;
        }


        private static int? GetPlayTo(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            // Prefer the rules on a playable child match. If none are found,
            // fall back to rules attached to the stage itself.
            using (SqlCommand cmd = new SqlCommand(@"
SELECT TOP 1 MR.PlayTo
FROM [Match] M
INNER JOIN MatchRules MR ON MR.Id = M.MatchRulesId
WHERE (M.ParentMatchId = @StageMatchId AND M.IndividualMatch = 1)
   OR M.Id = @StageMatchId
ORDER BY CASE WHEN M.ParentMatchId = @StageMatchId THEN 0 ELSE 1 END, M.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                object value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                {
                    return null;
                }

                return Convert.ToInt32(value);
            }
        }

        private static List<int> GetChildMatchIds(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            List<int> result = new List<int>();
            using (SqlCommand cmd = new SqlCommand(@"
SELECT Id
FROM [Match]
WHERE ParentMatchId = @StageMatchId
  AND IndividualMatch = 1
ORDER BY Id;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(Convert.ToInt32(reader["Id"]));
                    }
                }
            }
            return result;
        }

        private static List<ChildSeatResult> GetChildResults(
            SqlConnection conn,
            SqlTransaction transaction,
            int stageMatchId)
        {
            List<ChildSeatResult> result = new List<ChildSeatResult>();
            using (SqlCommand cmd = new SqlCommand(@"
SELECT
    S.MatchId,
    S.ParentSeatId,
    S.PlayerId,
    S.ResultMatchPlace,
    S.ResultPoints
FROM Seat S
INNER JOIN [Match] M ON M.Id = S.MatchId
WHERE M.ParentMatchId = @StageMatchId
  AND M.IndividualMatch = 1
ORDER BY S.MatchId, S.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@StageMatchId", SqlDbType.Int).Value = stageMatchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader["ParentSeatId"] == DBNull.Value || reader["PlayerId"] == DBNull.Value)
                        {
                            continue;
                        }

                        result.Add(new ChildSeatResult
                        {
                            MatchId = Convert.ToInt32(reader["MatchId"]),
                            StageSeatId = Convert.ToInt32(reader["ParentSeatId"]),
                            PlayerId = Convert.ToInt32(reader["PlayerId"]),
                            ResultMatchPlace = ToNullableInt(reader["ResultMatchPlace"]),
                            ResultPoints = ToNullableInt(reader["ResultPoints"])
                        });
                    }
                }
            }
            return result;
        }

        private static List<DestinationSeat> GetDestinationSeats(
            SqlConnection conn,
            SqlTransaction transaction,
            int sourceStageMatchId,
            int place)
        {
            List<DestinationSeat> result = new List<DestinationSeat>();
            using (SqlCommand cmd = new SqlCommand(@"
SELECT S.Id, S.MatchId, S.PlayerId, M.Name AS MatchName
FROM Seat S
INNER JOIN [Match] M ON M.Id = S.MatchId
WHERE S.AutoSelectMatchId = @SourceStageMatchId
  AND S.AutoSelectlPlace = @Place
ORDER BY S.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@SourceStageMatchId", SqlDbType.Int).Value = sourceStageMatchId;
                cmd.Parameters.Add("@Place", SqlDbType.Int).Value = place;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new DestinationSeat
                        {
                            SeatId = Convert.ToInt32(reader["Id"]),
                            MatchId = Convert.ToInt32(reader["MatchId"]),
                            MatchName = reader["MatchName"] == DBNull.Value ? null : Convert.ToString(reader["MatchName"]),
                            PlayerId = ToNullableInt(reader["PlayerId"])
                        });
                    }
                }
            }
            return result;
        }

        private static List<DestinationSeat> GetAllDestinationSeats(
            SqlConnection conn,
            SqlTransaction transaction,
            int sourceStageMatchId)
        {
            List<DestinationSeat> result = new List<DestinationSeat>();
            using (SqlCommand cmd = new SqlCommand(@"
SELECT S.Id, S.MatchId, S.PlayerId, M.Name AS MatchName
FROM Seat S
INNER JOIN [Match] M ON M.Id = S.MatchId
WHERE S.AutoSelectMatchId = @SourceStageMatchId
ORDER BY S.Id;", conn, transaction))
            {
                cmd.Parameters.Add("@SourceStageMatchId", SqlDbType.Int).Value = sourceStageMatchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new DestinationSeat
                        {
                            SeatId = Convert.ToInt32(reader["Id"]),
                            MatchId = Convert.ToInt32(reader["MatchId"]),
                            MatchName = reader["MatchName"] == DBNull.Value ? null : Convert.ToString(reader["MatchName"]),
                            PlayerId = ToNullableInt(reader["PlayerId"])
                        });
                    }
                }
            }
            return result;
        }

        private static int? ToNullableInt(object value)
        {
            return value == null || value == DBNull.Value ? (int?)null : Convert.ToInt32(value);
        }

        private static string ToNullableString(object value)
        {
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static DateTime? ToNullableDateTime(object value)
        {
            return value == null || value == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(value);
        }
    }
}
