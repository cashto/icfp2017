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
    class SolverState
    {
        public ServerMessage initialState;
    }

    class Program
    {
        static void Main(string[] args)
        {
            Parser parser;
            string debug = null;

            // debug = @"C:\Users\cashto\Documents\GitHub\icfp2017\work\\debug130";

            if (debug != null)
            {
                parser = new Parser(
                    new StreamReader(debug),
                    Console.Out);
            }
            else
            {
                parser = new Parser(Console.In, Console.Out) { debug = false };
                parser.Write(new MeMessage() { me = "Prototyp by cashto" });
                parser.Read<YouMessage>();
            }

            var message = parser.Read<ServerMessage>();

            if (message.move != null)
            {
                // Make a move
                var state = message.state.ToObject<SolverState>();
                var ans = V4Strategy(message.move, state);
                ans.state = message.state;
                parser.Write(ans);
            }
            else if (message.punter != null)
            {
                // Initial setup
                message.settings = message.settings ?? new Settings();

                parser.Write(new SetupResponse()
                {
                    ready = message.punter.Value,
                    state = JObject.FromObject(
                        new SolverState() { initialState = message },
                        new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore })
                });
            }
        }

        static Move CreateClaimMove(
            int myId, 
            River river)
        {
            return new Move()
            {
                claim = new ClaimMove()
                {
                    punter = myId,
                    source = river.source,
                    target = river.target,
                }
            };
        }

        static Move CreateOptionMove(
            int myId,
            River river)
        {
            return new Move()
            {
                option = new OptionMove()
                {
                    punter = myId,
                    source = river.source,
                    target = river.target,
                }
            };
        }

        static Move V4Strategy(
            MoveMessage message,
            SolverState solverState)
        {
            var startTime = DateTime.UtcNow;

            var myId = solverState.initialState.punter.Value;

            var initialMap = solverState.initialState.map;

            var takenRivers = Utils.ConvertMovesToRivers(initialMap, message.moves, (id) => true)
                .ToLookup(river => river, river => true);

            var availableRivers = initialMap.rivers
                .Where(river => !takenRivers.Contains(river))
                .ToList();

            var canUseOptions =
                solverState.initialState.settings.options &&
                message.moves.Count(move => move.option != null && move.option.punter == myId) < initialMap.mines.Count;

            var adjacencyMap = Utils.BuildAdjacencyMap(initialMap.rivers);

            var mineDistances = new MineDistances(initialMap, adjacencyMap);

            var setupDoneTime = DateTime.UtcNow;

            // if we see a choke, take it
            var chokeFindTask = Task.Run(() =>
            {
                return null;

                var punters = 
                    new List<int>() { myId }.Concat(
                    Enumerable.Range(0, solverState.initialState.punters.Value).Where(i => i != myId));

                foreach (var punter in punters)
                {
                    var availableOptions = new List<River>();
                    if (canUseOptions && punter == myId)
                    {
                        var takenOptions = Utils.ConvertMovesToRivers(initialMap, message.moves.Where(move => move.option != null), (id) => true)
                            .ToLookup(river => river, river => true);

                        availableOptions =
                            Utils.ConvertMovesToRivers(initialMap, message.moves, (id) => id != myId)
                            .Where(river => !takenOptions.Contains(river))
                            .ToList();
                    }

                    var chokes = FindChokes(
                        initialMap.mines,
                        Utils.ConvertMovesToRivers(initialMap, message.moves, (id) => id == punter).ToList(),
                        availableRivers.Concat(availableOptions).ToList(),
                        adjacencyMap);

                    if (chokes.Any())
                    {
                        var river = chokes[0][0];

                        var chokeAnalysisDoneTime = DateTime.UtcNow;

                        Log(myId, string.Format("[{0}] [{1}] [{2}/{3}]",
                            punter == myId ? "TakeChoke" : "BlockChoke",
                            (int)(chokeAnalysisDoneTime - startTime).TotalMilliseconds,
                            (int)(setupDoneTime - startTime).TotalMilliseconds,
                            (int)(chokeAnalysisDoneTime - setupDoneTime).TotalMilliseconds));

                        return availableOptions.Contains(river) ? CreateOptionMove(myId, river) : CreateClaimMove(myId, river);
                    }
                }

                return null;
            });

            // otherwise just play a move that joins two trees, increases liberty, or increases score
            var trees = new TreeSet(
                Utils.ConvertMovesToRivers(initialMap, message.moves, (id) => id == myId),
                initialMap.mines);

            var riversToConsider = availableRivers
                .Where(river => trees.Contains(river.source) || trees.Contains(river.target))
                .DefaultIfEmpty(availableRivers.First());

            var rankedRivers =
                from river in riversToConsider
                let newTrees = trees.AddRiver(river)
                let treeCount = newTrees.Trees.Count
                let liberty = newTrees.ComputeLiberty(availableRivers, adjacencyMap)
                let score = newTrees.ComputeScore(mineDistances)
                orderby treeCount, liberty descending, score descending
                select new { river = river, liberty = liberty, score = score, treeCount = treeCount };

            var ans = CreateClaimMove(myId, rankedRivers.First().river);

            var analysisDoneTime = DateTime.UtcNow;

            if (chokeFindTask.Result != null)
            {
                return chokeFindTask.Result;
            }

            var doneTime = DateTime.UtcNow;

            Log(myId, string.Format("[Normal] [{0}] [{1}/{2}/{3}]",
                (int)(doneTime - startTime).TotalMilliseconds,
                (int)(setupDoneTime - startTime).TotalMilliseconds,
                (int)(analysisDoneTime - setupDoneTime).TotalMilliseconds,
                (int)(doneTime - analysisDoneTime).TotalMilliseconds));

            return ans;
        }

        static List<List<River>> FindChokes(
            List<int> mines,
            List<River> myRivers,
            List<River> availableRivers,
            Dictionary<int, HashSet<int>> adjacencyMap)
        {
            var trees = new TreeSet(myRivers, mines);

            var treePairs =
                from tree1 in trees.Trees
                from tree2 in trees.Trees
                where tree1.First() < tree2.First()
                select Tuple.Create(tree1, tree2);

            var shortestPaths = treePairs
                .Select(treePair => new
                {
                    treePair = treePair,
                    shortestPath = Utils.FindShortestPath(
                        treePair.Item1,
                        treePair.Item2,
                        availableRivers.Concat(myRivers),
                        adjacencyMap)
                })
                .Where(i => i.shortestPath != null)
                .OrderBy(i => i.shortestPath.Count)
                // .Take(map.mines.Count)
                .ToList();

            var bestChokes = shortestPaths
                .Select(i => FindBestChoke(
                    myRivers,
                    availableRivers,
                    i.treePair.Item1,
                    i.treePair.Item2,
                    i.shortestPath,
                    adjacencyMap))
                .Where(i => i.Item2 > 1.3)
                .GroupBy(i => i.Item1)
                .OrderByDescending(group => group.Sum(i => i.Item2))
                .ToList();

            return bestChokes
                .Select(group => new List<River>() { group.Key })
                .ToList();
        }

        static Tuple<River, double> FindBestChoke(
            List<River> myRivers,
            List<River> availableRivers,
            HashSet<int> sourceMine,
            HashSet<int> targetMine,
            List<int> shortestPath,
            Dictionary<int, HashSet<int>> adjacencyMap)
        {
            var allRivers = myRivers.Concat(availableRivers);

            Func<List<int>, int> lengthOrDefault = (list) => list == null ? 10000 : list.Count;

            var rivers = shortestPath.Zip(
                shortestPath.Skip(1), 
                (source, target) =>
                {
                    var river = new River() { source = source, target = target };
                    return allRivers.Contains(river) ? river : new River() { source = target, target = source };
                });

            var bestRivers = availableRivers
                .Select(river => new
                {
                    river = river,
                    shortestPath = lengthOrDefault(
                        Utils.FindShortestPath(
                            sourceMine, 
                            targetMine, 
                            allRivers.Where(i => !i.Equals(river)),
                            adjacencyMap))
                })
                .OrderByDescending(i => i.shortestPath);

            var bestRiver = bestRivers.First();

            return Tuple.Create(bestRiver.river, (double)bestRiver.shortestPath / shortestPath.Count);
        }

        static void Log(int myId, string s)
        {
            using (var file = File.AppendText($"prototyp-{myId}.log"))
            {
                file.WriteLine(s);
            }
        }
    }
}
