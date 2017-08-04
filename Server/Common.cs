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
    class SetupMessage
    {
        int punter;
        int punters;
        Map map;
    }

    class SetupResponse
    {
        int ready;
        JObject state;
    }

    class Map
    {
        List<Site> sites;
        List<River> rivers;
        List<int> mines;
    }

    class Site
    {
        int id;
    }

    class River
    {
        int source;
        int target;
    }

    class ServerMessage
    {
        MoveMessage move;
        StopMessage stop;
        int? timeout;
    }

    class MoveMessage
    {
        List<Move> moves;
        JObject state;
    }

    class StopMessage
    {
        List<Move> moves;
        List<Score> scores;
    }

    class Move
    {
        ClaimMove claim;
        PassMove pass;
    }

    class PassMove
    {
        int punter;
    }

    class ClaimMove : PassMove
    {
        int source;
        int target;
    }

    class Score
    {
        int punter;
        int score;
    }

    class Parser
    {
        public static IEnumerable<string> ReadCommands(TextReader reader)
        {
            int readChar;
            var sb = new StringBuilder();

            while ((readChar = reader.Read()) != -1)
            {
                var ch = (char)readChar;
                if (ch == ':')
                {
                    var n = int.Parse(sb.ToString());
                    var buffer = new char[n];
                    reader.ReadBlock(buffer, 0, n);
                    yield return new string(buffer);
                    sb.Clear();
                }
                else if (char.IsDigit(ch))
                {
                    sb.Append(ch);
                }
            }
        }
    }
}
