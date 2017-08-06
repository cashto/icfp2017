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
        public List<List<River>> chokes;
    }

    class Program
    {
        static void Main(string[] args)
        {
            Parser parser;
            string debug = null;

            //debug = @"C:\Users\cashto\Documents\GitHub\icfp2017\work\\debug1";
            Strategy strategy = V4Strategy;

            if (debug != null)
            {
                parser = new Parser(
                    new StreamReader(debug),
                    Console.Out);
            }
            else
            {
                parser = new Parser(Console.In, Console.Out) { debug = false };
                parser.Write(new MeMessage() { me = "Ich bau mir einen Prototyp" });
                parser.Read<YouMessage>();
            }

            var message = parser.Read<ServerMessage>();

            if (message.punter != null)
            {
                Utils.ComputeMineDistances(message.map);

                parser.Write(new SetupResponse()
                {
                    ready = message.punter.Value,
                    state = JObject.FromObject(
                        new SolverState() { initialState = message, chokes = FindChokes(message.map) },
                        new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore })
                });
            }
            else if (message.move != null)
            {
                parser.Write(ComputeMove(message, strategy));
            }
        }

        delegate River Strategy(
            MoveMessage message,
            SolverState solverState,
            List<River> availableRivers);

        static Move ComputeMove(
            ServerMessage message,
            Strategy strategy)
        {
            var state = message.state.ToObject<SolverState>();

            var myId = state.initialState.punter.Value;

            var rivers = state.initialState.map.rivers;

            var takenRivers = message.move.moves
                .Where(move => move.claim != null)
                .ToDictionary(
                    move => new River()
                    {
                        source = move.claim.source,
                        target = move.claim.target
                    },
                    move => true);

            var availableRivers = rivers
                .Where(river => !takenRivers.ContainsKey(river))
                .ToList();

            var ans = strategy(message.move, state, availableRivers);

            return new Move()
            {
                claim = new ClaimMove()
                {
                    punter = myId,
                    source = ans.source,
                    target = ans.target,
                },
                state = message.state
            };
        }
        
        static River V4Strategy(
            MoveMessage message,
            SolverState solverState,
            List<River> availableRivers)
        {
            var myId = solverState.initialState.punter.Value;

            // if there is a choke that hasn't been taken yet, grab it
            var choke = solverState.chokes
                .FirstOrDefault(i => i.Count == 1 && availableRivers.Contains(i[0]));
            if (choke != null)
            {
                return choke[0];
            }

            // otherwise play a move that brings two close big trees closer together (TODO)
            var treeSet = new TreeSet(message.moves);
            var trees = treeSet.GetTrees(i => i == myId);
            var adjMap = Utils.BuildAdjacencyMap(solverState.initialState.map.rivers);

            // otherwise just play a move that increases the number of neighbors or adds more points
            return availableRivers
                .Select(river => new
                {
                    river = river,
                    neighbors = CountNewNeighbors(river, trees, adjMap),
                    score = CountNewPoints(river, trees, solverState.initialState.map)
                })
                .OrderByDescending(i => i.neighbors)
                .ThenByDescending(i => i.score)
                .Select(i => i.river)
                .First();
        }

        static int CountNewNeighbors(
            River river,
            List<HashSet<int>> trees,
            Dictionary<int, HashSet<int>> adjMap)
        {
            bool sourceInTree = trees.Any(i => i.Contains(river.source));
            bool targetInTree = trees.Any(i => i.Contains(river.target));

            if (sourceInTree && targetInTree || !sourceInTree && !targetInTree)
            {
                return 0;
            }

            var newSite = sourceInTree ? river.target : river.source;

            return adjMap[newSite].Count(site => !trees.Any(tree => tree.Contains(site)));
        }

        static int CountNewPoints(
            River river,
            List<HashSet<int>> trees,
            Map map)
        {
            bool sourceInTree = trees.Any(i => i.Contains(river.source));
            bool targetInTree = trees.Any(i => i.Contains(river.target));

            if (sourceInTree && targetInTree || !sourceInTree && !targetInTree)
            {
                return 0;
            }

            var oldSite = sourceInTree ? river.source : river.target;
            var newSite = sourceInTree ? river.target : river.source;
            var whichTree = trees.First(tree => tree.Contains(oldSite));

            return map.mines
                .Where(mine => whichTree.Contains(mine))
                .Select(mine => Utils.GetSquaredMineDistance(map.sites, mine, newSite))
                .DefaultIfEmpty(0)
                .Sum();
        }

        static List<List<River>> FindChokes(Map map)
        {
            var minePairs =
                from mine1 in map.mines
                from mine2 in map.mines
                where mine1 < mine2
                select Tuple.Create(mine1, mine2);

            var shortestPaths = minePairs
                .Select(minePair => new
                {
                    minePair = minePair,
                    shortestPath = Utils.FindShortestPath(
                        new HashSet<int>() { minePair.Item1 },
                        new HashSet<int>() { minePair.Item2 },
                        map.rivers)
                })
                .Where(i => i.shortestPath != null)
                .Where(i => i.shortestPath.Count > 1)
                .OrderBy(i => i.shortestPath.Count)
                .Take(map.mines.Count)
                .ToList();

            var bestChokes = shortestPaths
                .Select(i => FindBestChoke(
                    map, 
                    new HashSet<int>() { i.minePair.Item1 },
                    new HashSet<int>() { i.minePair.Item2 },
                    i.shortestPath))
                .Where(i => i.Item2 > 1.3)
                .GroupBy(i => i.Item1)
                .OrderByDescending(group => group.Sum(i => i.Item2))
                .ToList();

            return bestChokes
                .Select(group => new List<River>() { group.Key })
                .ToList();
        }

        static Tuple<River, double> FindBestChoke(
            Map map,
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
                    return map.rivers.Contains(river) ? river : new River() { source = target, target = source };
                });

            var bestRiver = rivers
                .Select(river => new
                {
                    river = river,
                    shortestPath = lengthOrDefault(Utils.FindShortestPath(sourceMine, targetMine, map.rivers.Where(i => !i.Equals(river))))
                })
                .OrderBy(i => shortestPath)
                .First();

            return Tuple.Create(bestRiver.river, (double)bestRiver.shortestPath / shortestPath.Count);
        }
    }
}
