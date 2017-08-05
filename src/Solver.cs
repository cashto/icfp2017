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

            var debug = false;
            Strategy strategy = LightningStrategy;

            if (debug)
            {
                parser = new Parser(
                    new StreamReader(@"C:\Users\cashto\Documents\GitHub\icfp2017\Server\bin\Debug\debug60"),
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
        
        static River LightningStrategy(
            MoveMessage message,
            ServerMessage initialState,
            List<River> availableRivers)
        {
            var earlyGame = (message.moves.Count - 2) * 10 < initialState.map.rivers.Count;
            if (!earlyGame)
            {
                return GreedyStrategy(message, initialState, availableRivers);
            }

            var myId = initialState.punter.Value;
            var map = initialState.map;
            var originalTreeSet = new TreeSet(message.moves);
            // var originalScore = originalTreeSet.ComputeScore(myId, map);
            var originalLiberty =
                originalTreeSet.ComputeLiberty(map, availableRivers, id => id != myId)
                ; // -originalTreeSet.ComputeLiberty(map.mines, availableRivers, id => id == myId);

            var analysis = availableRivers.Select(river =>
                {
                    var newTreeSet = originalTreeSet.AddRiver(myId, river);
                    var newLiberty =
                        originalTreeSet.ComputeLiberty(map, availableRivers.Where(i => i.source != river.source || i.target != river.target), id => id != myId)
                        ; // -newTreeSet.ComputeLiberty(map.mines, availableRivers, id => id == myId);
                    return new
                    {
                        river = river,
                        // scoreDelta = newTreeSet.ComputeScore(myId, map) - originalScore,
                        libertyDelta = newLiberty - originalLiberty
                    };
                })
            .ToList();

            // Future: make this less than O(n^2) (probably doesn't matter because code that generates
            // analysis is O(n^2)
            var rankedAnalysis = analysis
                .Select(i => new
                {
                    river = i.river,
                    score =
                        // analysis.Count(j => i.scoreDelta >= j.scoreDelta) +
                        analysis.Count(j => i.libertyDelta >= j.libertyDelta),
                    // scoreDelta = i.scoreDelta,
                    libertyDelta = i.libertyDelta
                })
                .OrderByDescending(i => i.score)
                .ToList();

            return rankedAnalysis.First().river;
        }
    }
}
