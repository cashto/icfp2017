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

            var states = Enumerable.Range(0, ais.Count)
                .Select(idx => RunAi<ReadyMessage>(
                    0,
                    ais[idx],
                    new ServerMessage()
                    {
                        punter = idx,
                        punters = ais.Count,
                        map = map,
                        settings = new Settings()
                        {
                            options = true
                        }
                    }).state)
                .ToList();

            var moves = Enumerable.Range(0, ais.Count)
                .Select(idx => new Move() { pass = new PassMove() { punter = idx } })
                .ToList();

            foreach (var moveNumber in Enumerable.Range(0, map.rivers.Count))
            {
                var aiIdx = moveNumber % ais.Count;

                var ch =
                    aiIdx == 0 ? '.' :
                    aiIdx == 1 ? ':' :
                    '*';

                Console.Error.Write(ch);
                Console.Error.Flush();

                var move = RunAi<Move>(
                    moveNumber,
                    ais[aiIdx],
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

            var mineDistances = new MineDistances(map, new AdjacencyMap(map.rivers));

            var scores = Enumerable.Range(0, ais.Count)
                .Select(idx => (new TreeSet(Utils.ConvertMovesToRivers(map, moves, (id) => id == idx))).ComputeScore(mineDistances))
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

            Console.Error.WriteLine();
            Console.WriteLine(JsonConvert.SerializeObject(output, Formatting.Indented, Parser.SerializerSettings));
        }

        static T RunAi<T>(
            int moveNumber,
            string ai,
            ServerMessage input)
        {
            //Console.Error.WriteLine($"+RunAi({moveNumber}, {ai}, ServerMessage)");

            using (var dbgWriter = new StreamWriter($"debug{moveNumber}"))
            {
                new Parser(null, dbgWriter).Write(input, Formatting.Indented);
            }

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

            try
            {
                var me = parser.Read<MeMessage>();
                parser.Write(new YouMessage() { you = me.me });

                parser.Write(input);
                process.StandardInput.Close();
                var ans = parser.Read<T>();
                tcs.Task.Wait();

                return ans;
            }
            catch (Exception)
            {
                var stderr = process.StandardError.ReadToEnd();
                Console.Error.WriteLine($"\nException in RunAi({moveNumber}, {ai}, ServerMessage), StdErr = {stderr}------------------");
                throw;
            }
        }
    }
}
