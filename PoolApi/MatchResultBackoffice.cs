using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;

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
    }

    internal class StageSeatScore
    {
        public int SeatId { get; set; }
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int Wins { get; set; }
        public int? ScoreDifference { get; set; }
        public int HeadToHeadWins { get; set; }
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
    S.ResultPoints
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
                                ResultPoints = ToNullableInt(reader["ResultPoints"])
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
                        SaveMatchResults(conn, transaction, matchId, results);
                        StageAdvanceResult advancement = RecalculateParentStage(conn, transaction, matchId);
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
