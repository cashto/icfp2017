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
            Strategy strategy = RandomStrategy;

            if (debug)
            {
                parser = new Parser(
                    new StreamReader(@"C:\Users\cashto\Documents\GitHub\icfp2017\Server\bin\Debug\debug"), 
                    Console.Out);
            }
            else
            {
                parser = new Parser(Console.In, Console.Out);
                parser.Write(new MeMessage() { me = "cashto" });
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

        delegate River Strategy(ServerMessage message, List<River> availableRivers);

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

            var ans = strategy(message, availableRivers);

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
            ServerMessage message,
            List<River> availableRivers)
        {
            var seed = 0xdeadbeef;
            return availableRivers[new Random((int)seed).Next() % availableRivers.Count];
        }

        static River GreedyStrategy(
            ServerMessage message,
            List<River> availableRivers)
        {
            var state = message.state.ToObject<SolverState>();

            var myId = state.initialState.punter.Value;

            var treeSet = new TreeSet(message.move.moves);

            Func<River, int> ComputeRiverScore = (river) =>
            {
                var newTreeSet = treeSet.AddRiver(myId, river);
                return newTreeSet.Score(myId, state.initialState.map);
            };

            return availableRivers
                .Select(river => new { river = river, score = ComputeRiverScore(river) })
                .OrderByDescending(riverScore => riverScore.score)
                .Select(riverScore => riverScore.river)
                .First();
        }
        
        static River LightningStrategy(
            ServerMessage message,
            List<River> availableRivers)
        {
            return new River();
        }
    }
}
