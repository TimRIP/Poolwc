using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;

namespace DrukDatabaseLayer
{
    public class UserBackoffice
    {
        public static IConfigurationRoot Configuration { get; set; }

        public UserBackoffice()
        {
            var builder = new ConfigurationBuilder()
                //.SetBasePath(Directory.GetCurrentDirectory())
                .SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            Configuration = builder.Build();
        }

        public void UserLog(string logtext, string Token)
        {
            //Bearer Token
            string newToken = Token.Remove(0, 7);

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_UserLog", conn))
            {

                SqlParameter parm = new SqlParameter("@logtext", SqlDbType.VarChar);
                parm.Value = logtext;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@token", SqlDbType.VarChar);
                parm2.Value = newToken;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                cmd.CommandType = CommandType.StoredProcedure;

                conn.Open();

                cmd.ExecuteNonQuery();

                conn.Close();
            }
        }
        public void SaveToken(int UserID, string Token)
        {
            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_SaveToken", conn))
            {

                SqlParameter parm = new SqlParameter("@userID", SqlDbType.Int);
                parm.Value = UserID;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@token", SqlDbType.VarChar);
                parm2.Value = Token;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                cmd.CommandType = CommandType.StoredProcedure;

                conn.Open();

                cmd.ExecuteNonQuery();

                conn.Close();
            }
        }
        public int GetUserIdFromCredentials(string UserName, string PassWord)
        {
            int UserID = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_LoginUser", conn))
            {

                SqlParameter parm = new SqlParameter("@pUserName", SqlDbType.VarChar);
                parm.Value = UserName;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pPassword", SqlDbType.VarChar);
                parm2.Value = PassWord;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter outputresponseMessageParam = new SqlParameter("@responseMessage", SqlDbType.NVarChar, 250)
                {
                    Direction = ParameterDirection.Output
                };

                SqlParameter outputIdParam = new SqlParameter("@userID", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputresponseMessageParam);
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                UserID = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return UserID;
        }

        public int SP_CreateMatch(string Name, int? ParentMachId, int? MatchRulesId, bool Induvidual)
        {
            int MatchID = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_CreateMatch", conn))
            {

                SqlParameter parm = new SqlParameter("@pName", SqlDbType.VarChar);
                parm.Value = Name;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pParentMachId", SqlDbType.Int);
                parm2.Value = ParentMachId;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter parm3 = new SqlParameter("@pMatchRulesId", SqlDbType.Int);
                parm3.Value = MatchRulesId;
                parm3.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm3);

                SqlParameter parm4 = new SqlParameter("@pInduvidual", SqlDbType.Bit);
                parm4.Value = Induvidual;
                parm4.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm4);


                SqlParameter outputIdParam = new SqlParameter("@MatchId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };

                SqlParameter outputresponseMessageParam = new SqlParameter("@responseMessage", SqlDbType.NVarChar, 250)
                {
                    Direction = ParameterDirection.Output
                };



                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputresponseMessageParam);
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                MatchID = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return MatchID;
        }

        public int SP_SetParentMatchId(int MatchId, int ParentMachId)
        {
            int rtn = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_SetParentMatchId", conn))
            {
                

                SqlParameter parm = new SqlParameter("@pMatchId", SqlDbType.Int);
                parm.Value = MatchId;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pParentMachId", SqlDbType.Int);
                parm2.Value = ParentMachId;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter outputIdParam = new SqlParameter("@Result", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };


                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                rtn = outputIdParam.Value as int? ?? -1;
                
                conn.Close();
            }
            return rtn;
        }

        public int SP_CreatePlayer(int? RegisteredUserID, string Name, int? ParentPlayerId)
        {
            int PlayerID = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_CreatePlayer", conn))
            {

                SqlParameter parm = new SqlParameter("@pRegisteredUserID", SqlDbType.Int);
                parm.Value = RegisteredUserID;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pName", SqlDbType.VarChar);
                parm2.Value = Name;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter parm3 = new SqlParameter("@pParentPlayerId", SqlDbType.Int);
                parm3.Value = ParentPlayerId;
                parm3.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm3);


                SqlParameter outputIdParam = new SqlParameter("@PlayerId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };

                SqlParameter outputresponseMessageParam = new SqlParameter("@responseMessage", SqlDbType.NVarChar, 250)
                {
                    Direction = ParameterDirection.Output
                };



                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputresponseMessageParam);
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                PlayerID = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return PlayerID;
        }

        #nullable enable
        public int SP_CreateSeat(int? ParentSeatId, int? PlayerId, int MtchId, int? AutoSelectMatchId, int? AutoSelectPlace)
        {
            int SeatID = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_CreateSeat", conn))
            {

                SqlParameter parm = new SqlParameter("@pParentSeatId", SqlDbType.Int);
                parm.Value = ParentSeatId;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pPlayerId", SqlDbType.Int);
                parm2.Value = PlayerId;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter parm3 = new SqlParameter("@pMatchId", SqlDbType.Int);
                parm3.Value = MtchId;
                parm3.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm3);

                SqlParameter parm4 = new SqlParameter("@pAutoSelectMatchId", SqlDbType.Int);
                parm4.Value = AutoSelectMatchId;
                parm4.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm4);

                SqlParameter parm5 = new SqlParameter("@pAutoSelectPlace", SqlDbType.Int);
                parm5.Value = AutoSelectPlace;
                parm5.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm5);

                SqlParameter outputIdParam = new SqlParameter("@SeatId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };

                SqlParameter outputresponseMessageParam = new SqlParameter("@responseMessage", SqlDbType.NVarChar, 250)
                {
                    Direction = ParameterDirection.Output
                };



                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputresponseMessageParam);
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                SeatID = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return SeatID;
        }

        #nullable enable
        public int SP_CreateTournament(int MatchId, int AdminId, bool Default, string? Description, string Name, string? FormData)
        {
            int TournamentID = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_CreateTournament", conn))
            {

                SqlParameter parm = new SqlParameter("@pMatchId", SqlDbType.Int);
                parm.Value = MatchId;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pAdmin", SqlDbType.Int);
                parm2.Value = AdminId;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter parm3 = new SqlParameter("@pDefault", SqlDbType.Bit);
                parm3.Value = Default;
                parm3.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm3);

                SqlParameter parm4 = new SqlParameter("@pDescription", SqlDbType.VarChar);
                parm4.Value = Description;
                parm4.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm4);

                SqlParameter parm5 = new SqlParameter("@pName", SqlDbType.VarChar);
                parm5.Value = Name;
                parm5.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm5);

                SqlParameter parm6 = new SqlParameter("@pFormData", SqlDbType.VarChar);
                parm6.Value = FormData;
                parm6.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm6);

                SqlParameter outputIdParam = new SqlParameter("@TurnamentId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };

                SqlParameter outputresponseMessageParam = new SqlParameter("@responseMessage", SqlDbType.NVarChar, 250)
                {
                    Direction = ParameterDirection.Output
                };

                

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputresponseMessageParam);
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                TournamentID = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return TournamentID;
        }

        public int SP_SetSeatAutoPlace(int SeatId, int AutoSelectMatchId, int AutoSelectlPlace)
        {
            int rtn = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_SetSeatAutoPlace", conn))
            {


                SqlParameter parm = new SqlParameter("@pSeatId", SqlDbType.Int);
                parm.Value = SeatId;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pAutoSelectMatchId", SqlDbType.Int);
                parm2.Value = AutoSelectMatchId;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter parm3 = new SqlParameter("@pAutoSelectlPlace", SqlDbType.Int);
                parm3.Value = AutoSelectlPlace;
                parm3.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm3);

                SqlParameter outputIdParam = new SqlParameter("@Result", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };


                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                rtn = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return rtn;
        }

        public int SP_SetPlayerOnSeat(int SeatId, int PlayerId)
        {
            int rtn = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_SetPlayerOnSeat", conn))
            {


                SqlParameter parm = new SqlParameter("@pSeatId", SqlDbType.Int);
                parm.Value = SeatId;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pPlayerId", SqlDbType.Int);
                parm2.Value = PlayerId;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter outputIdParam = new SqlParameter("@Result", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };


                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                rtn = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return rtn;
        }

        public int SP_GetUserFromToken(string token)
        {
            int userid = -1;

            string newToken = token.Remove(0, 7);

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_GetUserFromToken", conn))
            {


                SqlParameter parm = new SqlParameter("@token", SqlDbType.VarChar);
                parm.Value = newToken;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter outputIdParam = new SqlParameter("@UserId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };


                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                userid = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return userid;
        }

        public int SP_CreateMatchRule(int PlayStyleId, int PlayFrom, int PlayTo, string? Description)
        {
            int MatchRulesID = -1;

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_CreateMatchRule", conn))
            {

                SqlParameter parm = new SqlParameter("@pPlayStyleId", SqlDbType.Int);
                parm.Value = PlayStyleId;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);

                SqlParameter parm2 = new SqlParameter("@pPlayFrom", SqlDbType.Int);
                parm2.Value = PlayFrom;
                parm2.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm2);

                SqlParameter parm3 = new SqlParameter("@pPlayTo", SqlDbType.Int);
                parm3.Value = PlayTo;
                parm3.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm3);

                SqlParameter parm4 = new SqlParameter("@pDescription", SqlDbType.VarChar);
                parm4.Value = Description;
                parm4.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm4);

                SqlParameter outputIdParam = new SqlParameter("@MatchRuleId", SqlDbType.Int)
                {
                    Direction = ParameterDirection.Output
                };

                SqlParameter outputresponseMessageParam = new SqlParameter("@responseMessage", SqlDbType.NVarChar, 250)
                {
                    Direction = ParameterDirection.Output
                };



                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(outputresponseMessageParam);
                cmd.Parameters.Add(outputIdParam);

                conn.Open();

                cmd.ExecuteNonQuery();

                MatchRulesID = outputIdParam.Value as int? ?? -1;

                conn.Close();
            }
            return MatchRulesID;
        }

        public string SP_GetMatchesFromTournament(int tournamentId)
        {

            ArrayList objs = new ArrayList();

            using (SqlConnection conn = new SqlConnection(Configuration["connectionstring"]))
            using (SqlCommand cmd = new SqlCommand("SP_GetMatchesFromTournament", conn))
            {


                SqlParameter parm = new SqlParameter("@pTournamentId", SqlDbType.Int);
                parm.Value = tournamentId;
                parm.Direction = ParameterDirection.Input;
                cmd.Parameters.Add(parm);


                cmd.CommandType = CommandType.StoredProcedure;

                conn.Open();

                SqlDataReader reader = cmd.ExecuteReader();
                
                while (reader.Read())
                {
                    //if ((bool)reader["IndividualMatch"])
                    {
                        objs.Add(new
                        {
                            Id = reader["Id"],
                            Name = reader["Name"],
                        });
                    }
                }

                conn.Close();

                //objs.Reverse();

            }
            return JsonConvert.SerializeObject(objs);
        }

    }
}
