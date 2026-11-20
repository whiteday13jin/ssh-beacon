using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace SshBeacon
{
    internal static class VisualTests
    {
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
        static long Alpha(Bitmap b)
        {
            long sum = 0;
            for (int y = 0; y < b.Height; y++) for (int x = 0; x < b.Width; x++) sum += b.GetPixel(x,y).A;
            return sum;
        }
        static void Set(object obj, string field, object value)
        { obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value); }
        [STAThread]
        static void Main(string[] args)
        {
            DateTime when = DateTime.UtcNow;
            Transfer upload = Transfer.Parse(@"close ""/Desktop/\346\265\213\350\257\225.txt"" bytes read 0 written 24", when, "127.0.0.1");
            Check(upload.Name == "测试.txt" && upload.Action == "收到文件", "Chinese filename and incoming direction");
            Check(Transfer.Parse(@"close ""/D:/资料/测试.txt"" bytes read 20 written 0", when, "::1").LocalPath == @"D:\资料\测试.txt", "SFTP drive path displays as a Windows path");
            Check(Transfer.Parse(@"close ""a.txt"" bytes read 24 written 0", when, "::1").Action == "发出文件", "Outgoing direction");
            Check(Transfer.Parse(@"close ""a.txt"" bytes read 0 written 0", when, "::1").Action == "访问文件（0 字节）", "No direction without open flags");
            Check(Transfer.Parse(@"close ""a.txt"" bytes read 0 written 0", when, "::1", "READ").Action == "对方请求下载（0 字节）", "Empty read shows the remote download request");
            string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())).FullName;
            try
            {
                DateTime date = when.ToLocalTime().Date;
                string old = Path.Combine(folder, date.AddDays(-7).ToString("yyyyMMdd") + "_test.tsv");
                string keep = Path.Combine(folder, date.AddDays(-6).ToString("yyyyMMdd") + "_test.tsv");
                File.WriteAllText(old, "old");
                File.WriteAllText(keep, "keep");
                Transfer.Prune(folder, when);
                Check(!File.Exists(old) && File.Exists(keep), "Seven local calendar days of file history");
            }
            finally { Directory.Delete(folder, true); }
            string payload = "Accepted publickey for demo from 192.0.2.1 port 49152 ssh2: ED25519";
            VisitHistory history = new VisitHistory();
            Check(Login.Parse("Disconnected from 192.0.2.1 port 49152", when) == null, "Disconnect is not a login");
            Check(history.Add(Login.Parse(payload, when)), "Short authenticated session starts a visit");
            Check(!history.Add(Login.Parse(payload, when.AddSeconds(60))), "Consecutive logins share a notification");
            Check(history.Add(Login.Parse(payload.Replace("demo", "guest"), when.AddSeconds(61))), "Separate account starts another visit");
            Check(history.Add(Login.Parse(payload, when.AddSeconds(121))) && history.Recent[2].Count == 2, "New visit after quiet period preserves earlier count");
            using (Icon plain = App.BuildTrayIcon(false))
            using (Icon unread = App.BuildTrayIcon(true))
            using (Bitmap p = plain.ToBitmap())
            using (Bitmap u = unread.ToBitmap())
                Check(u.GetPixel(25, 5).R > 220 && p.GetPixel(25, 5).A == 0, "Unread badge occupies upper right corner");
            using (Bitmap b = new Bitmap(72,72,PixelFormat.Format32bppPArgb))
            {
                BeaconVisuals.Draw(b, BeaconState.Idle, 0, 0, 6);
                Check(Alpha(b) == 0, "Idle is fully transparent");
                BeaconVisuals.Draw(b, BeaconState.Connected, 0, 0, 6);
                Check(Alpha(b) == 0, "Arrival starts transparent");
                BeaconVisuals.Draw(b, BeaconState.Connected, 7, 0, 6);
                long connected = Alpha(b);
                Check(connected > 0, "Connected is visible");
                Color core = b.GetPixel(36,36), shoulder = b.GetPixel(40,36);
                Check(core.A >= 250 && core.G >= 240 && core.B >= 214, "Core is bright warm white");
                Check(core.A > shoulder.A + 70, "Core remains clearly resolved within soft halo");
                Check(b.GetPixel(43,35).A != b.GetPixel(28,35).A, "Halo is gently asymmetric");
                BeaconVisuals.Draw(b, BeaconState.Linger, 7, 0, 6);
                Check(Alpha(b) == connected, "Disconnect starts at identical luminance");
                long previous = connected;
                for (int i=1;i<=150;i++)
                {
                    double t = i * 0.04;
                    BeaconVisuals.Draw(b, BeaconState.Linger, 7+t, t, 6);
                    long current = Alpha(b);
                    if (current > previous) throw new Exception("Fade grew brighter at " + t);
                    previous = current;
                }
                Check(previous == 0, "Six-second fade is monotonic and ends invisible");
                BeaconVisuals.Draw(b, BeaconState.Connected, 2.4, 0, 6);
                Check(b.GetPixel(46, 36).A > b.GetPixel(25, 35).A, "Halo reaches farther right at the first breath peak");
                BeaconVisuals.Draw(b, BeaconState.Connected, 2.4 + BeaconVisuals.HaloTurnSeconds / 2, 0, 6);
                Check(b.GetPixel(25, 35).A > b.GetPixel(46, 36).A, "Halo reaches farther left half a turn later");
                BeaconVisuals.Draw(b, BeaconState.Error, 1, 0, 6); long error = Alpha(b);
                BeaconVisuals.Draw(b, BeaconState.Error, 99, 0, 6);
                Check(error > 0 && Alpha(b) == error && b.GetPixel(36,36).R > b.GetPixel(36,36).G, "Error stays steady red");
                for (int i=0;i<1000;i++)
                {
                    BeaconVisuals.Draw(b, BeaconState.Connected, i/25.0, 0, 6);
                    for(int x=0;x<72;x++) if(b.GetPixel(x,0).A!=0 || b.GetPixel(x,71).A!=0 || b.GetPixel(0,x).A!=0 || b.GetPixel(71,x).A!=0)
                        throw new Exception("Clipped halo");
                }
                Console.WriteLine("PASS 1000 animation frames render inside bounds");
                BeaconVisuals.Draw(b, BeaconState.Idle, 25, 8, 6);
                Check(Alpha(b)==0,"Idle clears previous rendered contents");
            }
            // Test the real state selector without constructing App or touching network/settings.
            object app = FormatterServices.GetUninitializedObject(typeof(App));
            DateTime now = DateTime.UtcNow;
            MethodInfo state = typeof(App).GetMethod("ComputeState", BindingFlags.Instance|BindingFlags.NonPublic);
            Set(app,"_lastConnUtc",DateTime.MinValue);
            Check((BeaconState)state.Invoke(app,new object[]{now})==BeaconState.Idle,"No connection stays idle");
            Set(app,"_connections",1);
            Check((BeaconState)state.Invoke(app,new object[]{now})==BeaconState.Connected,"Active connection wins");
            Set(app,"_error",true);
            Check((BeaconState)state.Invoke(app,new object[]{now})==BeaconState.Error,"Monitor error takes priority");
            Set(app,"_error",false); Set(app,"_connections",0); Set(app,"_lastConnUtc",now.AddSeconds(-5.99));
            Check((BeaconState)state.Invoke(app,new object[]{now})==BeaconState.Linger,"Disconnect grace remains six seconds");
            Set(app,"_lastConnUtc",now.AddSeconds(-6));
            Check((BeaconState)state.Invoke(app,new object[]{now})==BeaconState.Idle,"Grace ends at six seconds");
            Set(app,"_connections",1);
            Check((BeaconState)state.Invoke(app,new object[]{now})==BeaconState.Connected,"Reconnect interrupts fade");

            Set(app,"_connections",0); Set(app,"_lastConnUtc",DateTime.MinValue);
            using (DotWindow dot = new DotWindow())
            using (PillWindow pill = new PillWindow())
            using (System.Windows.Forms.Timer render = new System.Windows.Forms.Timer())
            using (System.Windows.Forms.Timer hover = new System.Windows.Forms.Timer())
            {
                Set(app,"_dot",dot); Set(app,"_pill",pill);
                Set(app,"_renderTimer",render); Set(app,"_hoverTimer",hover);
                render.Start(); hover.Start();
                typeof(App).GetMethod("RenderTick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(app,null);
                Check(!dot.Visible && !render.Enabled && !hover.Enabled,"Real idle tick hides dot and stops visual timers");
            }

            using (Bitmap sheet = new Bitmap(840,300))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font font = new Font("Segoe UI",10))
            using (Bitmap frame = new Bitmap(72,72,PixelFormat.Format32bppPArgb))
            {
                double[] ages={0.2,1.8,3.6,7.0,10.0,12.6,15.0};
                string[] labels={"Arrival","Breath peak","Breath low","Disconnect","Fade 3s","Fade 5.6s","Idle"};
                for(int row=0;row<2;row++)
                {
                    g.FillRectangle(row==0?Brushes.Black:Brushes.WhiteSmoke,0,row*150,840,150);
                    for(int i=0;i<7;i++)
                    {
                        BeaconState st=i<3?BeaconState.Connected:i<6?BeaconState.Linger:BeaconState.Idle;
                        BeaconVisuals.Draw(frame,st,ages[i],Math.Max(0,ages[i]-7),6);
                        g.DrawImageUnscaled(frame,i*120+24,row*150+20);
                        g.DrawString(labels[i],font,row==0?Brushes.White:Brushes.Black,i*120+12,row*150+106);
                    }
                }
                sheet.Save("preview-contact-sheet.png",ImageFormat.Png);
            }
            Console.WriteLine("PASS preview-contact-sheet.png generated");
        }
    }
}
