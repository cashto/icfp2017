using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Icfp2017
{
    class MeMessage
    {
        public string me;
    }

    class YouMessage
    {
        public string you;
    }

    class ReadyMessage
    {
        public int ready;
        public JObject state;
    }

    class SetupResponse
    {
        public int ready;
        public JObject state;
    }

    class Map
    {
        public List<Site> sites;
        public List<River> rivers;
        public List<int> mines;
    }

    class Site
    {
        public int id;
        public List<MineDistance> mineDistances;
    }

    class MineDistance
    {
        public int mineId;
        public int distance;
    }

    struct River
    {
        public int source;
        public int target;
    }

    class ServerMessage
    {
        public MoveMessage move;
        public StopMessage stop;
        public int? timeout;
        public int? punter;
        public int? punters;
        public Map map;
        public JObject state;
    }

    class MoveMessage
    {
        public List<Move> moves;
        public JObject state;
    }

    class StopMessage
    {
        public List<Move> moves;
        public List<Score> scores;
    }

    class Move
    {
        public ClaimMove claim;
        public PassMove pass;
        public JObject state;
    }

    class PassMove
    {
        public int punter;
    }

    class ClaimMove : PassMove
    {
        public int source;
        public int target;
    }

    class Score
    {
        public int punter;
        public int score;
    }

    class Parser
    {
        IEnumerator<string> commands;
        TextWriter writer;

        public static readonly JsonSerializerSettings DeserializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings()
        {
            NullValueHandling = NullValueHandling.Ignore,
        };

        public Parser(TextReader reader, TextWriter writer)
        {
            this.commands = ReadCommands(reader).GetEnumerator();
            this.writer = writer;
        }

        public T Read<T>()
        {
            if (!commands.MoveNext())
            {
                throw new Exception("Expected a command, got nothing");
            }

            return JsonConvert.DeserializeObject<T>(commands.Current, DeserializerSettings);
        }

        public void Write<T>(T obj)
        {
            var s = JsonConvert.SerializeObject(obj, Formatting.None, SerializerSettings);
            writer.Write(s.Length);
            writer.Write(':');
            writer.Write(s);
            writer.Flush();
        }

        static IEnumerable<string> ReadCommands(TextReader reader)
        {
            int readChar;
            var sb = new StringBuilder();

            while ((readChar = reader.Read()) != -1)
            {
                var ch = (char)readChar;
                if (ch == ':')
                {
                    var n = int.Parse(sb.ToString());
                    var buffer = new char[n];
                    reader.ReadBlock(buffer, 0, n);
                    yield return new string(buffer);
                    sb.Clear();
                }
                else if (char.IsDigit(ch))
                {
                    sb.Append(ch);
                }
            }
        }
    }

    static class Utils
    {
        public static readonly List<HashSet<int>> EmptyTreeList = new List<HashSet<int>>();

        public static Dictionary<int, List<HashSet<int>>> ComputeTrees(
            List<Move> moves)
        {
            var ans = new Dictionary<int, List<HashSet<int>>>();

            var claims = moves
                .Select(move => move.claim)
                .Where(claim => claim != null);

            foreach (var claim in claims)
            {
                if (!ans.ContainsKey(claim.punter))
                {
                    ans[claim.punter] = new List<HashSet<int>>();
                }

                var trees = ans[claim.punter];

                var matchingTrees = trees
                    .Where(tree => tree.Contains(claim.source) || tree.Contains(claim.target))
                    .ToList();

                switch (matchingTrees.Count)
                {
                    case 0:
                        trees.Add(new HashSet<int>() { claim.source, claim.target });
                        break;
                    case 1:
                        matchingTrees.First().Add(claim.source);
                        matchingTrees.First().Add(claim.target);
                        break;
                    case 2:
                        matchingTrees.First().UnionWith(matchingTrees.Last());
                        trees.Remove(matchingTrees.Last());
                        break;
                    default:
                        throw new Exception("wtf?");
                }
            }

            return ans;
        }

        public static void ComputeMineDistances(Map map)
        {
            var mineDistances = map.mines.ToDictionary(
                    mine => mine,
                    mine => ComputeMineDistance(map, mine));

            foreach (Site site in map.sites)
            {
                site.mineDistances = mineDistances
                    .Where(mine => mine.Value.ContainsKey(site.id))
                    .Select(mine => new MineDistance() { mineId = mine.Key, distance = mine.Value[site.id] })
                    .ToList();
            }
        }

        static Dictionary<int, int> ComputeMineDistance(
            Map map,
            int mine)
        {
            var ans = new Dictionary<int, int>()
            {
                [mine] = 0
            };

            while (true)
            {
                var newAns = ans.ToDictionary(i => i.Key, i => i.Value);

                foreach (var river in map.rivers)
                {
                    var sourceDistance = TryGetValue(ans, river.source);
                    var targetDistance = TryGetValue(ans, river.target);

                    if (sourceDistance.HasValue && !targetDistance.HasValue)
                    {
                        newAns[river.target] = sourceDistance.Value + 1;
                    }
                    else if (!sourceDistance.HasValue && targetDistance.HasValue)
                    {
                        newAns[river.source] = targetDistance.Value + 1;
                    }
                }

                if (newAns.Count == ans.Count)
                {
                    return ans;
                }

                ans = newAns;
            }
        }

        static int? TryGetValue(Dictionary<int, int> dict, int key)
        {
            int ans;
            if (!dict.TryGetValue(key, out ans))
            {
                return null;
            }

            return ans;
        }

        public static int GetSquaredMineDistance(
            List<Site> sites,
            int mine,
            int newSite)
        {
            var ans = sites
                .First(site => site.id == newSite).mineDistances
                .FirstOrDefault(mineDistance => mineDistance.mineId == mine);

            var dist = sites == null ? 0 : ans.distance;

            return dist * dist;
        }
    }
}
