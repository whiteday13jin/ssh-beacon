using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace SshBeacon
{
    internal sealed class Transfer
    {
        internal DateTime Utc;
        internal string Device, Name, Action, LocalPath;

        private static readonly Regex Close = new Regex(@"^(forced )?close ""(.+)"" bytes read ([0-9]+) written ([0-9]+)$");

        internal static Transfer Parse(string line, DateTime utc, string device, string openFlags = null)
        {
            Match m = Close.Match(line);
            long sent, received;
            if (!m.Success || !long.TryParse(m.Groups[3].Value, out sent) ||
                !long.TryParse(m.Groups[4].Value, out received)) return null;
            string path = Regex.Replace(m.Groups[2].Value, @"(?:\\[0-3][0-7]{2})+", delegate(Match escaped)
            {
                byte[] bytes = new byte[escaped.Length / 4];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(escaped.Value.Substring(i * 4 + 1, 3), 8);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }).Replace('\\', '/');
            string action = sent > 0 && received > 0 ? "双向读写" : sent > 0 ? "发出文件" : "收到文件";
            if (sent == 0 && received == 0)
            {
                string[] flags = (openFlags ?? "").Split(',');
                bool read = Array.IndexOf(flags, "READ") >= 0, write = Array.IndexOf(flags, "WRITE") >= 0;
                action = (read == write ? "访问文件" : read ? "对方请求下载" : "对方请求上传") + "（0 字节）";
            }
            if (m.Groups[1].Success) action += "（连接中断）";
            string local = path.Replace('/', '\\');
            if (local.Length > 3 && local[0] == '\\' && char.IsLetter(local[1]) && local[2] == ':' && local[3] == '\\')
                local = local.Substring(1);
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                local = Path.GetFullPath(Path.Combine(home, local));
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
            return new Transfer { Utc = utc, Device = device, Name = path.Substring(path.LastIndexOf('/') + 1), Action = action, LocalPath = local };
        }

        internal static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SshBeacon", "Transfers"); } }
        internal static void Prune(string folder, DateTime now)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, "*.tsv"))
            {
                DateTime day;
                if (DateTime.TryParseExact(Path.GetFileName(file).Split('_')[0], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day) && day < now.ToLocalTime().Date.AddDays(-6))
                    File.Delete(file);
            }
        }
        private static readonly Regex SessionStart = new Regex(@"^session opened for local user (.+) from \[([^\]]+)\]$");

        private static readonly Regex Open = new Regex(@"^open ""(.+)"" flags ([A-Z,]+) mode ");
        internal static List<Transfer> ReadNative(HashSet<long> wanted = null)
        {
            var rows = new List<Transfer>();
            var peers = new Dictionary<int, string>();
            var modes = new Dictionary<int, Dictionary<string,string>>();
            string account;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                account = identity.Name;
            }
            string filter = "*[System[Provider[@Name='OpenSSH'] and TimeCreated[timediff(@SystemTime) <= 604800000]] and EventData[Data[@Name='process']='sftp-server']]";
            var query = new EventLogQuery("OpenSSH/Operational", PathType.LogName, filter);
            using (var reader = new EventLogReader(query))
            {
                while (true)
                {
                    using (EventRecord record = reader.ReadEvent())
                    {
                        if (record == null) break;
                        var fields = record.Properties;
                        if (fields.Count < 2 || !record.ProcessId.HasValue || !record.TimeCreated.HasValue) continue;
                        string message = fields[1].Value as string ?? "";
                        int pid = record.ProcessId.Value;
                        Match start = SessionStart.Match(message);
                        if (start.Success)
                        {
                            peers.Remove(pid);
                            modes.Remove(pid);
                            string user = start.Groups[1].Value;
                            if (user.IndexOf('\\') < 0) user = Environment.MachineName + "\\" + user;
                            if (!string.Equals(user, account, StringComparison.OrdinalIgnoreCase)) continue;
                            IPAddress address;
                            peers[pid] = IPAddress.TryParse(start.Groups[2].Value, out address) ? address.ToString() : "未知设备";
                            modes[pid] = new Dictionary<string,string>();
                        }
                        else if (message.StartsWith("session closed ", StringComparison.Ordinal))
                        {
                            peers.Remove(pid);
                            modes.Remove(pid);
                        }
                        else
                        {
                            string source;
                            if (!peers.TryGetValue(pid, out source)) continue;
                            Match open = Open.Match(message);
                            if (open.Success)
                            {
                                string key = open.Groups[1].Value;
                                modes[pid][key] = modes[pid].ContainsKey(key) ? "" : open.Groups[2].Value;
                                continue;
                            }
                            Match close = Close.Match(message);
                            if (!close.Success) continue;
                            string path = close.Groups[2].Value, flags;
                            modes[pid].TryGetValue(path, out flags);
                            modes[pid].Remove(path);
                            if (wanted != null && !wanted.Contains(record.RecordId.GetValueOrDefault())) continue;
                            Transfer item = Parse(message, record.TimeCreated.Value.ToUniversalTime(), source, flags);
                            if (item != null) rows.Add(item);
                        }
                    }
                }
            }
            rows.Reverse();
            return rows;
        }

        internal static List<Transfer> Read()
        {
            List<Transfer> rows = ReadNative();
            if (!Directory.Exists(Folder)) return rows;
            Prune(Folder, DateTime.UtcNow);
            foreach (string file in Directory.GetFiles(Folder, "*.tsv"))
            {
                try
                {
                    using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string row;
                        while ((row = reader.ReadLine()) != null)
                        {
                            string[] fields = row.Split('\t');
                            long ticks;
                            if ((fields.Length != 4 && fields.Length != 5) || !long.TryParse(fields[0], out ticks) || ticks < 0 || ticks > DateTime.MaxValue.Ticks) continue;
                            rows.Add(new Transfer { Utc = new DateTime(ticks, DateTimeKind.Utc), Device = fields[1], Action = fields[2], Name = fields[3],
                                LocalPath = fields.Length == 5 ? fields[4] : null });
                        }
                    }
                }
                catch (FileNotFoundException) { }
            }
            rows.Sort((a,b) => b.Utc.CompareTo(a.Utc));
            return rows;
        }
    }

}
