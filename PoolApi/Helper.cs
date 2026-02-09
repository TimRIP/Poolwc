using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DrukDatabaseLayer;

namespace TournamentBackend
{
    public class SavedVariabels
    {
        public int BeenInPuljeMatchId { get; set; }

        public int ParentPuljeMatchId { get; set; }
        public int Place { get; set; }
        public int ParentPlace { get; set; }
        public int Player { get; set; }
        public int Runde { get; set; }

        public bool used { get; set; } = false;

        public bool found { get; set; } = false;

        public override string ToString()
        {
            string str = "Runde:" + Runde + " BeenInPuljeMatchId: " + BeenInPuljeMatchId + " ParentPuljeMatchId:" + ParentPuljeMatchId + " Place:" + Place + " Player:" + Player + "Used:" + used;
            return str;
        }

    }
    public class Tournament
    {
        public class Player
        {
            public int PlayerId { get; set; }
        }
        public class Seat
        {
            public int SeatId { get; set; }
            public int ParentSeatId { get; set; }
            public Player Player;

            public int AutoSelectMatchId { get; set; }
            public int AutoSelectlPlace { get; set; }

            public override string ToString()
            {
                string str = "SeatId:" + SeatId + " ParentSeatId:" + ParentSeatId;
                return str;
            }
        }
        public class Match
        {
            public string name { get; set; }
            public int MatchId { get; set; }
            public int ParentMatchId { get; set; }
            public bool Individual { get; set; }

            public List<Seat> seats = new List<Seat>();

            public override string ToString()
            {
                string str = "Name:" + name + " Id:" + MatchId + " ParentMatchId:" + ParentMatchId + " Individual:" + Individual + "\r\n";
                if (seats != null)
                {
                    foreach (Seat item in seats)
                    {
                        str = str + "\t\t\t\t" + item.ToString() + "\r\n";
                    }
                }
                return str;
            }
        }
        public class Pulje
        {
            public Match PuljeMatch;
            public List<Match> matches = new List<Match>();

            public override string ToString()
            {
                string str = "Pulje:" + PuljeMatch.ToString();
                if (matches != null)
                {
                    foreach (Match item in matches)
                    {
                        str = str + "\t\t\t" + item.ToString();
                    }
                }
                return str;
            }
        }
        public class Runde
        {
            public Match RundeMatch;
            public string name;
            public int NbPlayers;
            public int puljesize;
            public string playstyle;
            public int BestOf;
            public List<int> whereto = new List<int>();
            public List<Pulje> puljer = new List<Pulje>();

            public override string ToString()
            {
                string str = "Runde:(" + name + ") NbPlayers:" + NbPlayers + " puljesize:" + puljesize + " playstyle:" + playstyle + " BestOf:" + BestOf + "\r\n";

                if (whereto != null)
                {
                    str = str + "\twherto: ";
                    foreach (int to in whereto)
                    {
                        str = str + to + " ";
                    }
                    str = str + "\r\n";
                }

                str = str + "\t" + RundeMatch.ToString();
                if (puljer != null)
                {
                    foreach (Pulje item in puljer)
                    {
                        str = str + "\t\t" + item.ToString();
                    }
                }
                return str;
            }


        }
        public string name { get; set; }
        public Match TournamentMatch { get; set; }

        public List<Runde> runder = new List<Runde>();

        public override string ToString()
        {
            string run = "Tournament(" + name + ") \r\n";
            run = run + TournamentMatch.ToString();
            if (runder != null)
            {
                foreach (Runde item in runder)
                {
                    run = run + "\t" + item.ToString();
                }
            }
            return run;
        }
    }
    public class Helper
    {
        public static List<SavedVariabels> GetPosibleVars(Tournament turn, List<SavedVariabels> oldsaved, int rundeindex)
        {
            int myplayer = 1;
            List<SavedVariabels> newsaved = new List<SavedVariabels>();
            for (int i = rundeindex - 1; i >= 0; i--)
            {
                for (int j = 0; j < turn.runder[i].whereto.Count; j++)
                {
                    SavedVariabels found = new SavedVariabels();
                    if (turn.runder[i].whereto[j] == rundeindex)
                    {
                        for (int k = 0; k < oldsaved.Count; k++)
                        {
                            if (oldsaved[k].Place == (j + 1) && oldsaved[k].Runde == i && oldsaved[k].used == false)
                            {
                                oldsaved[k].used = true;
                                found = oldsaved[k];
                                SavedVariabels entry = new SavedVariabels() { ParentPuljeMatchId = found.BeenInPuljeMatchId, Runde = rundeindex, Player = myplayer, ParentPlace = (j + 1) };
                                newsaved.Add(entry);
                                myplayer++;

                            }
                        }
                    }
                }

            }
            return newsaved;
        }

        public static SavedVariabels FirstNotInCurrent(List<SavedVariabels> saved, List<SavedVariabels> current)
        {
            SavedVariabels found = new SavedVariabels();
            //select first not found
            foreach (SavedVariabels first in saved)
            {
                if (!first.found)
                {
                    found = first;
                    break;
                }
            }
            //we check if last match conflicted with last match

            foreach (SavedVariabels item in saved)
            {
                bool conflict = false;

                if (!item.found)
                {
                    foreach (SavedVariabels olditem in current)
                    {
                        if (item.ParentPuljeMatchId == olditem.ParentPuljeMatchId)
                        {
                            //here is a conflict
                            conflict = true;
                        }
                    }
                    if (!conflict)
                    {
                        found = item;
                    }
                }
            }
            found.found = true;
            return found;
        }
        public static void SetPlayersInPulje(Tournament turn, List<SavedVariabels> newsaved, List<SavedVariabels> oldsaved, int rundeindex)
        {
            UserBackoffice ubo = new UserBackoffice();


            int index = 0;
            int puljeindex = 0;

            SavedVariabels firstcur = new SavedVariabels();

            foreach (Tournament.Pulje item in turn.runder[rundeindex].puljer)
            {
                List<SavedVariabels> current = new List<SavedVariabels>();

                foreach (SavedVariabels getfirst in newsaved)
                {
                    if (!getfirst.found)
                    {
                        firstcur = getfirst;
                        break;
                    }
                }
                firstcur.found = true;
                current.Add(firstcur);

                int nbseat = 1;
                foreach (Tournament.Seat seat in item.PuljeMatch.seats)
                {
                    //Here we Add newsaved to current where last does not exist in earlyer puljer
                    if (index > 0)
                    {
                        //Find first newsaved not in current
                        SavedVariabels firstsaved = FirstNotInCurrent(newsaved, current);
                        current.Add(firstsaved);
                    }


                    current[index].BeenInPuljeMatchId = item.PuljeMatch.MatchId;
                    ubo.SP_SetSeatAutoPlace(seat.SeatId, current[index].ParentPuljeMatchId, current[index].ParentPlace);
                    current[index].Place = nbseat;
                    index++;
                    nbseat++;
                }
                index = 0;
                puljeindex++;
            }

        }
        public int CreateTournamentFromString(string jsontext)
        {
            UserBackoffice ubo = new UserBackoffice();
            //This method returns user id from username and password.
            int userId = ubo.GetUserIdFromCredentials("timrip", "ug2-gj-8");
            //Console.WriteLine("Your UserID = "+userId);

            //string jsontext = "{\"errors\":[],\"tournamentname\":\"new tournament\",\"tournamentplayers\":8,\"rundearray\":[{\"navn\":\"indledende runde\",\"NbPlayers\":8,\"selected\":\"pool\",\"BestOf\":2,\"puljesize\":4,\"valsArray\":[2,2,1,1],\"playstyle\":\"roundrobin\"},{\"navn\":\"opsamling\",\"NbPlayers\":4,\"selected\":\"pool\",\"BestOf\":1,\"puljesize\":4,\"valsArray\":[2,2,-1,-1],\"playstyle\":\"roundrobin\"},{\"navn\":\"semifinale\",\"NbPlayers\":6,\"selected\":\"knockout\",\"BestOf\":1,\"puljesize\":2,\"valsArray\":[3,-1],\"playstyle\":\"roundrobin\"},{\"navn\":\"finale\",\"NbPlayers\":3,\"selected\":\"pool\",\"BestOf\":1,\"puljesize\":3,\"valsArray\":[\"0\",-1,-1],\"playstyle\":\"roundrobin\"}]}";
            JObject json = JObject.Parse(jsontext);



            int TournamentMatchId = ubo.SP_CreateMatch(json["tournamentname"].ToString(), null, null, false);

            Tournament turn = new Tournament() { name = json["tournamentname"].ToString(), TournamentMatch = new Tournament.Match() { MatchId = TournamentMatchId, Individual = false, name = json["tournamentname"].ToString() } };

            int LastMatchId = TournamentMatchId;

            JArray runder = (JArray)json["rundearray"];
            foreach (JObject item in runder)
            {

                List<int> arr = item.GetValue("valsArray").ToObject<List<int>>();

                Tournament.Runde run = new Tournament.Runde();
                run.whereto = arr;

                run.RundeMatch = new Tournament.Match() { ParentMatchId = LastMatchId, Individual = false, name = item.GetValue("navn").ToString() };

                LastMatchId = ubo.SP_CreateMatch(item.GetValue("navn").ToString(), LastMatchId, null, false);
                run.RundeMatch.MatchId = LastMatchId;


                //Console.WriteLine("Create: runde("+ item.GetValue("navn").ToString() + ")");
                run.name = item.GetValue("navn").ToString();
                run.playstyle = item.GetValue("playstyle").ToString();
                int NbPlayers = Int32.Parse(item.GetValue("NbPlayers").ToString());
                int puljesize = Int32.Parse(item.GetValue("puljesize").ToString());

                int Best = 0;
                if (item.GetValue("BestOf") != null)
                {
                    Best = Int32.Parse(item.GetValue("BestOf").ToString());
                }
                run.BestOf = Best;
                run.NbPlayers = NbPlayers;
                run.puljesize = puljesize;

                turn.runder.Add(run);

                for (int i = 0; i < NbPlayers / puljesize; i++)
                {
                    Tournament.Pulje pu = new Tournament.Pulje();
                    Tournament.Match puljematch = new Tournament.Match() { ParentMatchId = LastMatchId, name = "pulje" + " " + (i + 1).ToString(), Individual = false };
                    int puljeMatchId = ubo.SP_CreateMatch("pulje" + " " + (i + 1).ToString(), LastMatchId, null, false);
                    puljematch.MatchId = puljeMatchId;
                    pu.PuljeMatch = puljematch;

                    //We create Seats for Puljer
                    for (int r = 0; r < puljesize; r++)
                    {
                        int SeatId = ubo.SP_CreateSeat(null, null, puljeMatchId, null, null);
                        puljematch.seats.Add(new Tournament.Seat() { SeatId = SeatId });
                    }


                    //Console.WriteLine("Create: "+ "pulje" + " " + (i + 1).ToString());
                    run.puljer.Add(pu);

                    int MatchRuleId = 0;

                    if (String.Compare(item.GetValue("playstyle").ToString(), "beerpot") == 0)
                    {
                        MatchRuleId = ubo.SP_CreateMatchRule(3, 70, 0, null);
                    }
                    else if (String.Compare(item.GetValue("playstyle").ToString(), "swiss") == 0)
                    {
                        MatchRuleId = ubo.SP_CreateMatchRule(2, 70, 0, null);
                    }
                    else if (String.Compare(item.GetValue("playstyle").ToString(), "roundrobin") == 0)
                    {
                        MatchRuleId = ubo.SP_CreateMatchRule(1, 70, 0, null);
                    }

                    if (String.Compare(item.GetValue("playstyle").ToString(), "beerpot") == 0)
                    {
                        int matchId = ubo.SP_CreateMatch("runde: " + item.GetValue("navn").ToString() + " Pulje " + (i + 1) + " Alle i en kamp ", puljeMatchId, MatchRuleId, true);
                        Tournament.Match mat = new Tournament.Match() { Individual = true, MatchId = matchId, name = "runde: " + item.GetValue("navn").ToString() + " Pulje " + (i + 1) + " Alle i en kamp ", ParentMatchId = puljeMatchId };
                        pu.matches.Add(mat);

                        for (int l = 0; l < puljesize; l++)
                        {
                            int FirstSeatId = ubo.SP_CreateSeat(puljematch.seats[l].SeatId, null, mat.MatchId, null, null);
                            mat.seats.Add(new Tournament.Seat() { SeatId = FirstSeatId, ParentSeatId = puljematch.seats[l].SeatId });
                        }
                    }
                    else
                    {

                        //create individual matches
                        int kampnr = 1;
                        int iterate = puljesize;
                        int nb = 1;
                        for (int j = 0; j < puljesize; j++)
                        {
                            for (int k = iterate; k > nb; k--)
                            {
                                //Console.WriteLine("runde: " + item.GetValue("navn").ToString() + " Pulje "+(i+1) +" Kamp "+kampnr );

                                //Console.WriteLine("spiller "+(j+1)+"," +k );
                                int BestOf = Int32.Parse(item.GetValue("BestOf").ToString());

                                for (int l = 0; l < BestOf; l++)
                                {
                                    string tex = "";
                                    if (BestOf > 1)
                                    {
                                        tex = " BestOf: " + (l + 1);
                                        //Console.WriteLine("indbyrdes kamp:" + l);
                                    }

                                    int matchId = ubo.SP_CreateMatch("runde: " + item.GetValue("navn").ToString() + " Pulje " + (i + 1) + " Kamp " + kampnr + tex, puljeMatchId, MatchRuleId, true);
                                    Tournament.Match mat = new Tournament.Match() { Individual = true, MatchId = matchId, name = "runde: " + item.GetValue("navn").ToString() + " Pulje " + (i + 1) + " Kamp " + kampnr + tex, ParentMatchId = puljeMatchId };
                                    pu.matches.Add(mat);

                                    int FirstSeatId = ubo.SP_CreateSeat(puljematch.seats[j].SeatId, null, mat.MatchId, null, null);
                                    mat.seats.Add(new Tournament.Seat() { SeatId = FirstSeatId, ParentSeatId = puljematch.seats[j].SeatId });

                                    int SecondSeatId = ubo.SP_CreateSeat(puljematch.seats[k - 1].SeatId, null, mat.MatchId, null, null);
                                    mat.seats.Add(new Tournament.Seat() { SeatId = SecondSeatId, ParentSeatId = puljematch.seats[k - 1].SeatId });

                                }



                                kampnr++;
                            }
                            if (String.Compare(item.GetValue("playstyle").ToString(), "swiss") == 0)
                            {
                                nb = iterate;
                            }
                            else
                            {
                                nb++;
                            }
                        }
                        kampnr = 1;
                    }

                }
            }

            //make circular reference
            ubo.SP_SetParentMatchId(TournamentMatchId, LastMatchId);

            turn.TournamentMatch.ParentMatchId = LastMatchId;

            bool firstRunde = true;
            int nbPlayer = 1;
            int rundeindex = 0;

            List<SavedVariabels> saved = new List<SavedVariabels>();

            //We make structure to keep PuljeMatch,Seat,Place

            foreach (Tournament.Runde run in turn.runder)
            {

                foreach (Tournament.Pulje pulje in run.puljer)
                {
                    int puljePlace = 1;
                    foreach (Tournament.Seat sea in pulje.PuljeMatch.seats)
                    {
                        //We set the players in first runde!!
                        if (firstRunde)
                        {
                            int PlayerId = ubo.SP_CreatePlayer(null, "Player:" + nbPlayer, null);
                            ubo.SP_SetPlayerOnSeat(sea.SeatId, PlayerId);
                            Tournament.Player pl = new Tournament.Player() { PlayerId = PlayerId };

                            SavedVariabels entry = new SavedVariabels() { BeenInPuljeMatchId = pulje.PuljeMatch.MatchId, ParentPuljeMatchId = 0, Place = puljePlace, Runde = rundeindex, Player = nbPlayer };
                            saved.Add(entry);

                            nbPlayer++;
                            puljePlace++;

                        }
                    }
                }
                firstRunde = false;



                if (rundeindex > 0)
                {

                    List<SavedVariabels> newsaved = GetPosibleVars(turn, saved, rundeindex);

                    SetPlayersInPulje(turn, newsaved, saved, rundeindex);

                    foreach (SavedVariabels item in newsaved)
                    {

                        saved.Add(item);

                    }

                }
                rundeindex++;

            }

            foreach (SavedVariabels va in saved)
            {
                Console.WriteLine(va);
            }

            return TournamentMatchId;
        }
    }
}
