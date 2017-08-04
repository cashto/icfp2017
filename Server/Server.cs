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
    class Program
    {
        static void Main(string[] args)
        {
            var mapFile = args[1];

            var ais = args.Skip(2).ToList();

            var map = JsonConvert.DeserializeObject<Map>(File.ReadAllText(mapFile));

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
                var aiIdx = moveNumber % ais.Count;

                var move = RunAi<Move>(ais[aiIdx],
                    new ServerMessage()
                    {
                        move = new MoveMessage()
                        {
                            moves = moves,
                            state = states[aiIdx]
                        }
                    });

                states[aiIdx] = move.state;
                move.state = null;
                moves.Add(move);
            }
        }

        static T RunAi<T>(
            string ai,
            ServerMessage input)
        {
            var path = @"C:\Users\cashto\Documents\GitHub\icfp2017\work\ai";

            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = path + @"\" + ai + @"\\solver.exe",
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
            tcs.Task.Wait();
            return parser.Read<T>();
        }
    }
}
