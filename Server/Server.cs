using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Icfp2017
{
    class Output
    {
        public int score;
        public List<int> scores;
        public string map;
        public VerboseOutput verbose;
    }

    class VerboseOutput
    {
        public List<Move> moves;
    }

    class Program
    {
        static readonly string RootPath = @"C:\Users\cashto\Documents\GitHub\icfp2017\work";

        static void Main(string[] args)
        {
            var mapFile = Path.Combine(RootPath, "maps", args[0] + ".json");

            var ais = args.Skip(1).ToList();

            var map = JsonConvert.DeserializeObject<Map>(File.ReadAllText(mapFile));

            //Utils.ComputeMineDistances(map);

            var states = Enumerable.Range(0, ais.Count)
                .Select(idx => RunAi<ReadyMessage>(ais[idx],
                    new ServerMessage()
                    {
                        punter = idx,
                        punters = ais.Count,
                        map = map
                    }).state)
                .ToList();

            var moves = Enumerable.Range(0, ais.Count)
                .Select(idx => new Move() { pass = new PassMove() { punter = idx } })
                .ToList();

            foreach (var moveNumber in Enumerable.Range(0, map.rivers.Count))
            {
                Console.Error.WriteLine(moveNumber);

                var aiIdx = moveNumber % ais.Count;

                var move = RunAi<Move>(ais[aiIdx],
                    new ServerMessage()
                    {
                        move = new MoveMessage()
                        {
                            moves = moves
                        },
                        state = states[aiIdx]
                    });

                states[aiIdx] = move.state;
                move.state = null;
                moves.Add(move);
            }

            var allTrees = Utils.ComputeTrees(moves);
            Utils.ComputeMineDistances(map);

            var scores = Enumerable.Range(0, ais.Count)
                .Select(idx =>
                {
                    var trees = allTrees.ContainsKey(idx) ? allTrees[idx] : Utils.EmptyTreeList;

                    return map.mines.Sum(mine =>
                        trees.Sum(tree =>
                            tree.Contains(mine) ? tree.Sum(site => Utils.GetSquaredMineDistance(map.sites, mine, site)) : 0));
                })
                .ToList();

            var output = new Output()
            {
                score = Math.Sign(scores[0] - scores[1]),
                scores = scores,
                map = args[0],
                verbose = new VerboseOutput()
                {
                    moves = moves
                }
            };

            Console.WriteLine(JsonConvert.SerializeObject(output, Formatting.Indented, Parser.SerializerSettings));
        }

        static T RunAi<T>(
            string ai,
            ServerMessage input)
        {
            //using (var dbgWriter = new StreamWriter("debug"))
            //{
            //    var dbgParser = new Parser(null, dbgWriter);
            //    dbgParser.Write(input);
            //}

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(RootPath, "ai", ai, "punter.exe"),
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true,
            };

            var tcs = new TaskCompletionSource<object>();
            process.Exited += (sender, args) => tcs.TrySetResult(null);

            process.Start();

            var parser = new Parser(process.StandardOutput, process.StandardInput);

            var me = parser.Read<MeMessage>();
            parser.Write(new YouMessage() { you = me.me });

            parser.Write(input);
            process.StandardInput.Close();
            var ans = parser.Read<T>();
            tcs.Task.Wait();
            return ans;
        }
    }
}
