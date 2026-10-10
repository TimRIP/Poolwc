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
    public class TournamentPlayerSearchItem
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int? Mmr { get; set; }
        public bool IsRegistered { get; set; }
    }

    public class TournamentPlayerMatchSeat
    {
        public int SeatId { get; set; }
        public int? PlayerId { get; set; }
        public string PlayerName { get; set; }
        public int? ResultMatchPlace { get; set; }
        public int? ResultPoints { get; set; }
    }

    public class TournamentPlayerMatchItem
    {
        public int MatchId { get; set; }
        public string MatchName { get; set; }
        public string PoolName { get; set; }
        public string StageName { get; set; }
        public string VenueName { get; set; }
        public DateTime? FromTime { get; set; }
        public int? PlayFrom { get; set; }
        public int? PlayTo { get; set; }
        public bool Played { get; set; }
        public string Result { get; set; }
        public string Opponents { get; set; }
        public string Score { get; set; }
        public List<TournamentPlayerMatchSeat> Seats { get; set; } = new List<TournamentPlayerMatchSeat>();
    }

    public class TournamentPlayerMatchBackoffice
    {
        private readonly IConfigurationRoot _configuration;

        public TournamentPlayerMatchBackoffice()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public List<TournamentPlayerSearchItem> GetPlayers(int tournamentId, int userId)
        {
            EnsureMatchAccess(tournamentId, userId);

            const string sql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId FROM Tournament WHERE Id = @TournamentId;

;WITH TournamentTree AS
(
    SELECT M.Id AS MatchId,
           CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @RootMatchId

    UNION ALL

    SELECT M.Id,
           CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> @RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT
    P.Id AS PlayerId,
    P.Name AS PlayerName,
    RU.Mmr,
    CASE WHEN COALESCE(P.RegisteredUserID, PR.RegisteredUserId) IS NULL THEN 0 ELSE 1 END AS IsRegistered
FROM Player P
OUTER APPLY
(
    SELECT TOP 1 TR.RegisteredUserId
    FROM TournamentRegistration TR
    WHERE TR.TournamentId = @TournamentId
      AND TR.PlayerId = P.Id
      AND TR.Status = 'registered'
    ORDER BY TR.UpdatedAt DESC, TR.RegisteredAt DESC
) PR
LEFT JOIN RegisteredUsers RU
    ON RU.RegisteredUserID = COALESCE(P.RegisteredUserID, PR.RegisteredUserId)
WHERE EXISTS
(
    SELECT 1
    FROM TournamentTree TT
    INNER JOIN Seat S ON S.MatchId = TT.MatchId
    WHERE S.PlayerId = P.Id
)
ORDER BY P.Name, P.Id
OPTION (MAXRECURSION 1000);";

            var result = new List<TournamentPlayerSearchItem>();
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                conn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(new TournamentPlayerSearchItem
                        {
                            PlayerId = Convert.ToInt32(reader["PlayerId"]),
                            PlayerName = Convert.ToString(reader["PlayerName"]),
                            Mmr = reader["Mmr"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["Mmr"]),
                            IsRegistered = Convert.ToBoolean(reader["IsRegistered"])
                        });
                    }
                }
            }

            return result;
        }

        public List<TournamentPlayerMatchItem> GetPlayerMatches(int tournamentId, int userId, int playerId)
        {
            EnsureMatchAccess(tournamentId, userId);

            const string sql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId FROM Tournament WHERE Id = @TournamentId;

;WITH TournamentTree AS
(
    SELECT
        0 AS [Level],
        M.Id AS MatchId,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @RootMatchId

    UNION ALL

    SELECT
        TT.[Level] + 1,
        M.Id,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> @RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
),
PlayerMatches AS
(
    SELECT TT.[Level], TT.MatchId
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.Id = TT.MatchId
    WHERE M.IndividualMatch = 1
      AND EXISTS
      (
          SELECT 1
          FROM Seat PS
          WHERE PS.MatchId = M.Id
            AND PS.PlayerId = @PlayerId
      )
)
SELECT
    PM.[Level],
    M.Id AS MatchId,
    M.Name AS MatchName,
    ParentM.Name AS PoolName,
    GrandM.Name AS StageName,
    SCH.FromTime,
    F.Name AS VenueName,
    MR.PlayFrom,
    MR.PlayTo,
    S.Id AS SeatId,
    S.PlayerId,
    P.Name AS PlayerName,
    S.ResultMatchPlace,
    S.ResultPoints
FROM PlayerMatches PM
INNER JOIN [Match] M ON M.Id = PM.MatchId
LEFT JOIN [Match] ParentM ON ParentM.Id = M.ParentMatchId
LEFT JOIN [Match] GrandM ON GrandM.Id = ParentM.ParentMatchId
LEFT JOIN MatchRules MR ON MR.Id = M.MatchRulesId
LEFT JOIN Schedule SCH ON SCH.Id = CASE
    WHEN M.ScheduleId IS NOT NULL THEN M.ScheduleId
    WHEN ParentM.ScheduleId IS NOT NULL THEN ParentM.ScheduleId
    ELSE NULL
END
LEFT JOIN Facility F ON F.Id = SCH.FacilityId
LEFT JOIN Seat S ON S.MatchId = M.Id
LEFT JOIN Player P ON P.Id = S.PlayerId
ORDER BY PM.[Level], M.Id, S.Id
OPTION (MAXRECURSION 1000);";

            var matches = new Dictionary<int, TournamentPlayerMatchItem>();
            var matchOrder = new List<int>();

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int matchId = Convert.ToInt32(reader["MatchId"]);
                        if (!matches.TryGetValue(matchId, out TournamentPlayerMatchItem item))
                        {
                            item = new TournamentPlayerMatchItem
                            {
                                MatchId = matchId,
                                MatchName = Convert.ToString(reader["MatchName"]),
                                PoolName = reader["PoolName"] == DBNull.Value ? null : Convert.ToString(reader["PoolName"]),
                                StageName = reader["StageName"] == DBNull.Value ? null : Convert.ToString(reader["StageName"]),
                                VenueName = reader["VenueName"] == DBNull.Value ? null : Convert.ToString(reader["VenueName"]),
                                FromTime = reader["FromTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["FromTime"]),
                                PlayFrom = reader["PlayFrom"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PlayFrom"]),
                                PlayTo = reader["PlayTo"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PlayTo"])
                            };
                            matches.Add(matchId, item);
                            matchOrder.Add(matchId);
                        }

                        if (reader["SeatId"] != DBNull.Value)
                        {
                            item.Seats.Add(new TournamentPlayerMatchSeat
                            {
                                SeatId = Convert.ToInt32(reader["SeatId"]),
                                PlayerId = reader["PlayerId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PlayerId"]),
                                PlayerName = reader["PlayerName"] == DBNull.Value ? null : Convert.ToString(reader["PlayerName"]),
                                ResultMatchPlace = reader["ResultMatchPlace"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["ResultMatchPlace"]),
                                ResultPoints = reader["ResultPoints"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["ResultPoints"])
                            });
                        }
                    }
                }
            }

            foreach (int matchId in matchOrder)
            {
                TournamentPlayerMatchItem match = matches[matchId];
                List<TournamentPlayerMatchSeat> assigned = match.Seats.Where(x => x.PlayerId.HasValue).ToList();
                TournamentPlayerMatchSeat mine = assigned.FirstOrDefault(x => x.PlayerId == playerId);
                List<TournamentPlayerMatchSeat> others = assigned.Where(x => x.PlayerId != playerId).ToList();

                match.Played = assigned.Count > 0 && assigned.All(x => x.ResultMatchPlace.HasValue);

                if (others.Count == 0)
                {
                    match.Opponents = match.Played && mine != null && mine.ResultMatchPlace == 1
                        ? "BYE"
                        : "Opponent not assigned";
                }
                else
                {
                    match.Opponents = string.Join(", ", others.Select(x => x.PlayerName ?? ("Player #" + x.PlayerId)));
                }

                if (match.Played && mine != null && mine.ResultMatchPlace.HasValue)
                {
                    if (mine.ResultMatchPlace.Value == 1)
                    {
                        match.Result = "Won";
                    }
                    else if (assigned.Count == 2)
                    {
                        match.Result = "Lost";
                    }
                    else
                    {
                        match.Result = "Place " + mine.ResultMatchPlace.Value;
                    }
                }
                else
                {
                    match.Result = "Pending";
                }

                if (mine != null && mine.ResultPoints.HasValue)
                {
                    if (others.Count == 1 && others[0].ResultPoints.HasValue)
                    {
                        match.Score = mine.ResultPoints.Value + " - " + others[0].ResultPoints.Value;
                    }
                    else
                    {
                        match.Score = mine.ResultPoints.Value.ToString();
                    }
                }
            }

            return matchOrder.Select(id => matches[id]).ToList();
        }

        private static void EnsureMatchAccess(int tournamentId, int userId)
        {
            TournamentEditorBackoffice editorBackoffice = new TournamentEditorBackoffice();
            TournamentEditorAccess access = editorBackoffice.GetAccess(tournamentId, userId);
            if (access == null || !access.CanEditMatches)
            {
                throw new UnauthorizedAccessException("You do not have access to this tournament's matches.");
            }
        }
    }
}
