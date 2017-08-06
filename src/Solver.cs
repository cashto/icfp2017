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
                        new SolverState() { initialState = message },
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
            ServerMessage initialState,
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

            var ans = strategy(message.move, state.initialState, availableRivers);

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

        static River RandomStrategy(
            MoveMessage message,
            ServerMessage initialState,
            List<River> availableRivers)
        {
            var seed = 0xdeadbeef;
            return availableRivers[new Random((int)seed).Next() % availableRivers.Count];
        }

        static River GreedyStrategy(
            MoveMessage message,
            ServerMessage initialState,
            List<River> availableRivers)
        {
            var myId = initialState.punter.Value;

            var treeSet = new TreeSet(message.moves);

            Func<River, int> ComputeRiverScore = (river) =>
            {
                var newTreeSet = treeSet.AddRiver(myId, river);
                return newTreeSet.ComputeScore(myId, initialState.map);
            };

            return availableRivers
                .Select(river => new { river = river, score = ComputeRiverScore(river) })
                .OrderByDescending(riverScore => riverScore.score)
                .Select(riverScore => riverScore.river)
                .First();
        }

        static River V4Strategy(
            MoveMessage message,
            ServerMessage initialState,
            List<River> availableRivers)
        {
            var myId = initialState.punter.Value;

            // if it's early game, play a choke

            // play a move that brings two big trees together
            var treeSet = new TreeSet(message.moves);
            var trees = treeSet.GetTrees(i => i == myId);
            var adjMap = Utils.BuildAdjacencyMap(initialState.map.rivers);

            // play a move that increases the number of neighbors or adds more points
            return availableRivers
                .Select(river => new
                {
                    river = river,
                    neighbors = CountNewNeighbors(river, trees, adjMap),
                    score = CountNewPoints(river, trees, initialState.map)
                })
                .OrderByDescending(i => i.neighbors)
                .ThenByDescending(i => i.score)
                .Select(i => i.river)
                .First();

            //var earlyGame = (message.moves.Count - 2) * 10 < initialState.map.rivers.Count;
            //if (!earlyGame)
            //{
            //    return GreedyStrategy(message, initialState, availableRivers);
            //}

            //
            //var map = initialState.map;
            //var originalTreeSet = new TreeSet(message.moves);
            //var originalScore = originalTreeSet.ComputeScore(myId, map);
            //var originalLiberty =
            //    originalTreeSet.ComputeLiberty(map, availableRivers, id => id != myId)
            //    ; // -originalTreeSet.ComputeLiberty(map.mines, availableRivers, id => id == myId);

            //var analysis = availableRivers.Select(river =>
            //    {
            //        var newTreeSet = originalTreeSet.AddRiver(myId, river);
            //        var newLiberty =
            //            originalTreeSet.ComputeLiberty(map, availableRivers.Where(i => i.source != river.source || i.target != river.target), id => id != myId)
            //            ; // -newTreeSet.ComputeLiberty(map.mines, availableRivers, id => id == myId);
            //        return new
            //        {
            //            river = river,
            //            scoreDelta = newTreeSet.ComputeScore(myId, map) - originalScore,
            //            libertyDelta = (double)newLiberty / originalLiberty 
            //        };
            //    })
            //.ToList();

            //var bestForLiberty = analysis.OrderByDescending(i => i.libertyDelta).First();
            //if (bestForLiberty.libertyDelta > 1.1)
            //{
            //    return bestForLiberty.river;
            //}

            //var bestForScore = analysis.OrderByDescending(i => i.scoreDelta).ThenByDescending(i => i.libertyDelta).First();
            //return bestForScore.river;
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
    }
}
