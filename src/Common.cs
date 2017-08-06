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

        [JsonProperty(PropertyName = "md")]
        public List<MineDistance> mineDistances;
    }

    class MineDistance
    {
        [JsonProperty(PropertyName = "id")]
        public int mineId;

        [JsonProperty(PropertyName = "d")]
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
        public bool debug;

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

            if (debug)
            {
                Console.Error.WriteLine("< " + commands.Current);
            }

            return JsonConvert.DeserializeObject<T>(commands.Current, DeserializerSettings);
        }

        public void Write<T>(T obj, Formatting formatting = Formatting.None)
        {
            var s = JsonConvert.SerializeObject(obj, formatting, SerializerSettings);
            writer.Write(s.Length);
            writer.Write(':');
            writer.Write(s);
            writer.Flush();

            if (debug)
            {
                Console.Error.WriteLine("> " + s);
            }
        }

        static IEnumerable<string> ReadCommands(TextReader reader)
        {
            //int readChar;
            var sb = new StringBuilder();
            var chBuffer = new char[1];

            while (reader.ReadBlock(chBuffer, 0, 1) >= 0)
            {
                var ch = chBuffer[0];
                //Console.Error.WriteLine($"#{(int)ch} '{ch}'");

                if (ch == ':')
                {
                    //Console.Error.WriteLine($"<< '{sb.ToString()}'");
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

    class TreeSet
    {
        //static readonly List<HashSet<int>> EmptyTreeList = new List<HashSet<int>>();

        List<HashSet<int>> trees;

        TreeSet(TreeSet other)
        {
            trees = other.trees.Select(i => new HashSet<int>(i)).ToList();
        }

        public TreeSet(
            IEnumerable<Move> moves,
            IEnumerable<int> mines = null,
            Func<ClaimMove, bool> moveFilter = null)
        {
            trees = new List<HashSet<int>>();

            if (mines != null)
            {
                trees.AddRange(mines.Select(mine => new HashSet<int>() { mine }));
            }

            var claims = moves
                .Select(move => move.claim)
                .Where(claim => claim != null)
                .Where(claim => moveFilter == null ? true : moveFilter(claim));

            foreach (var claim in claims)
            {
                AddRiverImpl(claim.source, claim.target);
            }
        }

        public TreeSet AddRiver(River river)
        {
            var ans = new TreeSet(this);
            ans.AddRiverImpl(river.source, river.target);
            return ans;
        }

        void AddRiverImpl(
            int source,
            int target)
        {
            var matchingTrees = this.trees
                .Where(tree => tree.Contains(source) || tree.Contains(target))
                .ToList();

            switch (matchingTrees.Count)
            {
                case 0:
                    this.trees.Add(new HashSet<int>() { source, target });
                    break;
                case 1:
                    matchingTrees.First().Add(source);
                    matchingTrees.First().Add(target);
                    break;
                case 2:
                    matchingTrees.First().UnionWith(matchingTrees.Last());
                    this.trees.Remove(matchingTrees.Last());
                    break;
                default:
                    throw new Exception("wtf?");
            }
        }

        public int ComputeScore(Map map)
        {
            return map.mines.Sum(mine =>
                this.trees.Sum(tree =>
                    tree.Contains(mine) ? tree.Sum(site => Utils.GetSquaredMineDistance(map.sites, mine, site)) : 0));
        }

        public int ComputeLiberty(IEnumerable<River> rivers)
        {
            return this.trees
                .SelectMany(tree => Utils.BreadthFirstSearch(tree, rivers, 2))
                .Distinct()
                .Count();
        }

        public bool Contains(int site)
        {
            return trees.Any(tree => tree.Contains(site));
        }
    }

    static class Utils
    {
        public static void ComputeMineDistances(Map map)
        {
            var sites = map.sites.ToDictionary(i => i.id, i => i);

            foreach (Site site in map.sites)
            {
                site.mineDistances = new List<MineDistance>();
            }

            foreach (int mine in map.mines)
            {
                foreach (var i in BreadthFirstSearch(new HashSet<int>() { mine }, map.rivers))
                {
                    sites[i.site].mineDistances.Add(
                        new MineDistance()
                        {
                            mineId = mine,
                            distance = i.distance
                        });
                }
            }
        }

        public struct BfsResult
        {
            public int site;
            public int previousSite;
            public int distance;
        };

        public static IEnumerable<BfsResult> BreadthFirstSearch(
            HashSet<int> startingSites,
            IEnumerable<River> rivers,
            int maxDepth = int.MaxValue)
        {
            var adjacencyMap = BuildAdjacencyMap(rivers);

            var distances = startingSites.ToDictionary(i => i, i => 0);

            var queue = new Queue<int>(startingSites);

            while (queue.Any())
            {
                var item = queue.Dequeue();
                var distance = distances[item];
                if (distance >= maxDepth)
                {
                    yield break; 
                }

                HashSet<int> neighbors;
                if (adjacencyMap.TryGetValue(item, out neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (!distances.ContainsKey(neighbor))
                        {
                            distances[neighbor] = distance + 1;
                            queue.Enqueue(neighbor);
                            yield return new BfsResult()
                            {
                                site = neighbor,
                                previousSite = item,
                                distance = distance + 1
                            };
                        }
                    }
                }
            }
        }

        public static Dictionary<int, HashSet<int>> BuildAdjacencyMap(
            IEnumerable<River> rivers)
        {
            var ans = new Dictionary<int, HashSet<int>>();

            Action<int, int> add = (source, target) =>
            {
                if (!ans.ContainsKey(source))
                {
                    ans[source] = new HashSet<int>();
                }

                ans[source].Add(target);
            };

            foreach (var river in rivers)
            {
                add(river.source, river.target);
                add(river.target, river.source);
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

            var dist = ans == null ? 0 : ans.distance;

            return dist * dist;
        }

        public static List<int> FindShortestPath(
            HashSet<int> source,
            HashSet<int> target,
            IEnumerable<River> rivers)
        {
            var previousSites = new Dictionary<int, int>();

            foreach (var result in BreadthFirstSearch(source, rivers))
            {
                previousSites[result.site] = result.previousSite;

                if (target.Contains(result.site))
                {
                    var ans = new List<int>();
                    var site = result.site;

                    while (true)
                    {
                        ans.Add(site);

                        int newSite;
                        if (!previousSites.TryGetValue(site, out newSite))
                        {
                            return ans;
                        }

                        site = newSite;
                    }
                }
            }

            return null;
        }

        public static List<River> RemoveRivers(
            IEnumerable<River> rivers,
            IEnumerable<Move> riversToRemove)
        {
            var takenRivers = riversToRemove
                .ToDictionary(
                    move => new River()
                    {
                        source = move.claim.source,
                        target = move.claim.target
                    },
                    move => true);

            return rivers
                .Where(river => !takenRivers.ContainsKey(river))
                .ToList();
        }
    }
}
