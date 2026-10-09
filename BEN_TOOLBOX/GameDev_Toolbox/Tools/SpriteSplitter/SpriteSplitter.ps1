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