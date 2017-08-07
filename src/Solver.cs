using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Icfp2017
{
    class SolverState
    {
        public ServerMessage initialState;
        public List<Move> moves;
    }

    class Program
    {
        static void Main(string[] args)
        {
            string debug = null;
            // debug = @"C:\Users\cashto\Documents\GitHub\icfp2017\work\\debug0";

            Parser parser;
            var onlineMode = args.Length > 0;

            if (debug != null)
            {
                parser = new Parser(new StreamReader(debug), Console.Out);
            }
            else
            {
                if (!onlineMode)
                {
                    parser = new Parser(Console.In, Console.Out);
                }
                else
                {
                    var tcpClient = new TcpClient() { NoDelay = true };
                    tcpClient.Connect("punter.inf.ed.ac.uk", int.Parse(args[0]));
                    var stream = tcpClient.GetStream();
                    parser = new Parser(new StreamReader(stream), new StreamWriter(stream)) { debug = true };
                }

                parser.Write(new MeMessage() { me = "Prototyp by cashto" });
                parser.Read<YouMessage>();
            }

            JObject savedState = null;
            
            do
            {
                var message = parser.Read<ServerMessage>();

                if (message.move != null)
                {
                    // Make a move
                    try
                    {
                        var state = (onlineMode ? savedState : message.state).ToObject<SolverState>();

                        var myId = state.initialState.punter.Value;
                        var punters = state.initialState.punters.Value;

                        foreach (var idx in Enumerable.Range(myId, punters))
                        {
                            var punter = idx % punters;

                            var lastMove = message.move.moves.First(move =>
                                move.claim != null && move.claim.punter == punter ||
                                move.option != null && move.option.punter == punter ||
                                move.splurge != null && move.splurge.punter == punter ||
                                move.pass != null && move.pass.punter == punter);

                            state.moves.Add(lastMove);
                        }

                        var ans = V4Strategy(message.move, state);

                        savedState = ans.state = JObject.FromObject(
                            state,
                            new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore });

                        parser.Write(ans);
                    }
                    catch (Exception e)
                    {
                        Log(10000, $"[{e.ToString()}]");
                        Console.Error.WriteLine(e.ToString());
                    }
                }
                else if (message.punter != null)
                {
                    // Initial setup
                    message.settings = message.settings ?? new Settings();

                    var response = new SetupResponse()
                    {
                        ready = message.punter.Value,
                        state = JObject.FromObject(
                            new SolverState() { initialState = message, moves = new List<Move>() },
                            new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore })
                    };

                    parser.Write(response);

                    savedState = response.state;
                }
                else if (message.stop != null)
                {
                    break;
                }
            } while (onlineMode);
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

            var deadLine = startTime.AddMilliseconds(900);

            var myId = solverState.initialState.punter.Value;

            var initialMap = solverState.initialState.map;

            var takenRivers = Utils.ConvertMovesToRivers(initialMap, solverState.moves, (id) => true)
                .ToLookup(river => river, river => true);

            var availableRivers = initialMap.rivers
                .Where(river => !takenRivers.Contains(river))
                .ToList();

            var canUseOptions =
                solverState.initialState.settings.options &&
                solverState.moves.Count(move => move.option != null && move.option.punter == myId) < initialMap.mines.Count;

            var adjacencyMap = new AdjacencyMap(initialMap.rivers);

            var mineDistances = new MineDistances(initialMap, adjacencyMap);

            var setupDoneTime = DateTime.UtcNow;

            // if we see a choke, take it
            var chokeFindTask = Task.Run(() =>
            {
                var punters = new List<int>() { myId };
                if (solverState.initialState.punters.Value == 2)
                {
                    punters.Add(1 - myId);
                }

                foreach (var punter in punters)
                {
                    var availableOptions = new List<River>();
                    if (canUseOptions && punter == myId)
                    {
                        var takenOptions = Utils.ConvertMovesToRivers(initialMap, solverState.moves.Where(move => move.option != null), (id) => true)
                            .ToLookup(river => river, river => true);

                        availableOptions =
                            Utils.ConvertMovesToRivers(initialMap, solverState.moves, (id) => id != myId)
                            .Where(river => !takenOptions.Contains(river))
                            .ToList();
                    }

                    var chokes = FindChokes(
                        initialMap.mines,
                        Utils.ConvertMovesToRivers(initialMap, solverState.moves, (id) => id == punter).ToList(),
                        availableRivers.Concat(availableOptions).ToList(),
                        deadLine.AddMilliseconds(-100));

                    if (chokes.Any())
                    {
                        var river = chokes[0][0];

                        var chokeAnalysisDoneTime = DateTime.UtcNow;

                        Log(myId, string.Format("[{0}] [{1}] [{2}/{3}]",
                            punter == myId ? "TakeChoke " : "BlockChoke",
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
                Utils.ConvertMovesToRivers(initialMap, solverState.moves, (id) => id == myId),
                initialMap.mines);

            var riversToConsider = availableRivers
                .Where(river => trees.Contains(river.source) || trees.Contains(river.target))
                .DefaultIfEmpty(availableRivers.First());

            var riversConsidered =
                from river in riversToConsider
                where DateTime.UtcNow < deadLine
                let newTrees = trees.AddRiver(river)
                let treeCount = newTrees.Trees.Count
                let liberty = newTrees.ComputeLiberty(availableRivers, adjacencyMap)
                let score = newTrees.ComputeScore(mineDistances)
                select new { river = river, liberty = liberty, score = score, treeCount = treeCount };

            var rankedRivers = riversConsidered
                .ToList()
                .OrderBy(i => i.treeCount)
                .ThenByDescending(i => i.liberty)
                .ThenByDescending(i => i.score);

            var ans = CreateClaimMove(myId, rankedRivers.First().river);

            var analysisDoneTime = DateTime.UtcNow;

            var zero = TimeSpan.FromTicks(0);
            var waitTime = deadLine - DateTime.UtcNow;
            waitTime = waitTime < zero ? zero : waitTime;
            chokeFindTask.Wait(waitTime);

            if (chokeFindTask.IsCompleted && chokeFindTask.Result != null)
            {
                return chokeFindTask.Result;
            }

            var doneTime = DateTime.UtcNow;

            Log(myId, string.Format("[NormalMove] [{0}] [{1}/{2}/{3}] [Trees:{4}] [Liberties:{5}] [Score:{6}]",
                (int)(doneTime - startTime).TotalMilliseconds,
                (int)(setupDoneTime - startTime).TotalMilliseconds,
                (int)(analysisDoneTime - setupDoneTime).TotalMilliseconds,
                (int)(doneTime - analysisDoneTime).TotalMilliseconds,
                rankedRivers.First().treeCount,
                rankedRivers.First().liberty,
                rankedRivers.First().score));

            return ans;
        }

        static List<List<River>> FindChokes(
            List<int> mines,
            List<River> myRivers,
            List<River> availableRivers,
            DateTime deadLine)
        {
            var trees = new TreeSet(myRivers, mines);

            var treePairs =
                from tree1 in trees.Trees
                from tree2 in trees.Trees
                where tree1.First() < tree2.First()
                select Tuple.Create(tree1, tree2);

            var allRivers = availableRivers.Concat(myRivers);

            var allRiversAdjacencyMap = new AdjacencyMap(allRivers);

            var shortestPaths = treePairs
                .Select(treePair => new
                {
                    treePair = treePair,
                    shortestPath = DateTime.UtcNow > deadLine ? null : Utils.FindShortestPath(
                        treePair.Item1,
                        treePair.Item2,
                        allRivers,
                        allRiversAdjacencyMap)
                })
                .Where(i => i.shortestPath != null)
                .OrderBy(i => i.shortestPath.Count)
                // .Take(map.mines.Count)
                .ToList();

            var bestChokes = shortestPaths
                .Select(i => DateTime.UtcNow > deadLine ? null : FindBestChoke(
                    allRivers,
                    allRiversAdjacencyMap,
                    availableRivers,
                    i.treePair.Item1,
                    i.treePair.Item2,
                    i.shortestPath))
                .Where(i => i != null && i.Item2 > 1.3)
                .GroupBy(i => i.Item1)
                .OrderByDescending(group => group.Sum(i => i.Item2))
                .ToList();

            return bestChokes
                .Select(group => new List<River>() { group.Key })
                .ToList();
        }

        static Tuple<River, double> FindBestChoke(
            IEnumerable<River> allRivers,
            AdjacencyMap allRiversAdjacencyMap,
            List<River> availableRivers,
            HashSet<int> sourceMine,
            HashSet<int> targetMine,
            List<int> shortestPath)
        {
            Func<List<int>, int> lengthOrDefault = (list) => list == null ? 10000 : list.Count;

            var rivers = shortestPath.Zip(
                shortestPath.Skip(1), 
                (source, target) =>
                {
                    var river = new River() { source = source, target = target };
                    return allRivers.Contains(river) ? river : new River() { source = target, target = source };
                });

            var bestRivers = availableRivers
                .Select(river =>
                {
                    bool removedItem = allRiversAdjacencyMap.Remove(river);

                    var ans = new
                    {
                        river = river,
                        shortestPath = lengthOrDefault(
                            Utils.FindShortestPath(
                                sourceMine,
                                targetMine,
                                allRivers.Where(i => !i.Equals(river)),
                                allRiversAdjacencyMap))
                    };

                    if (removedItem)
                    {
                        allRiversAdjacencyMap.Add(river);
                    }

                    return ans;
                })
                .OrderByDescending(i => i.shortestPath);

            var bestRiver = bestRivers.First();

            return Tuple.Create(bestRiver.river, (double)bestRiver.shortestPath / shortestPath.Count);
        }

        static void Log(int myId, string s)
        {
            //using (var file = File.AppendText($"prototyp-{myId}.log"))
            //{
            //    file.WriteLine(s);
            //}
        }
    }
}
