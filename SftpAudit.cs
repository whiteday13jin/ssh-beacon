using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace SshBeacon
{
    internal sealed class Transfer
    {
        internal DateTime Utc;
        internal string Device, Name, Action, LocalPath;

        private static readonly Regex Close = new Regex(@"^(forced )?close ""(.+)"" bytes read ([0-9]+) written ([0-9]+)$");

        internal static Transfer Parse(string line, DateTime utc, string device)
        {
            Match m = Close.Match(line);
            long sent, received;
            if (!m.Success || !long.TryParse(m.Groups[3].Value, out sent) ||
                !long.TryParse(m.Groups[4].Value, out received) || (sent == 0 && received == 0)) return null;
            string path = Regex.Replace(m.Groups[2].Value, @"(?:\\[0-3][0-7]{2})+", delegate(Match escaped)
            {
                byte[] bytes = new byte[escaped.Length / 4];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(escaped.Value.Substring(i * 4 + 1, 3), 8);
                return Encoding.UTF8.GetString(bytes);
            }).Replace('\\', '/');
            string action = sent > 0 && received > 0 ? "双向读写" : sent > 0 ? "发出文件" : "收到文件";
            if (m.Groups[1].Success) action += "（连接中断）";
            string local = path.Replace('/', '\\');
            if (local.Length > 3 && local[0] == '\\' && char.IsLetter(local[1]) && local[2] == ':' && local[3] == '\\')
                local = local.Substring(1);
            try { local = Path.GetFullPath(local); }
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
        internal void Save(string session)
        {
            Directory.CreateDirectory(Folder);
            string file = Path.Combine(Folder, Utc.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + session + ".tsv");
            string row = Utc.Ticks + "\t" + Device + "\t" + Action + "\t" + CleanField(Name) + "\t" + CleanField(LocalPath) + "\n";
            using (FileStream stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] data = Encoding.UTF8.GetBytes(row);
                stream.Write(data, 0, data.Length);
            }
        }
        private static string CleanField(string value)
        {
            return value.Replace('\t',' ').Replace('\r',' ').Replace('\n',' ');
        }
        internal static List<Transfer> Read()
        {
            List<Transfer> rows = new List<Transfer>();
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

    // 二进制协议直接使用 sshd 提供的标准输入和输出；只读取原生服务的诊断流。
    internal static class SftpAudit
    {
        internal static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--prune") { Transfer.Prune(Transfer.Folder, DateTime.UtcNow); return 0; }
            string session = Guid.NewGuid().ToString("N");
            string connection = Environment.GetEnvironmentVariable("SSH_CONNECTION") ?? "";
            IPAddress address;
            string device = IPAddress.TryParse(connection.Split(' ')[0], out address) ? address.ToString() : "未知设备";
            bool audit = args.Length == 0 || (args.Length == 2 && args[0] == "--account" &&
                WindowsIdentity.GetCurrent().User.Value == args[1]);
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "OpenSSH", "sftp-server.exe"), audit ? "-e -l INFO" : "");
            psi.UseShellExecute = false;
            if (!audit)
            {
                using (Process native = Process.Start(psi)) { native.WaitForExit(); return native.ExitCode; }
            }
            psi.RedirectStandardError = true;
            psi.StandardErrorEncoding = Encoding.UTF8;
            using (Process server = Process.Start(psi))
            {
                string line;
                while ((line = server.StandardError.ReadLine()) != null)
                {
                    Transfer item = Transfer.Parse(line, DateTime.UtcNow, device);
                    if (item == null) continue;
                    try { item.Save(session); }
                    catch (IOException) { Console.Error.WriteLine("无法保存文件操作记录。"); }
                    catch (UnauthorizedAccessException) { Console.Error.WriteLine("没有保存文件操作记录的权限。"); }
                }
                server.WaitForExit();
                return server.ExitCode;
            }
        }
    }
}
