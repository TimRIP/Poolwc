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
    public class TournamentPlayerDrawStatus
    {
        public int TournamentId { get; set; }
        public bool UsePlayerDraw { get; set; }
        public bool DrawCompleted { get; set; }
        public int RegisteredPlayers { get; set; }
        public int AssignedPlayers { get; set; }
        public int Capacity { get; set; }
        public bool HasResults { get; set; }
        public bool CanDraw { get; set; }
    }

    public class TournamentPlayerDrawRegistration
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string PlayerName { get; set; }
        public int? PlayerId { get; set; }
    }

    public class TournamentPlayerDrawSlot
    {
        public int PlayerId { get; set; }
        public string SlotName { get; set; }
        public int? AssignedUserId { get; set; }
        public string AssignedPlayerName { get; set; }
    }

    public class TournamentPlayerDrawAssignmentRequest
    {
        public int UserId { get; set; }
        public int PlayerId { get; set; }
    }

    public class TournamentPlayerDrawResult
    {
        public TournamentPlayerDrawStatus Status { get; set; }
        public List<TournamentPlayerDrawRegistration> Registrations { get; set; } = new List<TournamentPlayerDrawRegistration>();
        public List<TournamentPlayerDrawSlot> Slots { get; set; } = new List<TournamentPlayerDrawSlot>();
    }

    internal class DrawRegistration
    {
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public int? PlayerId { get; set; }
    }

    internal class DrawSlot
    {
        public int PlayerId { get; set; }
        public string OriginalPlayerName { get; set; }
    }

    public class TournamentDrawBackoffice
    {
        private readonly IConfigurationRoot _configuration;

        public TournamentDrawBackoffice()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public void ConfigureManualDraw(int tournamentId, int adminUserId, bool enabled)
        {
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                if (!IsTournamentAdmin(conn, null, tournamentId, adminUserId))
                {
                    throw new UnauthorizedAccessException("Only the tournament administrator can configure the player draw.");
                }

                using (SqlCommand cmd = new SqlCommand(@"
UPDATE Tournament
SET UsePlayerDraw = @Enabled,
    PlayerDrawCompleted = CASE WHEN @Enabled = 1 THEN 0 ELSE PlayerDrawCompleted END
WHERE Id = @TournamentId;", conn))
                {
                    cmd.Parameters.Add("@Enabled", SqlDbType.Bit).Value = enabled;
                    cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public TournamentPlayerDrawResult GetDraw(int tournamentId, int userId)
        {
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                if (!IsTournamentAdmin(conn, null, tournamentId, userId))
                {
                    throw new UnauthorizedAccessException("Only the tournament administrator can manage the manual draw.");
                }

                TournamentPlayerDrawStatus status = ReadStatus(conn, null, tournamentId, false);
                if (status == null)
                {
                    return null;
                }

                List<DrawRegistration> registrations = GetRegistrations(conn, null, tournamentId);
                List<DrawSlot> slots = GetSlots(conn, null, tournamentId);

                return BuildResult(status, registrations, slots);
            }
        }

        public TournamentPlayerDrawResult SaveManualDraw(
            int tournamentId,
            int adminUserId,
            IList<TournamentPlayerDrawAssignmentRequest> assignments)
        {
            assignments = assignments ?? new List<TournamentPlayerDrawAssignmentRequest>();

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        if (!IsTournamentAdmin(conn, transaction, tournamentId, adminUserId))
                        {
                            throw new UnauthorizedAccessException("Only the tournament administrator can manage the manual draw.");
                        }

                        TournamentPlayerDrawStatus status = ReadStatus(conn, transaction, tournamentId, true);
                        if (status == null)
                        {
                            throw new InvalidOperationException("Tournament not found.");
                        }

                        if (!status.UsePlayerDraw)
                        {
                            throw new InvalidOperationException("Manual draw is not enabled for this tournament.");
                        }

                        if (status.HasResults)
                        {
                            throw new InvalidOperationException("The player draw cannot be changed after match results have been recorded.");
                        }

                        List<DrawRegistration> registrations = GetRegistrations(conn, transaction, tournamentId);
                        List<DrawSlot> slots = GetSlots(conn, transaction, tournamentId);

                        HashSet<int> validUsers = new HashSet<int>(registrations.Select(x => x.UserId));
                        HashSet<int> validPlayers = new HashSet<int>(slots.Select(x => x.PlayerId));
                        HashSet<int> seenUsers = new HashSet<int>();
                        HashSet<int> seenPlayers = new HashSet<int>();

                        foreach (TournamentPlayerDrawAssignmentRequest assignment in assignments)
                        {
                            if (assignment == null || assignment.UserId <= 0 || assignment.PlayerId <= 0)
                            {
                                throw new InvalidOperationException("Every manual draw assignment must contain a valid user and player place.");
                            }

                            if (!validUsers.Contains(assignment.UserId))
                            {
                                throw new InvalidOperationException("One of the selected users is not registered for this tournament.");
                            }

                            if (!validPlayers.Contains(assignment.PlayerId))
                            {
                                throw new InvalidOperationException("One of the selected player places does not belong to this tournament.");
                            }

                            if (!seenUsers.Add(assignment.UserId))
                            {
                                throw new InvalidOperationException("A registered player can only be assigned to one player place.");
                            }

                            if (!seenPlayers.Add(assignment.PlayerId))
                            {
                                throw new InvalidOperationException("A player place can only be assigned to one registered player.");
                            }
                        }

                        ClearCurrentAssignments(conn, transaction, tournamentId, slots);

                        Dictionary<int, DrawRegistration> registrationByUser = registrations.ToDictionary(x => x.UserId);
                        Dictionary<int, DrawSlot> slotByPlayer = slots.ToDictionary(x => x.PlayerId);

                        foreach (TournamentPlayerDrawAssignmentRequest assignment in assignments)
                        {
                            AssignRegistrationToSlot(
                                conn,
                                transaction,
                                tournamentId,
                                registrationByUser[assignment.UserId],
                                slotByPlayer[assignment.PlayerId]);
                        }

                        bool completed = registrations.Count > 0 && assignments.Count == registrations.Count;
                        using (SqlCommand complete = new SqlCommand(@"
UPDATE Tournament
SET PlayerDrawCompleted = @Completed
WHERE Id = @TournamentId;", conn, transaction))
                        {
                            complete.Parameters.Add("@Completed", SqlDbType.Bit).Value = completed;
                            complete.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                            complete.ExecuteNonQuery();
                        }

                        TournamentPlayerDrawStatus freshStatus = ReadStatus(conn, transaction, tournamentId, false);
                        List<DrawRegistration> freshRegistrations = GetRegistrations(conn, transaction, tournamentId);
                        List<DrawSlot> freshSlots = GetSlots(conn, transaction, tournamentId);
                        TournamentPlayerDrawResult result = BuildResult(freshStatus, freshRegistrations, freshSlots);

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

        private static TournamentPlayerDrawResult BuildResult(
            TournamentPlayerDrawStatus status,
            List<DrawRegistration> registrations,
            List<DrawSlot> slots)
        {
            Dictionary<int, DrawRegistration> assignedByPlayer = registrations
                .Where(x => x.PlayerId.HasValue)
                .GroupBy(x => x.PlayerId.Value)
                .ToDictionary(g => g.Key, g => g.First());

            status.AssignedPlayers = registrations.Count(x => x.PlayerId.HasValue);
            status.DrawCompleted = registrations.Count > 0 && status.AssignedPlayers == registrations.Count;
            status.CanDraw = status.UsePlayerDraw && !status.HasResults;

            return new TournamentPlayerDrawResult
            {
                Status = status,
                Registrations = registrations.Select(x => new TournamentPlayerDrawRegistration
                {
                    UserId = x.UserId,
                    UserName = x.UserName,
                    PlayerName = x.DisplayName,
                    PlayerId = x.PlayerId
                }).ToList(),
                Slots = slots.Select(x =>
                {
                    DrawRegistration assigned;
                    assignedByPlayer.TryGetValue(x.PlayerId, out assigned);
                    return new TournamentPlayerDrawSlot
                    {
                        PlayerId = x.PlayerId,
                        SlotName = x.OriginalPlayerName,
                        AssignedUserId = assigned == null ? (int?)null : assigned.UserId,
                        AssignedPlayerName = assigned == null ? null : assigned.DisplayName
                    };
                }).ToList()
            };
        }

        private static TournamentPlayerDrawStatus ReadStatus(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            bool lockTournament)
        {
            string lockHint = lockTournament ? " WITH (UPDLOCK, HOLDLOCK)" : string.Empty;
            string sql = @"
DECLARE @RootMatchId INT;
DECLARE @UsePlayerDraw BIT;
DECLARE @DrawCompleted BIT;

SELECT
    @RootMatchId = MatchId,
    @UsePlayerDraw = ISNULL(UsePlayerDraw, 0),
    @DrawCompleted = ISNULL(PlayerDrawCompleted, 0)
FROM Tournament" + lockHint + @"
WHERE Id = @TournamentId;

IF @RootMatchId IS NULL
BEGIN
    SELECT CAST(NULL AS INT) AS TournamentId;
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
SlotPlayers AS
(
    SELECT DISTINCT P.Id AS PlayerId
    FROM TournamentTree TT
    INNER JOIN Seat S ON S.MatchId = TT.MatchId
    INNER JOIN Player P ON P.Id = S.PlayerId
),
ResultState AS
(
    SELECT CASE WHEN EXISTS
    (
        SELECT 1
        FROM TournamentTree TT
        INNER JOIN Seat S ON S.MatchId = TT.MatchId
        WHERE (S.ResultMatchPlace IS NOT NULL OR S.ResultPoints IS NOT NULL)
          AND EXISTS
          (
              SELECT 1
              FROM Seat S2
              WHERE S2.MatchId = TT.MatchId
              GROUP BY S2.MatchId
              HAVING COUNT(*) > 1
          )
    ) THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS HasResults
)
SELECT
    @TournamentId AS TournamentId,
    @UsePlayerDraw AS UsePlayerDraw,
    @DrawCompleted AS DrawCompleted,
    (SELECT COUNT(*) FROM TournamentRegistration TR WHERE TR.TournamentId = @TournamentId AND TR.Status = 'registered') AS RegisteredPlayers,
    (SELECT COUNT(*) FROM TournamentRegistration TR WHERE TR.TournamentId = @TournamentId AND TR.Status = 'registered' AND TR.PlayerId IS NOT NULL) AS AssignedPlayers,
    (SELECT COUNT(*) FROM SlotPlayers) AS Capacity,
    RS.HasResults
FROM ResultState RS
OPTION (MAXRECURSION 1000);";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read() || reader["TournamentId"] == DBNull.Value)
                    {
                        return null;
                    }

                    bool usePlayerDraw = Convert.ToBoolean(reader["UsePlayerDraw"]);
                    bool hasResults = Convert.ToBoolean(reader["HasResults"]);
                    int registeredPlayers = Convert.ToInt32(reader["RegisteredPlayers"]);
                    int assignedPlayers = Convert.ToInt32(reader["AssignedPlayers"]);

                    return new TournamentPlayerDrawStatus
                    {
                        TournamentId = tournamentId,
                        UsePlayerDraw = usePlayerDraw,
                        DrawCompleted = Convert.ToBoolean(reader["DrawCompleted"]),
                        RegisteredPlayers = registeredPlayers,
                        AssignedPlayers = assignedPlayers,
                        Capacity = Convert.ToInt32(reader["Capacity"]),
                        HasResults = hasResults,
                        CanDraw = usePlayerDraw && !hasResults
                    };
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

        private static List<DrawRegistration> GetRegistrations(SqlConnection conn, SqlTransaction transaction, int tournamentId)
        {
            const string sql = @"
SELECT
    TR.RegisteredUserId,
    RU.UserName,
    COALESCE(NULLIF(LTRIM(RTRIM(RU.RegisteredName)), ''), RU.UserName) AS DisplayName,
    TR.PlayerId
FROM TournamentRegistration TR WITH (UPDLOCK, HOLDLOCK)
INNER JOIN RegisteredUsers RU ON RU.RegisteredUserID = TR.RegisteredUserId
WHERE TR.TournamentId = @TournamentId
  AND TR.Status = 'registered'
ORDER BY COALESCE(NULLIF(LTRIM(RTRIM(RU.RegisteredName)), ''), RU.UserName), RU.UserName;";

            var registrations = new List<DrawRegistration>();
            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        registrations.Add(new DrawRegistration
                        {
                            UserId = Convert.ToInt32(reader["RegisteredUserId"]),
                            UserName = Convert.ToString(reader["UserName"]),
                            DisplayName = Convert.ToString(reader["DisplayName"]),
                            PlayerId = reader["PlayerId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PlayerId"])
                        });
                    }
                }
            }

            return registrations;
        }

        private static List<DrawSlot> GetSlots(SqlConnection conn, SqlTransaction transaction, int tournamentId)
        {
            const string sql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId FROM Tournament WHERE Id = @TournamentId;

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
SlotPlayers AS
(
    SELECT DISTINCT P.Id, P.Name
    FROM TournamentTree TT
    INNER JOIN Seat S ON S.MatchId = TT.MatchId
    INNER JOIN Player P ON P.Id = S.PlayerId
)
SELECT
    SP.Id AS PlayerId,
    COALESCE(
        NULLIF(LTRIM(RTRIM(CurrentRegistration.OriginalPlayerName)), ''),
        SP.Name
    ) AS OriginalPlayerName
FROM SlotPlayers SP
OUTER APPLY
(
    SELECT TOP 1 TR.OriginalPlayerName
    FROM TournamentRegistration TR
    WHERE TR.TournamentId = @TournamentId
      AND TR.PlayerId = SP.Id
      AND TR.OriginalPlayerName IS NOT NULL
    ORDER BY TR.UpdatedAt DESC, TR.Id DESC
) CurrentRegistration
ORDER BY
    CASE
        WHEN COALESCE(NULLIF(LTRIM(RTRIM(CurrentRegistration.OriginalPlayerName)), ''), SP.Name) LIKE 'Player:%'
        THEN TRY_CONVERT(INT, REPLACE(COALESCE(NULLIF(LTRIM(RTRIM(CurrentRegistration.OriginalPlayerName)), ''), SP.Name), 'Player:', ''))
        ELSE NULL
    END,
    SP.Id
OPTION (MAXRECURSION 1000);";

            var slots = new List<DrawSlot>();
            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        slots.Add(new DrawSlot
                        {
                            PlayerId = Convert.ToInt32(reader["PlayerId"]),
                            OriginalPlayerName = Convert.ToString(reader["OriginalPlayerName"])
                        });
                    }
                }
            }

            return slots;
        }

        private static void ClearCurrentAssignments(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            List<DrawSlot> slots)
        {
            using (SqlCommand clearRegistrations = new SqlCommand(@"
UPDATE TournamentRegistration
SET PlayerId = NULL,
    OriginalPlayerName = NULL,
    UpdatedAt = GETUTCDATE()
WHERE TournamentId = @TournamentId
  AND Status = 'registered';", conn, transaction))
            {
                clearRegistrations.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                clearRegistrations.ExecuteNonQuery();
            }

            foreach (DrawSlot slot in slots)
            {
                using (SqlCommand clearPlayer = new SqlCommand(@"
UPDATE Player
SET RegisteredUserID = NULL,
    Name = @OriginalPlayerName,
    LastUsedAt = GETUTCDATE()
WHERE Id = @PlayerId;", conn, transaction))
                {
                    clearPlayer.Parameters.Add("@OriginalPlayerName", SqlDbType.NVarChar, 255).Value = slot.OriginalPlayerName;
                    clearPlayer.Parameters.Add("@PlayerId", SqlDbType.Int).Value = slot.PlayerId;
                    clearPlayer.ExecuteNonQuery();
                }
            }
        }

        private static void AssignRegistrationToSlot(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            DrawRegistration registration,
            DrawSlot slot)
        {
            using (SqlCommand assignPlayer = new SqlCommand(@"
UPDATE Player
SET RegisteredUserID = @UserId,
    Name = @DisplayName,
    LastUsedAt = GETUTCDATE()
WHERE Id = @PlayerId;", conn, transaction))
            {
                assignPlayer.Parameters.Add("@UserId", SqlDbType.Int).Value = registration.UserId;
                assignPlayer.Parameters.Add("@DisplayName", SqlDbType.NVarChar, 255).Value = registration.DisplayName;
                assignPlayer.Parameters.Add("@PlayerId", SqlDbType.Int).Value = slot.PlayerId;
                assignPlayer.ExecuteNonQuery();
            }

            using (SqlCommand assignRegistration = new SqlCommand(@"
UPDATE TournamentRegistration
SET PlayerId = @PlayerId,
    OriginalPlayerName = @OriginalPlayerName,
    UpdatedAt = GETUTCDATE()
WHERE TournamentId = @TournamentId
  AND RegisteredUserId = @UserId
  AND Status = 'registered';", conn, transaction))
            {
                assignRegistration.Parameters.Add("@PlayerId", SqlDbType.Int).Value = slot.PlayerId;
                assignRegistration.Parameters.Add("@OriginalPlayerName", SqlDbType.NVarChar, 255).Value = slot.OriginalPlayerName;
                assignRegistration.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                assignRegistration.Parameters.Add("@UserId", SqlDbType.Int).Value = registration.UserId;
                assignRegistration.ExecuteNonQuery();
            }
        }
    }
}
