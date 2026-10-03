using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace DrukDatabaseLayer
{
    public class PlayerUserProfile
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public int Mmr { get; set; }
    }

    public class PlayerTournamentInfo
    {
        public int TournamentId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsPrivate { get; set; }
        public int Capacity { get; set; }
        public int RegisteredPlayers { get; set; }
        public int AvailableSlots { get; set; }
        public bool IsFull { get; set; }
        public bool IsRegistered { get; set; }
        public int? PlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    public class TournamentRegistrationResult
    {
        public int TournamentId { get; set; }
        public string TournamentName { get; set; }
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public bool AlreadyRegistered { get; set; }
    }

    public class PlayerPoolAssignment
    {
        public int PoolMatchId { get; set; }
        public string PoolName { get; set; }
        public int? StageMatchId { get; set; }
        public string StageName { get; set; }
        public int? ScheduleId { get; set; }
        public DateTime? FromTime { get; set; }
        public int? FacilityId { get; set; }
        public string VenueName { get; set; }
        public string VenueDescription { get; set; }
    }

    public class PlayerRegistrationBackoffice
    {
        private readonly IConfigurationRoot _configuration;

        public PlayerRegistrationBackoffice()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            _configuration = builder.Build();
        }

        public int CreateUser(string username, string password, string name, string email)
        {
            username = (username ?? string.Empty).Trim();
            name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

            const string sql = @"
IF EXISTS (SELECT 1 FROM RegisteredUsers WHERE UserName = @UserName)
BEGIN
    THROW 51000, 'That username is already registered.', 1;
END;

INSERT INTO RegisteredUsers
(
    UserName,
    PasswordHash,
    RegisteredName,
    RegisteredEMail,
    CreatedAt,
    LastUsedAt
)
VALUES
(
    @UserName,
    HASHBYTES('SHA2_512', @Password),
    @RegisteredName,
    @RegisteredEmail,
    GETUTCDATE(),
    GETUTCDATE()
);

SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@UserName", SqlDbType.NVarChar, 30).Value = username;
                cmd.Parameters.Add("@Password", SqlDbType.NVarChar, 50).Value = password;
                cmd.Parameters.Add("@RegisteredName", SqlDbType.NVarChar, 50).Value = (object)name ?? DBNull.Value;
                cmd.Parameters.Add("@RegisteredEmail", SqlDbType.NVarChar, 100).Value = (object)email ?? DBNull.Value;

                conn.Open();
                try
                {
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
                catch (SqlException ex) when (ex.Number == 51000 || ex.Number == 2601 || ex.Number == 2627)
                {
                    throw new InvalidOperationException("That username is already registered.");
                }
            }
        }

        public PlayerUserProfile GetUserProfile(int userId)
        {
            const string sql = @"
SELECT RegisteredUserID, UserName, RegisteredName, RegisteredEMail, ISNULL(Mmr, 1000) AS Mmr
FROM RegisteredUsers
WHERE RegisteredUserID = @UserId;";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    string username = Convert.ToString(reader["UserName"]);
                    string displayName = reader["RegisteredName"] == DBNull.Value
                        ? username
                        : Convert.ToString(reader["RegisteredName"]);

                    return new PlayerUserProfile
                    {
                        UserId = Convert.ToInt32(reader["RegisteredUserID"]),
                        Username = username,
                        Name = displayName,
                        Email = reader["RegisteredEMail"] == DBNull.Value
                            ? null
                            : Convert.ToString(reader["RegisteredEMail"]),
                        Mmr = Convert.ToInt32(reader["Mmr"])
                    };
                }
            }
        }

        public List<PlayerTournamentInfo> GetTournamentsForUser(int userId)
        {
            const string sql = @"
;WITH TournamentTree AS
(
    SELECT
        T.Id AS TournamentId,
        T.MatchId AS RootMatchId,
        M.Id AS MatchId,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
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
),
SlotPlayers AS
(
    SELECT
        TT.TournamentId,
        P.Id AS PlayerId,
        P.RegisteredUserID,
        P.Name,
        MAX(CASE WHEN S.ResultMatchPlace IS NOT NULL OR S.ResultPoints IS NOT NULL THEN 1 ELSE 0 END) AS HasResult
    FROM TournamentTree TT
    INNER JOIN Seat S ON S.MatchId = TT.MatchId
    INNER JOIN Player P ON P.Id = S.PlayerId
    GROUP BY TT.TournamentId, P.Id, P.RegisteredUserID, P.Name
),
SlotCounts AS
(
    SELECT
        TournamentId,
        COUNT(*) AS Capacity,
        SUM(CASE WHEN RegisteredUserID IS NULL AND Name LIKE 'Player:%' AND HasResult = 0 THEN 1 ELSE 0 END) AS AvailableSlots
    FROM SlotPlayers
    GROUP BY TournamentId
)
SELECT
    T.Id AS TournamentId,
    RM.Name AS TournamentName,
    T.Description,
    ISNULL(T.IsPrivate, 0) AS IsPrivate,
    ISNULL(SC.Capacity, 0) AS Capacity,
    ISNULL(SC.Capacity, 0) - ISNULL(SC.AvailableSlots, 0) AS RegisteredPlayers,
    ISNULL(SC.AvailableSlots, 0) AS AvailableSlots,
    CASE WHEN ISNULL(SC.AvailableSlots, 0) <= 0 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsFull,
    CASE WHEN TR.Id IS NOT NULL AND TR.Status = 'registered' THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsRegistered,
    CASE WHEN TR.Status = 'registered' THEN TR.PlayerId ELSE NULL END AS PlayerId,
    CASE WHEN TR.Status = 'registered' THEN RP.Name ELSE NULL END AS PlayerName
FROM Tournament T
INNER JOIN [Match] RM ON RM.Id = T.MatchId
LEFT JOIN SlotCounts SC ON SC.TournamentId = T.Id
LEFT JOIN TournamentRegistration TR
    ON TR.TournamentId = T.Id
   AND TR.RegisteredUserId = @UserId
LEFT JOIN Player RP ON RP.Id = TR.PlayerId
WHERE ISNULL(T.IsPrivate, 0) = 0
   OR (TR.Id IS NOT NULL AND TR.Status = 'registered')
ORDER BY T.Id DESC
OPTION (MAXRECURSION 1000);";

            var tournaments = new List<PlayerTournamentInfo>();

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tournaments.Add(new PlayerTournamentInfo
                        {
                            TournamentId = Convert.ToInt32(reader["TournamentId"]),
                            Name = Convert.ToString(reader["TournamentName"]),
                            Description = reader["Description"] == DBNull.Value ? null : Convert.ToString(reader["Description"]),
                            IsPrivate = Convert.ToBoolean(reader["IsPrivate"]),
                            Capacity = Convert.ToInt32(reader["Capacity"]),
                            RegisteredPlayers = Convert.ToInt32(reader["RegisteredPlayers"]),
                            AvailableSlots = Convert.ToInt32(reader["AvailableSlots"]),
                            IsFull = Convert.ToBoolean(reader["IsFull"]),
                            IsRegistered = Convert.ToBoolean(reader["IsRegistered"]),
                            PlayerId = reader["PlayerId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PlayerId"]),
                            PlayerName = reader["PlayerName"] == DBNull.Value ? null : Convert.ToString(reader["PlayerName"])
                        });
                    }
                }
            }

            return tournaments;
        }

        public PlayerTournamentInfo FindTournamentByJoinCode(int userId, string joinCode)
        {
            joinCode = NormalizeJoinCode(joinCode);
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return null;
            }

            const string sql = @"
;WITH TournamentTree AS
(
    SELECT
        T.Id AS TournamentId,
        T.MatchId AS RootMatchId,
        M.Id AS MatchId,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM Tournament T
    INNER JOIN [Match] M ON M.Id = T.MatchId
    WHERE ISNULL(T.IsPrivate, 0) = 1
      AND UPPER(T.JoinCode) = @JoinCode

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
),
SlotPlayers AS
(
    SELECT
        TT.TournamentId,
        P.Id AS PlayerId,
        P.RegisteredUserID,
        P.Name,
        MAX(CASE WHEN S.ResultMatchPlace IS NOT NULL OR S.ResultPoints IS NOT NULL THEN 1 ELSE 0 END) AS HasResult
    FROM TournamentTree TT
    INNER JOIN Seat S ON S.MatchId = TT.MatchId
    INNER JOIN Player P ON P.Id = S.PlayerId
    GROUP BY TT.TournamentId, P.Id, P.RegisteredUserID, P.Name
),
SlotCounts AS
(
    SELECT
        TournamentId,
        COUNT(*) AS Capacity,
        SUM(CASE WHEN RegisteredUserID IS NULL AND Name LIKE 'Player:%' AND HasResult = 0 THEN 1 ELSE 0 END) AS AvailableSlots
    FROM SlotPlayers
    GROUP BY TournamentId
)
SELECT TOP 1
    T.Id AS TournamentId,
    RM.Name AS TournamentName,
    T.Description,
    ISNULL(T.IsPrivate, 0) AS IsPrivate,
    ISNULL(SC.Capacity, 0) AS Capacity,
    ISNULL(SC.Capacity, 0) - ISNULL(SC.AvailableSlots, 0) AS RegisteredPlayers,
    ISNULL(SC.AvailableSlots, 0) AS AvailableSlots,
    CASE WHEN ISNULL(SC.AvailableSlots, 0) <= 0 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsFull,
    CASE WHEN TR.Id IS NOT NULL AND TR.Status = 'registered' THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsRegistered,
    CASE WHEN TR.Status = 'registered' THEN TR.PlayerId ELSE NULL END AS PlayerId,
    CASE WHEN TR.Status = 'registered' THEN RP.Name ELSE NULL END AS PlayerName
FROM Tournament T
INNER JOIN [Match] RM ON RM.Id = T.MatchId
LEFT JOIN SlotCounts SC ON SC.TournamentId = T.Id
LEFT JOIN TournamentRegistration TR
    ON TR.TournamentId = T.Id
   AND TR.RegisteredUserId = @UserId
LEFT JOIN Player RP ON RP.Id = TR.PlayerId
WHERE ISNULL(T.IsPrivate, 0) = 1
  AND UPPER(T.JoinCode) = @JoinCode
OPTION (MAXRECURSION 1000);";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                cmd.Parameters.Add("@JoinCode", SqlDbType.NVarChar, 20).Value = joinCode;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new PlayerTournamentInfo
                    {
                        TournamentId = Convert.ToInt32(reader["TournamentId"]),
                        Name = Convert.ToString(reader["TournamentName"]),
                        Description = reader["Description"] == DBNull.Value ? null : Convert.ToString(reader["Description"]),
                        IsPrivate = Convert.ToBoolean(reader["IsPrivate"]),
                        Capacity = Convert.ToInt32(reader["Capacity"]),
                        RegisteredPlayers = Convert.ToInt32(reader["RegisteredPlayers"]),
                        AvailableSlots = Convert.ToInt32(reader["AvailableSlots"]),
                        IsFull = Convert.ToBoolean(reader["IsFull"]),
                        IsRegistered = Convert.ToBoolean(reader["IsRegistered"]),
                        PlayerId = reader["PlayerId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PlayerId"]),
                        PlayerName = reader["PlayerName"] == DBNull.Value ? null : Convert.ToString(reader["PlayerName"])
                    };
                }
            }
        }

        public TournamentRegistrationResult RegisterForTournament(int tournamentId, int userId, string joinCode = null)
        {
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        string tournamentName = GetTournamentName(conn, transaction, tournamentId);
                        if (tournamentName == null)
                        {
                            throw new InvalidOperationException("Tournament not found.");
                        }

                        TournamentRegistrationResult existing = GetActiveRegistration(conn, transaction, tournamentId, userId);
                        if (existing != null)
                        {
                            existing.AlreadyRegistered = true;
                            transaction.Commit();
                            return existing;
                        }

                        ValidateTournamentAccess(conn, transaction, tournamentId, joinCode);

                        string displayName = GetDisplayName(conn, transaction, userId);
                        if (displayName == null)
                        {
                            throw new InvalidOperationException("User not found.");
                        }

                        int playerId;
                        string originalPlayerName;
                        FindFreePlayerSlot(conn, transaction, tournamentId, out playerId, out originalPlayerName);

                        if (playerId <= 0)
                        {
                            throw new InvalidOperationException("There are no free player places in this tournament.");
                        }

                        const string claimSql = @"
UPDATE Player
SET RegisteredUserID = @UserId,
    Name = @DisplayName,
    LastUsedAt = GETUTCDATE()
WHERE Id = @PlayerId
  AND RegisteredUserID IS NULL;

IF @@ROWCOUNT <> 1
BEGIN
    THROW 51001, 'The player place was taken by another user. Please try again.', 1;
END;";

                        using (SqlCommand claim = new SqlCommand(claimSql, conn, transaction))
                        {
                            claim.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                            claim.Parameters.Add("@DisplayName", SqlDbType.NVarChar, 255).Value = displayName;
                            claim.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                            claim.ExecuteNonQuery();
                        }

                        const string registrationSql = @"
IF EXISTS
(
    SELECT 1
    FROM TournamentRegistration
    WHERE TournamentId = @TournamentId
      AND RegisteredUserId = @UserId
)
BEGIN
    UPDATE TournamentRegistration
    SET PlayerId = @PlayerId,
        OriginalPlayerName = @OriginalPlayerName,
        Status = 'registered',
        RegisteredAt = GETUTCDATE(),
        UpdatedAt = GETUTCDATE()
    WHERE TournamentId = @TournamentId
      AND RegisteredUserId = @UserId;
END
ELSE
BEGIN
    INSERT INTO TournamentRegistration
    (
        TournamentId,
        RegisteredUserId,
        PlayerId,
        OriginalPlayerName,
        Status,
        RegisteredAt,
        UpdatedAt
    )
    VALUES
    (
        @TournamentId,
        @UserId,
        @PlayerId,
        @OriginalPlayerName,
        'registered',
        GETUTCDATE(),
        GETUTCDATE()
    );
END;";

                        using (SqlCommand registration = new SqlCommand(registrationSql, conn, transaction))
                        {
                            registration.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                            registration.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                            registration.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                            registration.Parameters.Add("@OriginalPlayerName", SqlDbType.NVarChar, 255).Value = originalPlayerName;
                            registration.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return new TournamentRegistrationResult
                        {
                            TournamentId = tournamentId,
                            TournamentName = tournamentName,
                            PlayerId = playerId,
                            PlayerName = displayName,
                            AlreadyRegistered = false
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

        public List<PlayerPoolAssignment> GetPoolAssignmentsForUser(int tournamentId, int userId)
        {
            const string sql = @"
DECLARE @RootMatchId INT;
DECLARE @PlayerId INT;

SELECT @RootMatchId = T.MatchId
FROM Tournament T
WHERE T.Id = @TournamentId;

SELECT @PlayerId = TR.PlayerId
FROM TournamentRegistration TR
WHERE TR.TournamentId = @TournamentId
  AND TR.RegisteredUserId = @UserId
  AND TR.Status = 'registered';

IF @RootMatchId IS NULL
BEGIN
    THROW 51010, 'Tournament not found.', 1;
END;

IF @PlayerId IS NULL
BEGIN
    THROW 51011, 'You are not registered for this tournament.', 1;
END;

;WITH TournamentTree AS
(
    SELECT
        M.Id AS MatchId,
        M.ParentMatchId,
        M.Name,
        M.IndividualMatch,
        M.ScheduleId,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @RootMatchId

    UNION ALL

    SELECT
        M.Id,
        M.ParentMatchId,
        M.Name,
        M.IndividualMatch,
        M.ScheduleId,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> @RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
),
PoolAssignments AS
(
    SELECT DISTINCT
        Pool.MatchId AS PoolMatchId,
        Pool.Name AS PoolName,
        Stage.Id AS StageMatchId,
        Stage.Name AS StageName,
        SCH.Id AS ScheduleId,
        SCH.FromTime,
        F.Id AS FacilityId,
        F.Name AS VenueName,
        F.Description AS VenueDescription
    FROM TournamentTree Pool
    LEFT JOIN [Match] Stage ON Stage.Id = Pool.ParentMatchId
    LEFT JOIN Schedule SCH ON SCH.Id = Pool.ScheduleId
    LEFT JOIN Facility F ON F.Id = SCH.FacilityId
    WHERE Pool.IndividualMatch = 0
      AND EXISTS
      (
          SELECT 1
          FROM [Match] ChildMatch
          WHERE ChildMatch.ParentMatchId = Pool.MatchId
            AND ChildMatch.IndividualMatch = 1
      )
      AND
      (
          -- Normal case: the player is assigned directly to a seat on the pool node.
          EXISTS
          (
              SELECT 1
              FROM Seat PoolSeat
              WHERE PoolSeat.MatchId = Pool.MatchId
                AND PoolSeat.PlayerId = @PlayerId
          )
          OR
          -- Older/generated trees can carry the player on the playable child seat.
          -- Venue/schedule is deliberately not part of membership, so the pool is
          -- returned even when ScheduleId is NULL.
          EXISTS
          (
              SELECT 1
              FROM [Match] ChildMatch
              INNER JOIN Seat ChildSeat ON ChildSeat.MatchId = ChildMatch.Id
              LEFT JOIN Seat ParentPoolSeat ON ParentPoolSeat.Id = ChildSeat.ParentSeatId
              WHERE ChildMatch.ParentMatchId = Pool.MatchId
                AND ChildMatch.IndividualMatch = 1
                AND
                (
                    ChildSeat.PlayerId = @PlayerId
                    OR ParentPoolSeat.PlayerId = @PlayerId
                )
          )
      )
)
SELECT
    PoolMatchId,
    PoolName,
    StageMatchId,
    StageName,
    ScheduleId,
    FromTime,
    FacilityId,
    VenueName,
    VenueDescription
FROM PoolAssignments
ORDER BY
    CASE WHEN FromTime IS NULL THEN 1 ELSE 0 END,
    FromTime,
    StageName,
    PoolName,
    PoolMatchId
OPTION (MAXRECURSION 1000);";

            var assignments = new List<PlayerPoolAssignment>();

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();

                try
                {
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            assignments.Add(new PlayerPoolAssignment
                            {
                                PoolMatchId = Convert.ToInt32(reader["PoolMatchId"]),
                                PoolName = reader["PoolName"] == DBNull.Value ? null : Convert.ToString(reader["PoolName"]),
                                StageMatchId = reader["StageMatchId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["StageMatchId"]),
                                StageName = reader["StageName"] == DBNull.Value ? null : Convert.ToString(reader["StageName"]),
                                ScheduleId = reader["ScheduleId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["ScheduleId"]),
                                FromTime = reader["FromTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["FromTime"]),
                                FacilityId = reader["FacilityId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["FacilityId"]),
                                VenueName = reader["VenueName"] == DBNull.Value ? null : Convert.ToString(reader["VenueName"]),
                                VenueDescription = reader["VenueDescription"] == DBNull.Value ? null : Convert.ToString(reader["VenueDescription"])
                            });
                        }
                    }
                }
                catch (SqlException ex) when (ex.Number == 51010 || ex.Number == 51011)
                {
                    throw new InvalidOperationException(ex.Message);
                }
            }

            return assignments;
        }

        public void CancelTournamentRegistration(int tournamentId, int userId)
        {
            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        int playerId = -1;
                        string originalPlayerName = null;

                        const string registrationSql = @"
SELECT PlayerId, OriginalPlayerName
FROM TournamentRegistration WITH (UPDLOCK, HOLDLOCK)
WHERE TournamentId = @TournamentId
  AND RegisteredUserId = @UserId
  AND Status = 'registered';";

                        using (SqlCommand registration = new SqlCommand(registrationSql, conn, transaction))
                        {
                            registration.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                            registration.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;

                            using (SqlDataReader reader = registration.ExecuteReader())
                            {
                                if (!reader.Read())
                                {
                                    throw new InvalidOperationException("You are not registered for this tournament.");
                                }

                                playerId = reader["PlayerId"] == DBNull.Value ? -1 : Convert.ToInt32(reader["PlayerId"]);
                                originalPlayerName = reader["OriginalPlayerName"] == DBNull.Value
                                    ? null
                                    : Convert.ToString(reader["OriginalPlayerName"]);
                            }
                        }

                        if (playerId <= 0)
                        {
                            throw new InvalidOperationException("The tournament registration does not have a player place assigned.");
                        }

                        if (PlayerHasRecordedResults(conn, transaction, tournamentId, playerId))
                        {
                            throw new InvalidOperationException("Registration cannot be cancelled after match results have been recorded for this player.");
                        }

                        if (string.IsNullOrWhiteSpace(originalPlayerName))
                        {
                            originalPlayerName = "Player:" + playerId;
                        }

                        const string releaseSql = @"
UPDATE Player
SET RegisteredUserID = NULL,
    Name = @OriginalPlayerName,
    LastUsedAt = GETUTCDATE()
WHERE Id = @PlayerId
  AND RegisteredUserID = @UserId;

UPDATE TournamentRegistration
SET PlayerId = NULL,
    Status = 'cancelled',
    UpdatedAt = GETUTCDATE()
WHERE TournamentId = @TournamentId
  AND RegisteredUserId = @UserId;";

                        using (SqlCommand release = new SqlCommand(releaseSql, conn, transaction))
                        {
                            release.Parameters.Add("@OriginalPlayerName", SqlDbType.NVarChar, 255).Value = originalPlayerName;
                            release.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                            release.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                            release.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                            release.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private static string NormalizeJoinCode(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return null;
            }

            return joinCode.Trim()
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty)
                .ToUpperInvariant();
        }

        private static void ValidateTournamentAccess(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            string joinCode)
        {
            const string sql = @"
SELECT ISNULL(IsPrivate, 0) AS IsPrivate, JoinCode
FROM Tournament WITH (UPDLOCK, HOLDLOCK)
WHERE Id = @TournamentId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        throw new InvalidOperationException("Tournament not found.");
                    }

                    bool isPrivate = Convert.ToBoolean(reader["IsPrivate"]);
                    string expectedCode = reader["JoinCode"] == DBNull.Value
                        ? null
                        : Convert.ToString(reader["JoinCode"]);

                    if (!isPrivate)
                    {
                        return;
                    }

                    string suppliedCode = NormalizeJoinCode(joinCode);
                    if (string.IsNullOrWhiteSpace(suppliedCode) ||
                        !string.Equals(suppliedCode, NormalizeJoinCode(expectedCode), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("A valid join code is required for this private tournament.");
                    }
                }
            }
        }

        private static string GetTournamentName(SqlConnection conn, SqlTransaction transaction, int tournamentId)
        {
            const string sql = @"
SELECT RM.Name
FROM Tournament T
INNER JOIN [Match] RM ON RM.Id = T.MatchId
WHERE T.Id = @TournamentId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            }
        }

        private static string GetDisplayName(SqlConnection conn, SqlTransaction transaction, int userId)
        {
            const string sql = @"
SELECT COALESCE(NULLIF(LTRIM(RTRIM(RegisteredName)), ''), UserName)
FROM RegisteredUsers
WHERE RegisteredUserID = @UserId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            }
        }

        private static TournamentRegistrationResult GetActiveRegistration(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            int userId)
        {
            const string sql = @"
SELECT TR.PlayerId, P.Name AS PlayerName, RM.Name AS TournamentName
FROM TournamentRegistration TR WITH (UPDLOCK, HOLDLOCK)
INNER JOIN Tournament T ON T.Id = TR.TournamentId
INNER JOIN [Match] RM ON RM.Id = T.MatchId
LEFT JOIN Player P ON P.Id = TR.PlayerId
WHERE TR.TournamentId = @TournamentId
  AND TR.RegisteredUserId = @UserId
  AND TR.Status = 'registered';";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    return new TournamentRegistrationResult
                    {
                        TournamentId = tournamentId,
                        TournamentName = Convert.ToString(reader["TournamentName"]),
                        PlayerId = reader["PlayerId"] == DBNull.Value ? -1 : Convert.ToInt32(reader["PlayerId"]),
                        PlayerName = reader["PlayerName"] == DBNull.Value ? null : Convert.ToString(reader["PlayerName"]),
                        AlreadyRegistered = true
                    };
                }
            }
        }

        private static void FindFreePlayerSlot(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            out int playerId,
            out string originalPlayerName)
        {
            const string sql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId
FROM Tournament
WHERE Id = @TournamentId;

;WITH TournamentTree AS
(
    SELECT
        M.Id AS MatchId,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @RootMatchId

    UNION ALL

    SELECT
        M.Id,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> @RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT TOP 1 P.Id, P.Name
FROM Player P WITH (UPDLOCK, HOLDLOCK)
WHERE P.RegisteredUserID IS NULL
  AND P.Name LIKE 'Player:%'
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
      FROM TournamentTree TT
      INNER JOIN Seat S ON S.MatchId = TT.MatchId
      WHERE S.PlayerId = P.Id
        AND (S.ResultMatchPlace IS NOT NULL OR S.ResultPoints IS NOT NULL)
  )
ORDER BY P.Id
OPTION (MAXRECURSION 1000);";

            playerId = -1;
            originalPlayerName = null;

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        playerId = Convert.ToInt32(reader["Id"]);
                        originalPlayerName = Convert.ToString(reader["Name"]);
                    }
                }
            }
        }

        private static bool PlayerHasRecordedResults(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            int playerId)
        {
            const string sql = @"
DECLARE @RootMatchId INT;
SELECT @RootMatchId = MatchId
FROM Tournament
WHERE Id = @TournamentId;

;WITH TournamentTree AS
(
    SELECT
        M.Id AS MatchId,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @RootMatchId

    UNION ALL

    SELECT
        M.Id,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM TournamentTree TT
    INNER JOIN [Match] M ON M.ParentMatchId = TT.MatchId
    WHERE M.Id <> @RootMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT COUNT(1)
FROM TournamentTree TT
INNER JOIN Seat S ON S.MatchId = TT.MatchId
WHERE S.PlayerId = @PlayerId
  AND (S.ResultMatchPlace IS NOT NULL OR S.ResultPoints IS NOT NULL)
OPTION (MAXRECURSION 1000);";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@PlayerId", SqlDbType.Int).Value = playerId;
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
