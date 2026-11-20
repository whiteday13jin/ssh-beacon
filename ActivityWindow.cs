using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SshBeacon
{
    internal sealed class ActivityCard
    {
        internal DateTime Time;
        internal string Action, Title, Detail, LocalPath;
        public override string ToString() { return Action + "　" + Title + "　" + Detail + "　" + Time.ToLocalTime() +
            (string.IsNullOrEmpty(LocalPath) ? "" : Environment.NewLine + LocalPath); }
    }

    internal sealed class ActivityWindow : Form
    {
        private readonly ListBox list = new ListBox();
        private readonly Font headline;
        private readonly Font linkFont;
        private readonly ToolTip pathTip = new ToolTip { AutoPopDelay = 20000 };
        private int hoverIndex = -1;
        internal ActivityWindow(List<ActivityCard> items)
        {
            Text = "SSH 访问日志";
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 425);
            MinimumSize = new Size(410, 260);
            BackColor = Color.FromArgb(235, 244, 239);
            Padding = new Padding(12, 10, 12, 8);
            Font = SystemFonts.MessageBoxFont;
            headline = new Font(Font, FontStyle.Bold);
            linkFont = new Font(Font, FontStyle.Underline);
            list.AccessibleName = "访问记录";
            list.DrawMode = DrawMode.OwnerDrawVariable;
            list.MeasureItem += delegate(object sender, MeasureItemEventArgs e)
            {
                ActivityCard item = (ActivityCard)list.Items[e.Index];
                e.ItemHeight = (Font.Height + 2) * (string.IsNullOrEmpty(item.LocalPath) ? 3 : 4) + 16;
            };
            list.Dock = DockStyle.Fill;
            list.BackColor = BackColor;
            list.BorderStyle = BorderStyle.None;
            list.IntegralHeight = false;
            list.DrawItem += DrawCard;
            list.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                int index = PathIndexAt(e.Location);
                list.Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                if (index == hoverIndex) return;
                hoverIndex = index;
                pathTip.SetToolTip(list, index < 0 ? null : ((ActivityCard)list.Items[index]).LocalPath);
            };
            list.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                int index = PathIndexAt(e.Location);
                if (index >= 0) RevealFile(((ActivityCard)list.Items[index]).LocalPath);
            };
            list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.C && list.SelectedItem != null)
                {
                    Clipboard.SetText(list.SelectedItem.ToString());
                    e.SuppressKeyPress = true;
                }
            };
            items.Sort((a,b) => b.Time.CompareTo(a.Time));
            list.Items.AddRange(items.ToArray());
            Controls.Add(list);
        }

        private Rectangle PathBounds(Rectangle row)
        {
            int h = Font.Height + 2;
            return new Rectangle(row.Left + 13, row.Top + 8 + 3 * h, row.Width - 26, h);
        }

        private int PathIndexAt(Point point)
        {
            int index = list.IndexFromPoint(point);
            if (index < 0 || string.IsNullOrEmpty(((ActivityCard)list.Items[index]).LocalPath)) return -1;
            return PathBounds(list.GetItemRectangle(index)).Contains(point) ? index : -1;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr item, uint mask, IntPtr attributes);
        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);

        private void RevealFile(string path)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "找不到这个文件，可能已移动、删除或暂时无法访问。", "定位文件");
                return;
            }
            IntPtr item;
            int result = SHParseDisplayName(path, IntPtr.Zero, out item, 0, IntPtr.Zero);
            try
            {
                if (result >= 0) result = SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0);
                if (result < 0) MessageBox.Show(this, "资源管理器暂时无法定位这个文件。", "定位文件");
            }
            finally { Marshal.FreeCoTaskMem(item); }
        }

        private void DrawCard(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            ActivityCard item = (ActivityCard)list.Items[e.Index];
            Graphics g = e.Graphics;
            using (Brush background = new SolidBrush(list.BackColor)) g.FillRectangle(background, e.Bounds);
            Rectangle box = Rectangle.Inflate(e.Bounds, -1, -3);
            g.FillRectangle(Brushes.White, box);
            bool selected = (e.State & DrawItemState.Selected) != 0 && list.Focused;
            using (Pen edge = new Pen(selected ? Color.FromArgb(116, 157, 137) : Color.FromArgb(211, 226, 217)))
                g.DrawRectangle(edge, box.X, box.Y, box.Width-1, box.Height-1);
            Color accent = item.Action.StartsWith("收到") ? Color.FromArgb(24,117,91) :
                item.Action.StartsWith("发出") ? Color.FromArgb(49,99,168) : Color.FromArgb(133,91,34);
            int x = box.X + 12, right = box.Right - 12, y = box.Y + 5, h = Font.Height + 2;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            string stamp = item.Time == DateTime.MinValue ? "" : item.Time.ToLocalTime().ToString("MM-dd HH:mm:ss");
            int clockWidth = TextRenderer.MeasureText(g, stamp, Font).Width;
            TextRenderer.DrawText(g, stamp, Font, new Rectangle(right-clockWidth,y,clockWidth,h), Color.DimGray, flags | TextFormatFlags.Right);
            TextRenderer.DrawText(g, item.Action, Font, new Rectangle(x,y,Math.Max(1,right-x-clockWidth-6),h), accent, flags);
            TextRenderer.DrawText(g, item.Title, headline, new Rectangle(x,y+h,right-x,h), Color.FromArgb(39,47,54), flags);
            TextRenderer.DrawText(g, item.Detail, Font, new Rectangle(x,y+2*h,right-x,h), Color.FromArgb(83,94,105), flags);
            if (!string.IsNullOrEmpty(item.LocalPath))
                TextRenderer.DrawText(g, "本机路径　" + item.LocalPath, linkFont, PathBounds(e.Bounds),
                    Color.FromArgb(47,104,82), (flags & ~TextFormatFlags.EndEllipsis) | TextFormatFlags.PathEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { pathTip.Dispose(); linkFont.Dispose(); headline.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
