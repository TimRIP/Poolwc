using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace DrukDatabaseLayer
{
    public class VenueDetails
    {
        public int FacilityId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class PoolScheduleAssignment
    {
        public int PoolMatchId { get; set; }
        public int? ScheduleId { get; set; }
        public int? FacilityId { get; set; }
        public string VenueName { get; set; }
        public string VenueDescription { get; set; }
        public DateTime? FromTime { get; set; }
        public DateTime? ToTime { get; set; }
    }

    public class VenueBackoffice
    {
        private readonly IConfigurationRoot _configuration;

        public VenueBackoffice()
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

        public List<VenueDetails> GetVenuesForTournament(int tournamentId, int userId)
        {
            EnsureTournamentAdmin(tournamentId, userId);

            const string sql = @"
SELECT Id, Name, Description
FROM Facility
WHERE AdminId = @UserId
ORDER BY Name, Id;";

            var venues = new List<VenueDetails>();

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        venues.Add(new VenueDetails
                        {
                            FacilityId = Convert.ToInt32(reader["Id"]),
                            Name = reader["Name"] == DBNull.Value ? null : Convert.ToString(reader["Name"]),
                            Description = reader["Description"] == DBNull.Value ? null : Convert.ToString(reader["Description"])
                        });
                    }
                }
            }

            return venues;
        }

        // Schedule information is readable by any authenticated user who can load the tournament.
        // Creating/updating schedules is still restricted to the tournament administrator.
        public List<PoolScheduleAssignment> GetPoolSchedulesForTournament(int tournamentId)
        {
            const string sql = @"
DECLARE @TournamentMatchId INT;
SELECT @TournamentMatchId = MatchId
FROM Tournament
WHERE Id = @TournamentId;

;WITH TournamentTree AS
(
    SELECT
        0 AS [Level],
        M.Id,
        M.ParentMatchId,
        M.IndividualMatch,
        M.ScheduleId,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @TournamentMatchId

    UNION ALL

    SELECT
        TT.[Level] + 1,
        M.Id,
        M.ParentMatchId,
        M.IndividualMatch,
        M.ScheduleId,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM [Match] M
    INNER JOIN TournamentTree TT ON M.ParentMatchId = TT.Id
    WHERE M.Id <> @TournamentMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT
    TT.Id AS PoolMatchId,
    S.Id AS ScheduleId,
    F.Id AS FacilityId,
    F.Name AS VenueName,
    F.Description AS VenueDescription,
    S.FromTime,
    S.ToTime
FROM TournamentTree TT
LEFT JOIN Schedule S ON TT.ScheduleId = S.Id
LEFT JOIN Facility F ON S.FacilityId = F.Id
WHERE TT.IndividualMatch = 0
  AND EXISTS
  (
      SELECT 1
      FROM [Match] ChildMatch
      WHERE ChildMatch.ParentMatchId = TT.Id
        AND ChildMatch.IndividualMatch = 1
  )
ORDER BY TT.Id
OPTION (MAXRECURSION 1000);";

            var result = new List<PoolScheduleAssignment>();

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                conn.Open();

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(ReadPoolSchedule(reader));
                    }
                }
            }

            return result;
        }

        public VenueDetails CreateVenue(int tournamentId, int userId, string name, string description)
        {
            EnsureTournamentAdmin(tournamentId, userId);

            name = (name ?? string.Empty).Trim();
            description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

            if (name.Length == 0)
            {
                throw new ArgumentException("Venue name is required.");
            }

            if (name.Length > 255)
            {
                throw new ArgumentException("Venue name can be at most 255 characters.");
            }

            if (description != null && description.Length > 255)
            {
                throw new ArgumentException("Venue description can be at most 255 characters.");
            }

            const string sql = @"
INSERT INTO Facility (Name, Description, AdminId)
VALUES (@Name, @Description, @UserId);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@Name", SqlDbType.NVarChar, 255).Value = name;
                cmd.Parameters.Add("@Description", SqlDbType.NVarChar, 255).Value = (object)description ?? DBNull.Value;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                conn.Open();

                int id = Convert.ToInt32(cmd.ExecuteScalar());
                return new VenueDetails
                {
                    FacilityId = id,
                    Name = name,
                    Description = description
                };
            }
        }

        public void DeleteVenue(int tournamentId, int userId, int facilityId)
        {
            EnsureTournamentAdmin(tournamentId, userId);

            const string countSql = @"
SELECT COUNT(1)
FROM Schedule S
INNER JOIN Facility F ON F.Id = S.FacilityId
WHERE F.Id = @FacilityId
  AND F.AdminId = @UserId;";

            const string deleteSql = @"
DELETE FROM Facility
WHERE Id = @FacilityId
  AND AdminId = @UserId;";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();

                using (SqlCommand count = new SqlCommand(countSql, conn))
                {
                    count.Parameters.Add("@FacilityId", SqlDbType.Int).Value = facilityId;
                    count.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                    if (Convert.ToInt32(count.ExecuteScalar()) > 0)
                    {
                        throw new InvalidOperationException("This venue is currently assigned to one or more pool schedules. Remove those assignments before deleting it.");
                    }
                }

                using (SqlCommand delete = new SqlCommand(deleteSql, conn))
                {
                    delete.Parameters.Add("@FacilityId", SqlDbType.Int).Value = facilityId;
                    delete.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                    if (delete.ExecuteNonQuery() == 0)
                    {
                        throw new InvalidOperationException("The venue was not found or does not belong to this administrator.");
                    }
                }
            }
        }

        // Backwards-compatible helper for the previous UI. It changes only the venue
        // and leaves any existing FromTime / ToTime values untouched.
        public PoolScheduleAssignment AssignVenueToPool(int tournamentId, int userId, int poolMatchId, int? facilityId)
        {
            EnsureTournamentAdmin(tournamentId, userId);

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        ValidatePoolAndVenue(conn, transaction, tournamentId, userId, poolMatchId, facilityId);

                        int? scheduleId = GetScheduleId(conn, transaction, poolMatchId);
                        if (!scheduleId.HasValue)
                        {
                            if (facilityId.HasValue)
                            {
                                scheduleId = CreateSchedule(conn, transaction, facilityId, null, null);
                                SetMatchSchedule(conn, transaction, poolMatchId, scheduleId);
                            }
                        }
                        else
                        {
                            SetScheduleFacility(conn, transaction, scheduleId.Value, facilityId);
                        }

                        PoolScheduleAssignment result = GetPoolScheduleAssignment(conn, transaction, poolMatchId);
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

        public PoolScheduleAssignment UpdatePoolSchedule(
            int tournamentId,
            int userId,
            int poolMatchId,
            int? facilityId,
            DateTime? fromTime)
        {
            EnsureTournamentAdmin(tournamentId, userId);

            // A pool schedule has only a start time. Schedule uses DATETIME in the
            // existing database, so normalize the incoming time to a safe fixed day.
            // ToTime is deliberately kept NULL for compatibility with the existing table.
            fromTime = NormalizeScheduleTime(fromTime);

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
                        ValidatePoolAndVenue(conn, transaction, tournamentId, userId, poolMatchId, facilityId);

                        int? scheduleId = GetScheduleId(conn, transaction, poolMatchId);
                        bool hasScheduleValues = facilityId.HasValue || fromTime.HasValue;

                        if (!scheduleId.HasValue && hasScheduleValues)
                        {
                            scheduleId = CreateSchedule(conn, transaction, facilityId, fromTime, null);
                            SetMatchSchedule(conn, transaction, poolMatchId, scheduleId);
                        }
                        else if (scheduleId.HasValue && hasScheduleValues)
                        {
                            UpdateSchedule(conn, transaction, scheduleId.Value, facilityId, fromTime, null);
                        }
                        else if (scheduleId.HasValue && !hasScheduleValues)
                        {
                            int idToDelete = scheduleId.Value;
                            SetMatchSchedule(conn, transaction, poolMatchId, null);
                            DeleteScheduleIfUnused(conn, transaction, idToDelete);
                        }

                        PoolScheduleAssignment result = GetPoolScheduleAssignment(conn, transaction, poolMatchId);
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

        private static DateTime? NormalizeScheduleTime(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            DateTime baseDate = new DateTime(2000, 1, 1);
            return baseDate.Add(value.Value.TimeOfDay);
        }

        private void EnsureTournamentAdmin(int tournamentId, int userId)
        {
            if (!IsTournamentAdmin(tournamentId, userId))
            {
                throw new UnauthorizedAccessException("Only the tournament administrator can manage venues and schedules.");
            }
        }

        private static void ValidatePoolAndVenue(
            SqlConnection conn,
            SqlTransaction transaction,
            int tournamentId,
            int userId,
            int poolMatchId,
            int? facilityId)
        {
            if (!IsPoolInTournament(conn, transaction, tournamentId, poolMatchId))
            {
                throw new InvalidOperationException("The selected match is not a pool in this tournament.");
            }

            if (facilityId.HasValue && !VenueBelongsToUser(conn, transaction, facilityId.Value, userId))
            {
                throw new InvalidOperationException("The selected venue does not belong to this tournament administrator.");
            }
        }

        private static bool IsPoolInTournament(SqlConnection conn, SqlTransaction transaction, int tournamentId, int poolMatchId)
        {
            const string sql = @"
DECLARE @TournamentMatchId INT;
SELECT @TournamentMatchId = MatchId
FROM Tournament
WHERE Id = @TournamentId;

;WITH TournamentTree AS
(
    SELECT
        0 AS [Level],
        M.Id,
        M.ParentMatchId,
        M.IndividualMatch,
        CAST('|' + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX)) AS [Path]
    FROM [Match] M
    WHERE M.Id = @TournamentMatchId

    UNION ALL

    SELECT
        TT.[Level] + 1,
        M.Id,
        M.ParentMatchId,
        M.IndividualMatch,
        CAST(TT.[Path] + CAST(M.Id AS VARCHAR(20)) + '|' AS VARCHAR(MAX))
    FROM [Match] M
    INNER JOIN TournamentTree TT ON M.ParentMatchId = TT.Id
    WHERE M.Id <> @TournamentMatchId
      AND CHARINDEX('|' + CAST(M.Id AS VARCHAR(20)) + '|', TT.[Path]) = 0
)
SELECT COUNT(1)
FROM TournamentTree TT
WHERE TT.Id = @PoolMatchId
  AND TT.IndividualMatch = 0
  AND EXISTS
  (
      SELECT 1
      FROM [Match] ChildMatch
      WHERE ChildMatch.ParentMatchId = TT.Id
        AND ChildMatch.IndividualMatch = 1
  )
OPTION (MAXRECURSION 1000);";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@TournamentId", SqlDbType.Int).Value = tournamentId;
                cmd.Parameters.Add("@PoolMatchId", SqlDbType.Int).Value = poolMatchId;
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static bool VenueBelongsToUser(SqlConnection conn, SqlTransaction transaction, int facilityId, int userId)
        {
            const string sql = @"
SELECT COUNT(1)
FROM Facility
WHERE Id = @FacilityId
  AND AdminId = @UserId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@FacilityId", SqlDbType.Int).Value = facilityId;
                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = userId;
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private static int? GetScheduleId(SqlConnection conn, SqlTransaction transaction, int poolMatchId)
        {
            const string sql = "SELECT ScheduleId FROM [Match] WHERE Id = @MatchId;";
            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = poolMatchId;
                object value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value) return null;
                return Convert.ToInt32(value);
            }
        }

        private static int CreateSchedule(
            SqlConnection conn,
            SqlTransaction transaction,
            int? facilityId,
            DateTime? fromTime,
            DateTime? toTime)
        {
            const string sql = @"
INSERT INTO Schedule (FacilityId, FromTime, ToTime)
VALUES (@FacilityId, @FromTime, @ToTime);
SELECT CAST(SCOPE_IDENTITY() AS INT);";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@FacilityId", SqlDbType.Int).Value = (object)facilityId ?? DBNull.Value;
                cmd.Parameters.Add("@FromTime", SqlDbType.DateTime).Value = (object)fromTime ?? DBNull.Value;
                cmd.Parameters.Add("@ToTime", SqlDbType.DateTime).Value = (object)toTime ?? DBNull.Value;
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static void SetMatchSchedule(SqlConnection conn, SqlTransaction transaction, int poolMatchId, int? scheduleId)
        {
            const string sql = @"
UPDATE [Match]
SET ScheduleId = @ScheduleId
WHERE Id = @MatchId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@ScheduleId", SqlDbType.Int).Value = (object)scheduleId ?? DBNull.Value;
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = poolMatchId;
                cmd.ExecuteNonQuery();
            }
        }

        private static void SetScheduleFacility(SqlConnection conn, SqlTransaction transaction, int scheduleId, int? facilityId)
        {
            const string sql = @"
UPDATE Schedule
SET FacilityId = @FacilityId
WHERE Id = @ScheduleId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@FacilityId", SqlDbType.Int).Value = (object)facilityId ?? DBNull.Value;
                cmd.Parameters.Add("@ScheduleId", SqlDbType.Int).Value = scheduleId;
                cmd.ExecuteNonQuery();
            }
        }

        private static void UpdateSchedule(
            SqlConnection conn,
            SqlTransaction transaction,
            int scheduleId,
            int? facilityId,
            DateTime? fromTime,
            DateTime? toTime)
        {
            const string sql = @"
UPDATE Schedule
SET FacilityId = @FacilityId,
    FromTime = @FromTime,
    ToTime = @ToTime
WHERE Id = @ScheduleId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@FacilityId", SqlDbType.Int).Value = (object)facilityId ?? DBNull.Value;
                cmd.Parameters.Add("@FromTime", SqlDbType.DateTime).Value = (object)fromTime ?? DBNull.Value;
                cmd.Parameters.Add("@ToTime", SqlDbType.DateTime).Value = (object)toTime ?? DBNull.Value;
                cmd.Parameters.Add("@ScheduleId", SqlDbType.Int).Value = scheduleId;
                cmd.ExecuteNonQuery();
            }
        }

        private static void DeleteScheduleIfUnused(SqlConnection conn, SqlTransaction transaction, int scheduleId)
        {
            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM [Match] WHERE ScheduleId = @ScheduleId)
BEGIN
    DELETE FROM Schedule WHERE Id = @ScheduleId;
END";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@ScheduleId", SqlDbType.Int).Value = scheduleId;
                cmd.ExecuteNonQuery();
            }
        }

        private static PoolScheduleAssignment GetPoolScheduleAssignment(SqlConnection conn, SqlTransaction transaction, int poolMatchId)
        {
            const string sql = @"
SELECT
    M.Id AS PoolMatchId,
    S.Id AS ScheduleId,
    F.Id AS FacilityId,
    F.Name AS VenueName,
    F.Description AS VenueDescription,
    S.FromTime,
    S.ToTime
FROM [Match] M
LEFT JOIN Schedule S ON M.ScheduleId = S.Id
LEFT JOIN Facility F ON S.FacilityId = F.Id
WHERE M.Id = @PoolMatchId;";

            using (SqlCommand cmd = new SqlCommand(sql, conn, transaction))
            {
                cmd.Parameters.Add("@PoolMatchId", SqlDbType.Int).Value = poolMatchId;
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return new PoolScheduleAssignment { PoolMatchId = poolMatchId };
                    }

                    return ReadPoolSchedule(reader);
                }
            }
        }

        private static PoolScheduleAssignment ReadPoolSchedule(SqlDataReader reader)
        {
            return new PoolScheduleAssignment
            {
                PoolMatchId = Convert.ToInt32(reader["PoolMatchId"]),
                ScheduleId = reader["ScheduleId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["ScheduleId"]),
                FacilityId = reader["FacilityId"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["FacilityId"]),
                VenueName = reader["VenueName"] == DBNull.Value ? null : Convert.ToString(reader["VenueName"]),
                VenueDescription = reader["VenueDescription"] == DBNull.Value ? null : Convert.ToString(reader["VenueDescription"]),
                FromTime = reader["FromTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["FromTime"]),
                ToTime = reader["ToTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["ToTime"])
            };
        }
    }
}
