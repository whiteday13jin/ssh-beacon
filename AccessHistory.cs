using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Text.RegularExpressions;

namespace SshBeacon
{
    internal sealed class Login
    {
        internal DateTime Utc;
        internal string Address;
        internal string User;
        private static readonly Regex Accepted = new Regex(@"^Accepted \S+ for (.+?) from (\S+) port \d+ ssh\d+(?:[: ]|$)");

        internal static Login Parse(string payload, DateTime utc)
        {
            Match match = Accepted.Match(payload ?? "");
            IPAddress address;
            if (!match.Success || !IPAddress.TryParse(match.Groups[2].Value, out address)) return null;
            return new Login { Utc = utc, Address = address.ToString(), User = match.Groups[1].Value };
        }
    }

    internal sealed class LoginBatch
    {
        internal readonly List<Login> Items = new List<Login>();
        internal long LatestId;
        internal static LoginBatch Read(long afterId)
        {
            LoginBatch result = new LoginBatch();
            EventLogQuery query = new EventLogQuery("OpenSSH/Operational", PathType.LogName,
                "*[System[Provider[@Name='OpenSSH'] and TimeCreated[timediff(@SystemTime) <= 604800000]]]");
            query.ReverseDirection = true;
            using (EventLogReader reader = new EventLogReader(query))
            {
                while (true)
                {
                    using (EventRecord record = reader.ReadEvent())
                    {
                        if (record == null) break;
                        long id = record.RecordId.GetValueOrDefault();
                        if (result.LatestId == 0) result.LatestId = id;
                        if (id == afterId) break;
                        var fields = record.Properties;
                        if (fields.Count < 2 || !record.TimeCreated.HasValue) continue;
                        Login login = Login.Parse(fields[1].Value as string, record.TimeCreated.Value.ToUniversalTime());
                        if (login != null) result.Items.Add(login);
                        if (afterId == 0 && result.Items.Count >= 1000) break;
                    }
                }
            }
            result.Items.Reverse();
            return result;
        }
    }

    internal sealed class Visit
    {
        internal Login First;
        internal DateTime Last;
        internal int Count;
    }

    internal sealed class VisitHistory
    {
        internal readonly List<Visit> Recent = new List<Visit>();

        internal bool Add(Login login)
        {
            Visit previous = Recent.Find(v => v.First.Address == login.Address && v.First.User == login.User);
            bool fresh = previous == null || login.Utc - previous.Last > TimeSpan.FromSeconds(60);
            Visit visit = fresh ? new Visit { First = login } : previous;
            if (!fresh) Recent.Remove(visit);
            visit.Last = login.Utc;
            visit.Count++;
            Recent.Insert(0, visit);
            if (Recent.Count > 200) Recent.RemoveAt(200);
            return fresh;
        }
    }
}
