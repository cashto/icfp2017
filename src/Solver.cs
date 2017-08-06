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

            //debug = @"C:\Users\cashto\Documents\GitHub\icfp2017\work\\debug85";

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
                        new SolverState() { initialState = message },
                        new JsonSerializer() { NullValueHandling = NullValueHandling.Ignore })
                });
            }
            else if (message.move != null)
            {
                parser.Write(ComputeMove(message, V4Strategy));
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

            var availableRivers = Utils.RemoveRivers(
                state.initialState.map.rivers,
                message.move.moves.Where(move => move.claim != null));

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
            var initialMap = solverState.initialState.map;

            // if we see a choke, take it
            var punters =
                new List<int>() { myId }.Concat(
                Enumerable.Range(0, solverState.initialState.punters.Value).Where(i => i != myId));

            foreach (var punter in punters)
            {
                var myRivers = message.moves
                    .Where(i => i.claim != null && i.claim.punter == punter)
                    .Select(i => new River { source = i.claim.source, target = i.claim.target })
                    .ToList();

                var chokes = FindChokes(initialMap.mines, myRivers, availableRivers);

                if (chokes.Any())
                {
                    return chokes[0][0];
                }
            }

            // otherwise play a move that brings two close big trees closer together (TODO)
            var trees = new TreeSet(message.moves, initialMap.mines, (claim) => claim.punter == myId);

            // otherwise just play a move that joins two trees, increases liberty, or increases score
            var riversToConsider = availableRivers
                .Where(river => trees.Contains(river.source) || trees.Contains(river.target))
                .DefaultIfEmpty(availableRivers.First());

            var rankedRivers =
                from river in riversToConsider
                let newTrees = trees.AddRiver(river)
                let treeCount = newTrees.Trees.Count
                let liberty = newTrees.ComputeLiberty(availableRivers)
                let score = newTrees.ComputeScore(initialMap)
                orderby treeCount, liberty descending, score descending
                select new { river = river, liberty = liberty, score = score, treeCount = treeCount };

            return rankedRivers.First().river;
        }

        static List<List<River>> FindChokes(
            List<int> mines,
            List<River> myRivers,
            List<River> availableRivers)
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
                        availableRivers.Concat(myRivers))
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
            List<River> myRivers,
            List<River> availableRivers,
            HashSet<int> sourceMine,
            HashSet<int> targetMine,
            List<int> shortestPath)
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
                    shortestPath = lengthOrDefault(Utils.FindShortestPath(sourceMine, targetMine, allRivers.Where(i => !i.Equals(river))))
                })
                .OrderByDescending(i => i.shortestPath);

            var bestRiver = bestRivers.First();

            return Tuple.Create(bestRiver.river, (double)bestRiver.shortestPath / shortestPath.Count);
        }
    }
}
