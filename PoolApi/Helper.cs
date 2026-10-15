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
            public List<int> poolSizes = new List<int>();
            public string playstyle;
            public int BestOf;
            public int PlayFrom;
            public int PlayTo;
            public List<int> whereto = new List<int>();
            public List<Pulje> puljer = new List<Pulje>();

            public override string ToString()
            {
                string str = "Runde:(" + name + ") NbPlayers:" + NbPlayers + " puljesize:" + puljesize + " playstyle:" + playstyle + " BestOf:" + BestOf + " PlayFrom:" + PlayFrom + " PlayTo:" + PlayTo + "\r\n";

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

            if (newsaved == null || newsaved.Count == 0)
            {
                return;
            }

            foreach (Tournament.Pulje item in turn.runder[rundeindex].puljer)
            {
                List<SavedVariabels> current = new List<SavedVariabels>();
                int nbseat = 1;

                foreach (Tournament.Seat seat in item.PuljeMatch.seats)
                {
                    // Wildcard-enabled stages can deliberately have more seats than
                    // normal automatic qualifiers. Leave those extra seats empty;
                    // ApplyExplicitPoolAdvancementRoutes marks them as wildcard slots.
                    if (!newsaved.Any(x => !x.found))
                    {
                        break;
                    }

                    SavedVariabels selected;
                    if (current.Count == 0)
                    {
                        selected = newsaved.First(x => !x.found);
                        selected.found = true;
                    }
                    else
                    {
                        selected = FirstNotInCurrent(newsaved, current);
                    }

                    current.Add(selected);
                    selected.BeenInPuljeMatchId = item.PuljeMatch.MatchId;
                    ubo.SP_SetSeatAutoPlace(
                        seat.SeatId,
                        selected.ParentPuljeMatchId,
                        selected.ParentPlace);
                    selected.Place = nbseat;
                    nbseat++;
                }
            }
        }
        private static List<int> BuildEvenPoolSizes(int nbPlayers, int preferredPoolSize)
        {
            List<int> sizes = new List<int>();

            if (nbPlayers < 2 || preferredPoolSize < 2)
            {
                return sizes;
            }

            int groups = (int)Math.Ceiling((double)nbPlayers / preferredPoolSize);

            // Never create a one-player pool. If necessary, use slightly larger
            // pools than the preferred size instead.
            while (groups > 1 && (nbPlayers / groups) < 2)
            {
                groups--;
            }

            int baseSize = nbPlayers / groups;
            int remainder = nbPlayers % groups;

            for (int i = 0; i < groups; i++)
            {
                sizes.Add(baseSize + (i < remainder ? 1 : 0));
            }

            return sizes;
        }

        private static bool IsValidPoolSizes(List<int> sizes, int nbPlayers)
        {
            return sizes != null
                && sizes.Count > 0
                && sizes.All(size => size >= 2)
                && sizes.Sum() == nbPlayers;
        }


        private class ExplicitPoolAdvancementRoute
        {
            public int SourceStage { get; set; }
            public int SourcePool { get; set; }
            public int Place { get; set; }
            public int DestinationStage { get; set; }
            public int DestinationPool { get; set; }
        }

        private class ExplicitWildcardRoute
        {
            public int SourceStage { get; set; }
            public int WildcardIndex { get; set; }
            public int DestinationStage { get; set; }
            public int DestinationPool { get; set; }
        }

        private static void ApplyExplicitPoolAdvancementRoutes(Tournament turn, JArray roundJson, UserBackoffice ubo)
        {
            var routesByDestinationStage = new Dictionary<int, List<ExplicitPoolAdvancementRoute>>();
            var wildcardsByDestinationStage = new Dictionary<int, List<ExplicitWildcardRoute>>();
            var seenSourceRoutes = new HashSet<string>();
            bool hasExplicitRoutes = false;

            for (int sourceStage = 0; sourceStage < roundJson.Count; sourceStage++)
            {
                JObject sourceJson = roundJson[sourceStage] as JObject;
                if (sourceJson == null) continue;

                JToken routesToken = sourceJson.GetValue("poolAdvancementRoutes");
                if (routesToken != null && routesToken.Type == JTokenType.Array)
                {
                    foreach (JObject routeJson in (JArray)routesToken)
                    {
                        if (routeJson == null) continue;

                        int? sourcePool = routeJson.Value<int?>("sourcePool");
                        int? place = routeJson.Value<int?>("place");
                        int? destinationStage = routeJson.Value<int?>("destinationStage");
                        int? destinationPool = routeJson.Value<int?>("destinationPool");

                        if (!sourcePool.HasValue || !place.HasValue || !destinationStage.HasValue || !destinationPool.HasValue)
                        {
                            throw new ArgumentException("A pool advancement route is incomplete.");
                        }

                        if (sourceStage < 0 || sourceStage >= turn.runder.Count ||
                            sourcePool.Value < 0 || sourcePool.Value >= turn.runder[sourceStage].puljer.Count)
                        {
                            throw new ArgumentException("A pool advancement route has an invalid source pool.");
                        }

                        Tournament.Pulje sourcePulje = turn.runder[sourceStage].puljer[sourcePool.Value];
                        int sourcePoolSize = sourcePulje.PuljeMatch.seats.Count;
                        if (place.Value < 1 || place.Value > sourcePoolSize)
                        {
                            throw new ArgumentException("A pool advancement route has an invalid finishing place.");
                        }

                        if (destinationStage.Value <= sourceStage || destinationStage.Value >= turn.runder.Count ||
                            destinationPool.Value < 0 || destinationPool.Value >= turn.runder[destinationStage.Value].puljer.Count)
                        {
                            throw new ArgumentException("A pool advancement route has an invalid destination pool.");
                        }

                        if (turn.runder[sourceStage].whereto == null ||
                            place.Value > turn.runder[sourceStage].whereto.Count ||
                            turn.runder[sourceStage].whereto[place.Value - 1] != destinationStage.Value)
                        {
                            throw new ArgumentException("A pool advancement route does not match the stage route for that finishing place.");
                        }

                        string sourceKey = sourceStage + "|" + sourcePool.Value + "|" + place.Value;
                        if (!seenSourceRoutes.Add(sourceKey))
                        {
                            throw new ArgumentException("The same source pool/place is routed more than once.");
                        }

                        var route = new ExplicitPoolAdvancementRoute
                        {
                            SourceStage = sourceStage,
                            SourcePool = sourcePool.Value,
                            Place = place.Value,
                            DestinationStage = destinationStage.Value,
                            DestinationPool = destinationPool.Value
                        };

                        if (!routesByDestinationStage.ContainsKey(route.DestinationStage))
                        {
                            routesByDestinationStage[route.DestinationStage] = new List<ExplicitPoolAdvancementRoute>();
                        }

                        routesByDestinationStage[route.DestinationStage].Add(route);
                        hasExplicitRoutes = true;
                    }
                }

                int wildcardCount = sourceJson.Value<int?>("wildcardCount") ?? 0;
                if (wildcardCount < 0)
                {
                    throw new ArgumentException("Wildcard count cannot be negative.");
                }

                JToken wildcardRoutesToken = sourceJson.GetValue("wildcardRoutes");
                JArray wildcardRoutesJson = wildcardRoutesToken != null && wildcardRoutesToken.Type == JTokenType.Array
                    ? (JArray)wildcardRoutesToken
                    : new JArray();

                if (wildcardRoutesJson.Count != wildcardCount)
                {
                    throw new ArgumentException("The number of wildcard routes does not match the configured wildcard count.");
                }

                var seenWildcardIndexes = new HashSet<int>();
                foreach (JObject wildcardJson in wildcardRoutesJson)
                {
                    if (wildcardJson == null) continue;

                    int? wildcardIndex = wildcardJson.Value<int?>("wildcardIndex");
                    int? destinationStage = wildcardJson.Value<int?>("destinationStage");
                    int? destinationPool = wildcardJson.Value<int?>("destinationPool");

                    if (!wildcardIndex.HasValue || !destinationStage.HasValue || !destinationPool.HasValue)
                    {
                        throw new ArgumentException("A wildcard route is incomplete.");
                    }

                    if (wildcardIndex.Value < 0 || wildcardIndex.Value >= wildcardCount ||
                        !seenWildcardIndexes.Add(wildcardIndex.Value))
                    {
                        throw new ArgumentException("A wildcard route has an invalid wildcard number.");
                    }

                    // Wildcards are deliberately tied to the immediately following stage.
                    if (destinationStage.Value != sourceStage + 1 ||
                        destinationStage.Value >= turn.runder.Count ||
                        destinationPool.Value < 0 ||
                        destinationPool.Value >= turn.runder[destinationStage.Value].puljer.Count)
                    {
                        throw new ArgumentException("A wildcard route has an invalid destination.");
                    }

                    var wildcard = new ExplicitWildcardRoute
                    {
                        SourceStage = sourceStage,
                        WildcardIndex = wildcardIndex.Value,
                        DestinationStage = destinationStage.Value,
                        DestinationPool = destinationPool.Value
                    };

                    if (!wildcardsByDestinationStage.ContainsKey(wildcard.DestinationStage))
                    {
                        wildcardsByDestinationStage[wildcard.DestinationStage] = new List<ExplicitWildcardRoute>();
                    }

                    wildcardsByDestinationStage[wildcard.DestinationStage].Add(wildcard);
                    hasExplicitRoutes = true;
                }
            }

            if (!hasExplicitRoutes) return; // Backward compatibility with older clients/drafts.

            var destinationStages = new HashSet<int>(routesByDestinationStage.Keys);
            destinationStages.UnionWith(wildcardsByDestinationStage.Keys);

            foreach (int destinationStage in destinationStages.OrderBy(x => x))
            {
                Tournament.Runde destinationRound = turn.runder[destinationStage];
                List<ExplicitPoolAdvancementRoute> routes = routesByDestinationStage.ContainsKey(destinationStage)
                    ? routesByDestinationStage[destinationStage]
                    : new List<ExplicitPoolAdvancementRoute>();
                List<ExplicitWildcardRoute> wildcardRoutes = wildcardsByDestinationStage.ContainsKey(destinationStage)
                    ? wildcardsByDestinationStage[destinationStage]
                    : new List<ExplicitWildcardRoute>();
                int destinationSeatCount = destinationRound.puljer.Sum(p => p.PuljeMatch.seats.Count);

                // Every destination seat must be either a normal automatic route or
                // a reserved wildcard slot. Wildcard slots stay empty until selected.
                if (routes.Count + wildcardRoutes.Count != destinationSeatCount)
                {
                    throw new ArgumentException(
                        "Explicit advancement for stage '" + destinationRound.name +
                        "' contains " + routes.Count + " normal routes and " +
                        wildcardRoutes.Count + " wildcard routes, but the stage has " +
                        destinationSeatCount + " seats.");
                }

                for (int destinationPool = 0; destinationPool < destinationRound.puljer.Count; destinationPool++)
                {
                    Tournament.Pulje destinationPulje = destinationRound.puljer[destinationPool];
                    List<ExplicitPoolAdvancementRoute> poolRoutes = routes
                        .Where(r => r.DestinationPool == destinationPool)
                        .OrderBy(r => r.SourceStage)
                        .ThenBy(r => r.SourcePool)
                        .ThenBy(r => r.Place)
                        .ToList();
                    List<ExplicitWildcardRoute> poolWildcards = wildcardRoutes
                        .Where(r => r.DestinationPool == destinationPool)
                        .OrderBy(r => r.SourceStage)
                        .ThenBy(r => r.WildcardIndex)
                        .ToList();

                    if (poolRoutes.Count + poolWildcards.Count != destinationPulje.PuljeMatch.seats.Count)
                    {
                        throw new ArgumentException(
                            "Destination pulje " + (destinationPool + 1) + " in stage '" +
                            destinationRound.name + "' needs " + destinationPulje.PuljeMatch.seats.Count +
                            " players, but " + poolRoutes.Count + " normal routes and " +
                            poolWildcards.Count + " wildcards were assigned.");
                    }

                    int seatIndex = 0;
                    foreach (ExplicitPoolAdvancementRoute route in poolRoutes)
                    {
                        Tournament.Pulje sourcePulje = turn.runder[route.SourceStage].puljer[route.SourcePool];
                        Tournament.Seat destinationSeat = destinationPulje.PuljeMatch.seats[seatIndex++];

                        ubo.SP_SetSeatAutoPlace(
                            destinationSeat.SeatId,
                            sourcePulje.PuljeMatch.MatchId,
                            route.Place);
                    }

                    foreach (ExplicitWildcardRoute wildcard in poolWildcards)
                    {
                        Tournament.Seat destinationSeat = destinationPulje.PuljeMatch.seats[seatIndex++];

                        // Place 0 is reserved for a wildcard. AutoSelectMatchId points
                        // to the SOURCE STAGE rather than a source pool, so it is never
                        // filled automatically by a finishing position.
                        ubo.SP_SetSeatAutoPlace(
                            destinationSeat.SeatId,
                            turn.runder[wildcard.SourceStage].RundeMatch.MatchId,
                            0);
                    }
                }
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

                // Match scoring is configured per stage. Older clients that do
                // not send these values keep the original 70 -> 0 behaviour.
                int playFrom = 70;
                int playTo = 0;
                JToken playFromToken = item.GetValue("PlayFrom");
                JToken playToToken = item.GetValue("PlayTo");
                int parsedPlayFrom;
                int parsedPlayTo;
                if (playFromToken != null && Int32.TryParse(playFromToken.ToString(), out parsedPlayFrom))
                {
                    playFrom = parsedPlayFrom;
                }
                if (playToToken != null && Int32.TryParse(playToToken.ToString(), out parsedPlayTo))
                {
                    playTo = parsedPlayTo;
                }
                if (playFrom < 0 || playTo < 0 || playFrom == playTo)
                {
                    throw new ArgumentException("Play from and Play to must be different non-negative whole numbers.");
                }

                run.PlayFrom = playFrom;
                run.PlayTo = playTo;
                run.NbPlayers = NbPlayers;
                run.puljesize = puljesize;

                // New format: the frontend can send the actual size of every pool,
                // e.g. 13 players -> [4,3,3,3]. Old payloads are still supported.
                List<int> poolSizes = new List<int>();
                JToken poolSizesToken = item.GetValue("poolSizes");
                if (poolSizesToken != null && poolSizesToken.Type == JTokenType.Array)
                {
                    poolSizes = poolSizesToken.ToObject<List<int>>();
                }

                bool distributeEvenly = false;
                JToken distributeToken = item.GetValue("distributeEvenly");
                if (distributeToken != null)
                {
                    distributeEvenly = distributeToken.ToObject<bool>();
                }

                if (!IsValidPoolSizes(poolSizes, NbPlayers))
                {
                    if (distributeEvenly)
                    {
                        poolSizes = BuildEvenPoolSizes(NbPlayers, puljesize);
                    }
                    else
                    {
                        if (puljesize < 2 || NbPlayers % puljesize != 0)
                        {
                            throw new ArgumentException("The number of players cannot be divided into the requested pool size.");
                        }

                        for (int i = 0; i < NbPlayers / puljesize; i++)
                        {
                            poolSizes.Add(puljesize);
                        }
                    }
                }

                if (!IsValidPoolSizes(poolSizes, NbPlayers))
                {
                    throw new ArgumentException("Invalid pool distribution.");
                }

                run.poolSizes = poolSizes;
                turn.runder.Add(run);

                for (int i = 0; i < poolSizes.Count; i++)
                {
                    int currentPoolSize = poolSizes[i];
                    Tournament.Pulje pu = new Tournament.Pulje();
                    Tournament.Match puljematch = new Tournament.Match() { ParentMatchId = LastMatchId, name = "pulje" + " " + (i + 1).ToString(), Individual = false };
                    int puljeMatchId = ubo.SP_CreateMatch("pulje" + " " + (i + 1).ToString(), LastMatchId, null, false);
                    puljematch.MatchId = puljeMatchId;
                    pu.PuljeMatch = puljematch;

                    //We create Seats for Puljer
                    for (int r = 0; r < currentPoolSize; r++)
                    {
                        int SeatId = ubo.SP_CreateSeat(null, null, puljeMatchId, null, null);
                        puljematch.seats.Add(new Tournament.Seat() { SeatId = SeatId });
                    }


                    //Console.WriteLine("Create: "+ "pulje" + " " + (i + 1).ToString());
                    run.puljer.Add(pu);

                    int MatchRuleId = 0;
                    string playstyle = item.GetValue("playstyle").ToString();

                    if (String.Compare(playstyle, "beerpot") == 0)
                    {
                        MatchRuleId = ubo.SP_CreateMatchRule(3, playFrom, playTo, null);
                    }
                    else if (String.Compare(playstyle, "swiss") == 0)
                    {
                        int swissRounds = 0;
                        JToken swissRoundsToken = item.GetValue("swissRounds");
                        if (swissRoundsToken != null)
                        {
                            Int32.TryParse(swissRoundsToken.ToString(), out swissRounds);
                        }

                        if (swissRounds < 1)
                        {
                            swissRounds = Math.Max(1, (int)Math.Ceiling(Math.Log(Math.Max(2, currentPoolSize), 2)));
                        }

                        // Store the configured number of rounds in the existing rule
                        // description. This keeps Swiss support compatible with existing
                        // databases and also gives the match editor a useful description.
                        MatchRuleId = ubo.SP_CreateMatchRule(
                            2,
                            playFrom,
                            playTo,
                            "Swiss system: " + swissRounds + " rounds");
                    }
                    else if (String.Compare(playstyle, "roundrobin") == 0)
                    {
                        MatchRuleId = ubo.SP_CreateMatchRule(1, playFrom, playTo, null);
                    }

                    if (String.Compare(playstyle, "beerpot") == 0)
                    {
                        int matchId = ubo.SP_CreateMatch("runde: " + item.GetValue("navn").ToString() + " Pulje " + (i + 1) + " Alle i en kamp ", puljeMatchId, MatchRuleId, true);
                        Tournament.Match mat = new Tournament.Match() { Individual = true, MatchId = matchId, name = "runde: " + item.GetValue("navn").ToString() + " Pulje " + (i + 1) + " Alle i en kamp ", ParentMatchId = puljeMatchId };
                        pu.matches.Add(mat);

                        for (int l = 0; l < currentPoolSize; l++)
                        {
                            int FirstSeatId = ubo.SP_CreateSeat(puljematch.seats[l].SeatId, null, mat.MatchId, null, null);
                            mat.seats.Add(new Tournament.Seat() { SeatId = FirstSeatId, ParentSeatId = puljematch.seats[l].SeatId });
                        }
                    }
                    else if (String.Compare(playstyle, "swiss") == 0)
                    {
                        // Swiss is dynamic. Only round 1 exists when the tournament is
                        // created. Later rounds are generated by MatchResultBackoffice
                        // after every match in the current round has been completed.
                        int kampnr = 1;
                        int seatIndex = 0;

                        while (seatIndex + 1 < currentPoolSize)
                        {
                            string matchName =
                                "runde: " + item.GetValue("navn").ToString() +
                                " Pulje " + (i + 1) +
                                " Swiss round 1 Match " + kampnr;

                            int matchId = ubo.SP_CreateMatch(matchName, puljeMatchId, MatchRuleId, true);
                            Tournament.Match mat = new Tournament.Match()
                            {
                                Individual = true,
                                MatchId = matchId,
                                name = matchName,
                                ParentMatchId = puljeMatchId
                            };
                            pu.matches.Add(mat);

                            int firstSeatId = ubo.SP_CreateSeat(
                                puljematch.seats[seatIndex].SeatId,
                                null,
                                matchId,
                                null,
                                null);
                            mat.seats.Add(new Tournament.Seat()
                            {
                                SeatId = firstSeatId,
                                ParentSeatId = puljematch.seats[seatIndex].SeatId
                            });

                            int secondSeatId = ubo.SP_CreateSeat(
                                puljematch.seats[seatIndex + 1].SeatId,
                                null,
                                matchId,
                                null,
                                null);
                            mat.seats.Add(new Tournament.Seat()
                            {
                                SeatId = secondSeatId,
                                ParentSeatId = puljematch.seats[seatIndex + 1].SeatId
                            });

                            seatIndex += 2;
                            kampnr++;
                        }

                        // With an odd number of players, the lowest initial seed gets
                        // the first bye. It is stored as an automatically completed
                        // one-player match so standings and PoolPlayer can show it.
                        if (seatIndex < currentPoolSize)
                        {
                            string byeName =
                                "runde: " + item.GetValue("navn").ToString() +
                                " Pulje " + (i + 1) +
                                " Swiss round 1 BYE";

                            int byeMatchId = ubo.SP_CreateMatch(byeName, puljeMatchId, MatchRuleId, true);
                            Tournament.Match byeMatch = new Tournament.Match()
                            {
                                Individual = true,
                                MatchId = byeMatchId,
                                name = byeName,
                                ParentMatchId = puljeMatchId
                            };
                            pu.matches.Add(byeMatch);

                            int byeSeatId = ubo.SP_CreateSeat(
                                puljematch.seats[seatIndex].SeatId,
                                null,
                                byeMatchId,
                                null,
                                null);
                            byeMatch.seats.Add(new Tournament.Seat()
                            {
                                SeatId = byeSeatId,
                                ParentSeatId = puljematch.seats[seatIndex].SeatId
                            });

                            ubo.SetSeatResult(byeSeatId, 1, 0);
                        }
                    }
                    else
                    {
                        // Round robin: every player meets every other player.
                        int kampnr = 1;
                        int iterate = currentPoolSize;
                        int nb = 1;
                        for (int j = 0; j < currentPoolSize; j++)
                        {
                            for (int k = iterate; k > nb; k--)
                            {
                                int BestOf = Int32.Parse(item.GetValue("BestOf").ToString());

                                for (int l = 0; l < BestOf; l++)
                                {
                                    string tex = "";
                                    if (BestOf > 1)
                                    {
                                        tex = " BestOf: " + (l + 1);
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
                            nb++;
                        }
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

            // If the tournament builder supplied exact pool-to-pool routes,
            // overwrite the generic balancing routes with those choices.
            ApplyExplicitPoolAdvancementRoutes(turn, runder, ubo);

            foreach (SavedVariabels va in saved)
            {
                Console.WriteLine(va);
            }

            return TournamentMatchId;
        }
    }
}
