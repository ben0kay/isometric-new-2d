# Windows PowerShell 5.1. Launch through Ben Toolbox or powershell.exe -STA -File.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if (-not ('BenSprites.SplitterWindow' -as [type])) {
Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BenSprites {
public class SheetView : Panel {
    public SheetView() { DoubleBuffered = true; ResizeRedraw = true; TabStop = true; SetStyle(ControlStyles.Selectable, true); }
}
public class SplitterWindow : Form {
    Bitmap sheet;
    byte[] pixels;
    string sourcePath;
    readonly List<int> cuts = new List<int>();
    readonly List<Bitmap> outputs = new List<Bitmap>();
    readonly SheetView view = new SheetView();
    readonly FlowLayoutPanel previews = new FlowLayoutPanel();
    readonly NumericUpDown sensitivity = new NumericUpDown();
    readonly NumericUpDown gap = new NumericUpDown();
    readonly NumericUpDown padding = new NumericUpDown();
    readonly ComboBox canvas = new ComboBox();
    readonly TextBox destination = new TextBox();
    readonly TextBox prefix = new TextBox();
    readonly Label status = new Label();
    RectangleF displayed;
    int selected = -1;
    bool dragging;

    public SplitterWindow(string root) {
        Text = "Sprite Splitter"; Width = 1220; Height = 900;
        MinimumSize = new Size(900, 700); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); KeyPreview = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 205));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        Controls.Add(layout);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddButton(toolbar, "Open PNG", OpenSheet);
        AddButton(toolbar, "Suggest cuts", Detect);
        AddButton(toolbar, "Remove selected", RemoveSelected);
        AddButton(toolbar, "Clear cuts", delegate { cuts.Clear(); selected = -1; RefreshPreviews(); });
        toolbar.Controls.Add(new Label { Text = "Click: add/select | Drag: move | Delete/right-click: remove", AutoSize = true, Margin = new Padding(12, 12, 0, 0) });
        layout.Controls.Add(toolbar, 0, 0);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill };
        AddNumber(options, "Detection alpha", sensitivity, 0, 254, 12);
        AddNumber(options, "Min gap px", gap, 1, 1000, 8);
        AddNumber(options, "Padding px", padding, 0, 512, 8);
        options.Controls.Add(new Label { Text = "Canvas", AutoSize = true, Margin = new Padding(10, 12, 0, 0) });
        canvas.DropDownStyle = ComboBoxStyle.DropDownList; canvas.Width = 245;
        canvas.Items.AddRange(new object[] { "Tight crop", "Shared rectangle, bottom aligned", "Shared square, bottom aligned" });
        canvas.SelectedIndex = 1; options.Controls.Add(canvas);
        layout.Controls.Add(options, 0, 1);
        view.Dock = DockStyle.Fill; view.BackColor = Color.FromArgb(40,40,40);
        view.Paint += PaintSheet; view.MouseDown += MouseDownSheet;
        view.MouseMove += MouseMoveSheet;
        view.MouseUp += delegate { if (dragging) { dragging = false; view.Capture = false; cuts.Sort(); RefreshPreviews(); } };
        layout.Controls.Add(view, 0, 2);
        previews.Dock = DockStyle.Fill; previews.AutoScroll = true; previews.WrapContents = false;
        layout.Controls.Add(previews, 0, 3);
        var export = new FlowLayoutPanel { Dock = DockStyle.Fill };
        export.Controls.Add(new Label { Text = "Output folder", AutoSize = true, Margin = new Padding(5,12,0,0) });
        destination.Text = root; destination.Width = 360; export.Controls.Add(destination);
        AddButton(export, "Browse", delegate {
            using (var d = new FolderBrowserDialog()) {
                if (Directory.Exists(destination.Text)) d.SelectedPath = destination.Text;
                if (d.ShowDialog(this) == DialogResult.OK) destination.Text = d.SelectedPath;
            }
        });
        export.Controls.Add(new Label { Text = "Name", AutoSize = true, Margin = new Padding(5,12,0,0) });
        prefix.Text = "Sprite"; prefix.Width = 170; export.Controls.Add(prefix);
        AddButton(export, "Export PNGs", Export);
        layout.Controls.Add(export, 0, 4);
        status.Dock = DockStyle.Fill; status.Text = "Open a transparent PNG. Vertical lines divide the sheet into sprites.";
        layout.Controls.Add(status, 0, 5);
        padding.ValueChanged += delegate { RefreshPreviews(); };
        canvas.SelectedIndexChanged += delegate { RefreshPreviews(); };
        sensitivity.ValueChanged += delegate { status.Text = "Detection alpha changed. Click Suggest cuts to apply; export preserves faint pixels."; };
        KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.KeyCode == Keys.Delete && view.Focused) { RemoveSelected(); e.Handled = true; }
        };
        FormClosed += delegate { ClearOutputs(); if (sheet != null) sheet.Dispose(); };
    }
    void AddButton(Control parent, string text, Action action) {
        var b = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(5) };
        b.Click += delegate { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Sprite Splitter"); } };
        parent.Controls.Add(b);
    }
    void AddNumber(Control parent, string text, NumericUpDown number, int min, int max, int value) {
        parent.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(8,12,0,0) });
        number.Minimum = min; number.Maximum = max; number.Value = value; number.Width = 65;
        parent.Controls.Add(number);
    }
    void OpenSheet() {
        using (var d = new OpenFileDialog { Filter = "PNG images|*.png" }) {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Bitmap loaded;
            using (var original = Image.FromFile(d.FileName)) {
                if ((long)original.Width * original.Height > 40000000) throw new Exception("Please use an image under 40 million pixels.");
                loaded = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(loaded)) {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(original, 0, 0);
                }
            }
            if (sheet != null) sheet.Dispose(); sheet = loaded; sourcePath = d.FileName;
            prefix.Text = Path.GetFileNameWithoutExtension(sourcePath); cuts.Clear(); selected = -1;
            pixels = new byte[sheet.Width * sheet.Height * 4];
            var data = sheet.LockBits(new Rectangle(0,0,sheet.Width,sheet.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { for (int y=0;y<sheet.Height;y++) Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),pixels,y*sheet.Width*4,sheet.Width*4); }
            finally { sheet.UnlockBits(data); }
            Detect();
        }
    }
    int Alpha(int x, int y) { return pixels[(y * sheet.Width + x)*4+3]; }
    void Detect() {
        if (sheet == null) return;
        cuts.Clear(); selected = -1; int start = -1; bool seenContent = false;
        for (int x=0; x<sheet.Width; x++) {
            bool occupied = false;
            for (int y=0;y<sheet.Height;y++) if (Alpha(x,y) > (int)sensitivity.Value) { occupied=true; break; }
            if (!occupied) { if (start<0) start=x; }
            else {
                if (seenContent && start>=0 && x-start >= (int)gap.Value) cuts.Add(start+(x-start)/2);
                start=-1; seenContent=true;
            }
        }
        RefreshPreviews();
    }
    Rectangle Bounds(int left, int right) {
        int minX=right, minY=sheet.Height, maxX=-1, maxY=-1;
        // Crop on all nonzero alpha; sensitivity only affects cut suggestions.
        for (int y=0;y<sheet.Height;y++) for (int x=left;x<right;x++) if (Alpha(x,y)>0) {
            minX=Math.Min(minX,x); maxX=Math.Max(maxX,x); minY=Math.Min(minY,y); maxY=Math.Max(maxY,y);
        }
        return maxX<0 ? Rectangle.Empty : new Rectangle(minX,minY,maxX-minX+1,maxY-minY+1);
    }
    void ClearOutputs() {
        while (previews.Controls.Count>0) { var c=previews.Controls[0]; previews.Controls.Remove(c); c.Dispose(); }
        foreach (var b in outputs) b.Dispose(); outputs.Clear();
    }
    void RefreshPreviews() {
        ClearOutputs(); view.Invalidate(); if (sheet == null) return;
        var edges = new List<int> { 0 }; edges.AddRange(cuts); edges.Add(sheet.Width); edges.Sort();
        var regions=new List<Rectangle>(); int width=0,height=0;
        for (int i=0;i<edges.Count-1;i++) {
            Rectangle r=Bounds(edges[i],edges[i+1]); if (r.IsEmpty) continue;
            regions.Add(r); width=Math.Max(width,r.Width); height=Math.Max(height,r.Height);
        }
        int p=(int)padding.Value; width+=2*p; height+=2*p;
        if (canvas.SelectedIndex==2) width=height=Math.Max(width,height);
        foreach (Rectangle r in regions) {
            int w=canvas.SelectedIndex==0?r.Width+2*p:width;
            int h=canvas.SelectedIndex==0?r.Height+2*p:height;
            var result=new Bitmap(w,h,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(result)) {
                g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(sheet,new Rectangle((w-r.Width)/2,h-p-r.Height,r.Width,r.Height),r,GraphicsUnit.Pixel);
            }
            outputs.Add(result);
            var card=new Panel { Width=180, Height=175, Margin=new Padding(8) };
            var picture=new PictureBox { Width=180,Height=145,SizeMode=PictureBoxSizeMode.Zoom,Image=result,BackColor=Color.FromArgb(60,60,60) };
            card.Controls.Add(picture);
            card.Controls.Add(new Label { Top=147,Width=180,Text=String.Format("{0:00}  |  {1} x {2}",outputs.Count,w,h),TextAlign=ContentAlignment.MiddleCenter });
            previews.Controls.Add(card);
        }
        status.Text=String.Format("{0} x {1} sheet | {2} sprites | Original pixel scale preserved. Shared canvases align visible bottoms.",sheet.Width,sheet.Height,outputs.Count);
    }
    void PaintSheet(object sender, PaintEventArgs e) {
        if (sheet==null) return;
        float scale=Math.Min((view.ClientSize.Width-20f)/sheet.Width,(view.ClientSize.Height-20f)/sheet.Height);
        if (scale<=0) return;
        displayed=new RectangleF((view.Width-sheet.Width*scale)/2,(view.Height-sheet.Height*scale)/2,sheet.Width*scale,sheet.Height*scale);
        var old=e.Graphics.Save(); e.Graphics.SetClip(displayed);
        using(var light=new SolidBrush(Color.FromArgb(95,95,95))) using(var dark=new SolidBrush(Color.FromArgb(65,65,65))) {
            for(int y=(int)displayed.Top;y<displayed.Bottom;y+=16) for(int x=(int)displayed.Left;x<displayed.Right;x+=16)
                e.Graphics.FillRectangle((((x-(int)displayed.Left)/16+(y-(int)displayed.Top)/16)%2)==0?light:dark,x,y,16,16);
        }
        e.Graphics.DrawImage(sheet,displayed);
        for(int i=0;i<cuts.Count;i++) using(var pen=new Pen(i==selected?Color.Yellow:Color.Lime,2)) {
            float x=displayed.Left+cuts[i]*scale;
            e.Graphics.DrawLine(pen,x,displayed.Top,x,displayed.Bottom);
            e.Graphics.DrawString((i+1).ToString(),Font,Brushes.Black,x+4,displayed.Top+4);
            e.Graphics.DrawString((i+1).ToString(),Font,Brushes.Yellow,x+3,displayed.Top+3);
        }
        e.Graphics.Restore(old);
    }
    int HitCut(int mouseX) {
        int nearest=-1; float best=9;
        for(int i=0;i<cuts.Count;i++) {
            float d=Math.Abs(displayed.Left+cuts[i]*displayed.Width/sheet.Width-mouseX);
            if(d<best) { best=d;nearest=i; }
        }
        return nearest;
    }
    int ImageX(int mouseX) { return Math.Max(1,Math.Min(sheet.Width-1,(int)Math.Round((mouseX-displayed.Left)*sheet.Width/displayed.Width))); }
    void MouseDownSheet(object sender, MouseEventArgs e) {
        if(sheet==null || !displayed.Contains(e.Location)) return;
        view.Focus(); selected=HitCut(e.X);
        if(e.Button==MouseButtons.Right) { RemoveSelected();return; }
        if(e.Button!=MouseButtons.Left) return;
        if(selected<0) {
            int x=ImageX(e.X); if(cuts.Contains(x)) selected=cuts.IndexOf(x);
            else { cuts.Add(x);cuts.Sort();selected=cuts.IndexOf(x); }
            RefreshPreviews();
        }
        dragging=true;view.Capture=true;view.Invalidate();
    }
    void MouseMoveSheet(object sender, MouseEventArgs e) {
        if(!dragging || selected<0) return;
        int x=ImageX(e.X);
        int lo=selected==0?1:cuts[selected-1]+1;
        int hi=selected==cuts.Count-1?sheet.Width-1:cuts[selected+1]-1;
        cuts[selected]=Math.Max(lo,Math.Min(hi,x));view.Invalidate();
    }
    void RemoveSelected() {
        if(selected>=0 && selected<cuts.Count) { cuts.RemoveAt(selected);selected=-1;RefreshPreviews(); }
    }
    void Export() {
        if(outputs.Count==0) throw new Exception("Open a PNG and preview its sprites first.");
        string name=prefix.Text.Trim();
        if(String.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.EndsWith(".") || name.EndsWith(" "))
            throw new Exception("Enter a valid filename prefix.");
        string folder=Path.GetFullPath(destination.Text.Trim());
        var targets=new List<string>();
        for(int i=0;i<outputs.Count;i++) {
            string target=Path.Combine(folder,name+"_"+(i+1).ToString("00")+".png");
            if(File.Exists(target)) throw new Exception("File already exists: "+target+"\nChoose another name or folder. Nothing was exported.");
            targets.Add(target);
        }
        Directory.CreateDirectory(folder);
        var written=new List<string>();
        try {
            for(int i=0;i<outputs.Count;i++) using(var stream=new FileStream(targets[i],FileMode.CreateNew,FileAccess.Write)) {
                written.Add(targets[i]); outputs[i].Save(stream,ImageFormat.Png);
            }
        } catch { foreach(string path in written) { try { File.Delete(path); } catch {} } throw; }
        status.Text=String.Format("Exported {0} PNGs to {1}",outputs.Count,folder);
        MessageBox.Show(this,status.Text,"Export complete");
    }
}
}
'@
}
[System.Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object BenSprites.SplitterWindow($PSScriptRoot)
try { [void]$form.ShowDialog() } finally { $form.Dispose() }
