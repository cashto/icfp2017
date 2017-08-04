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
            var parser = new Parser(Console.In, Console.Out);

            parser.Write(new MeMessage() { me = "cashto" });

            parser.Read<YouMessage>();

            var message = parser.Read<ServerMessage>();

            if (message.punter != null)
            {
                ComputeMineDistances(message.map);

                parser.Write(new SetupResponse()
                {
                    ready = message.punter.Value,
                    state = new JObject(new SolverState() { initialState = message })
                });
            }
            else if (message.move != null)
            {
                parser.Write(ComputeMove(message, GreedyStrategy));
            }
        }

        static void ComputeMineDistances(Map map)
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
                var originalSize = ans.Count;

                foreach (var river in map.rivers)
                {
                    var sourceDistance = TryGetValue(ans, river.source);
                    var targetDistance = TryGetValue(ans, river.target);

                    if (sourceDistance.HasValue && !targetDistance.HasValue)
                    {
                        ans.Add(river.target, sourceDistance.Value + 1);
                    }
                    else if (!sourceDistance.HasValue && targetDistance.HasValue)
                    {
                        ans.Add(river.source, targetDistance.Value + 1);
                    }
                }

                if (originalSize == ans.Count)
                {
                    return ans;
                }
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

        static Dictionary<int, List<HashSet<int>>> ComputeTrees(
            MoveMessage message)
        {
            var ans = new Dictionary<int, List<HashSet<int>>>();

            var claims = message.moves
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
            return availableRivers[new Random().Next(0, availableRivers.Count)];
        }

        static River GreedyStrategy(
            ServerMessage message,
            List<River> availableRivers)
        {
            var state = message.state.ToObject<SolverState>();

            var myId = state.initialState.punter.Value;

            var trees = ComputeTrees(message.move)[myId];

            return availableRivers
                .Select(river => new { river = river, score = ComputeRiverScore(state.initialState, trees, river) })
                .OrderByDescending(riverScore => riverScore.score)
                .Select(riverScore => riverScore.river)
                .First();
        }

        static int ComputeRiverScore(
            ServerMessage initialState,
            List<HashSet<int>> trees,
            River river)
        {
            var mines = initialState.map.mines;

            var newSource = !trees.Any(tree => tree.Contains(river.source));
            var newTarget = !trees.Any(tree => tree.Contains(river.target));

            if (!newSource && !newTarget)
            {
                return 0;
            }

            if (newSource && newTarget)
            {
                return mines.Count(mine => mine == river.source || mine == river.target);
            }

            var newSite = newSource ? river.target : river.source;

            return mines.Sum(mine =>
                trees.Sum(tree => 
                    tree.Contains(mine) ? ComputeRiverScore(initialState, mine, newSite) : 0));
        }

        static int ComputeRiverScore(
            ServerMessage initialState,
            int mine,
            int newSite)
        {
            var ans = initialState.map.sites
                .First(site => site.id == newSite).mineDistances
                .First(mineDistance => mineDistance.mineId == mine).distance;

            return ans * ans;
        }
    }
}
