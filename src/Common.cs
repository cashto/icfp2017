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
        public Settings settings;
        public JObject state;
    }

    class Settings
    {
        public bool futures;
        public bool options;
        public bool splurges;
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
        public SplurgeMove splurge;
        public OptionMove option;
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

    class SplurgeMove : PassMove
    {
        public List<int> route;
    }

    class OptionMove : ClaimMove
    {
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

        public List<HashSet<int>> Trees { get; private set; }

        TreeSet(TreeSet other)
        {
            Trees = other.Trees.Select(i => new HashSet<int>(i)).ToList();
        }

        public TreeSet(
            IEnumerable<River> rivers,
            IEnumerable<int> mines = null)
        {
            Trees = new List<HashSet<int>>();

            if (mines != null)
            {
                Trees.AddRange(mines.Select(mine => new HashSet<int>() { mine }));
            }

            foreach (var river in rivers)
            {
                AddRiverImpl(river.source, river.target);
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
            var matchingTrees = this.Trees
                .Where(tree => tree.Contains(source) || tree.Contains(target))
                .ToList();

            switch (matchingTrees.Count)
            {
                case 0:
                    this.Trees.Add(new HashSet<int>() { source, target });
                    break;
                case 1:
                    matchingTrees.First().Add(source);
                    matchingTrees.First().Add(target);
                    break;
                case 2:
                    matchingTrees.First().UnionWith(matchingTrees.Last());
                    this.Trees.Remove(matchingTrees.Last());
                    break;
                default:
                    throw new Exception("wtf?");
            }
        }

        public int ComputeScore(Map map)
        {
            return map.mines.Sum(mine =>
                this.Trees.Sum(tree =>
                    tree.Contains(mine) ? tree.Sum(site => Utils.GetSquaredMineDistance(map.sites, mine, site)) : 0));
        }

        public int ComputeLiberty(IEnumerable<River> rivers)
        {
            var myPoints = this.Trees.Sum(tree => tree.Count);

            return myPoints + this.Trees
                .SelectMany(tree => Utils.BreadthFirstSearch(tree, rivers, 2).Select(bfs => bfs.site))
                .Distinct()
                .Count();
        }

        public bool Contains(int site)
        {
            return Trees.Any(tree => tree.Contains(site));
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

        public static IEnumerable<River> ConvertMovesToRivers(
            Map map,
            IEnumerable<Move> moves,
            Func<int, bool> idFilter)
        {
            var validRivers = map.rivers.ToDictionary(river => river, river => true);

            Func<int, int, River> createRiver = (source, target) =>
            {
                River ans = new River() { source = source, target = target };
                return validRivers.ContainsKey(ans) ? ans : new River() { source = target, target = source };
            };

            foreach (var move in moves)
            {
                if (move.claim != null)
                {
                    if (idFilter(move.claim.punter))
                    {
                        yield return createRiver(move.claim.source, move.claim.target);
                    }
                }
                else if (move.option != null)
                {
                    if (idFilter(move.option.punter))
                    {
                        yield return createRiver(move.option.source, move.option.target);
                    }
                }
                else if (move.splurge != null)
                {
                    if (idFilter(move.splurge.punter))
                    {
                        var rivers = move.splurge.route.Zip(
                            move.splurge.route.Skip(1),
                            (source, target) => createRiver(source, target));

                        foreach (var river in rivers)
                        {
                            yield return river;
                        }
                    }
                }
            }
        }
    }
}
