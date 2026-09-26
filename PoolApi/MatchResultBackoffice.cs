using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
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
        public List<MatchSeatDetails> Seats { get; set; } = new List<MatchSeatDetails>();
    }

    public class MatchResultUpdate
    {
        public int SeatId { get; set; }
        public int ResultMatchPlace { get; set; }
        public int? ResultPoints { get; set; }
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
    S.Id AS SeatId,
    S.PlayerId,
    P.Name AS PlayerName,
    S.ResultMatchPlace,
    S.ResultPoints
FROM [Match] M
LEFT JOIN MatchRules MR ON M.MatchRulesId = MR.Id
LEFT JOIN PlayStyle PS ON MR.PlayStyleId = PS.Id
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

        public void SaveMatchResults(int matchId, IList<MatchResultUpdate> results)
        {
            if (results == null || results.Count == 0)
            {
                throw new ArgumentException("No results were supplied.");
            }

            const string sql = @"
UPDATE Seat
SET ResultMatchPlace = @ResultMatchPlace,
    ResultPoints = @ResultPoints
WHERE Id = @SeatId
  AND MatchId = @MatchId;";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            {
                conn.Open();
                using (SqlTransaction transaction = conn.BeginTransaction())
                {
                    try
                    {
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

        public void ClearMatchResults(int matchId)
        {
            const string sql = @"
UPDATE Seat
SET ResultMatchPlace = NULL,
    ResultPoints = NULL
WHERE MatchId = @MatchId;";

            using (SqlConnection conn = new SqlConnection(_configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add("@MatchId", SqlDbType.Int).Value = matchId;
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static int? ToNullableInt(object value)
        {
            return value == null || value == DBNull.Value ? (int?)null : Convert.ToInt32(value);
        }

        private static string ToNullableString(object value)
        {
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
    }
}
