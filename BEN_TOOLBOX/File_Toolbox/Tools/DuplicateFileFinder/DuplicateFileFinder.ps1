# Duplicate File Finder - Windows PowerShell 5.1 GUI
# Compare source A to destination B by filename, optionally verifying SHA-256.
# Only SHA-256 verified identical Source A files can be moved to the Recycle Bin. Destination B is never modified.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName Microsoft.VisualBasic
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Duplicate File Finder | File Toolbox'
$form.ClientSize = New-Object System.Drawing.Size(1100, 790)
$form.MinimumSize = New-Object System.Drawing.Size(900, 720)
$form.StartPosition = 'CenterScreen'
$form.BackColor = [Drawing.Color]::FromArgb(24,28,35)
$form.ForeColor = [Drawing.Color]::WhiteSmoke
$form.Font = New-Object Drawing.Font('Segoe UI', 10)

function New-Label([string]$text,[int]$x,[int]$y,[int]$w,[int]$h) {
    $l=New-Object Windows.Forms.Label
    $l.Text=$text; $l.SetBounds($x,$y,$w,$h); $l.Anchor='Top,Left'
    $form.Controls.Add($l); return $l
}
function New-Button([string]$text,[int]$x,[int]$y,[int]$w,[int]$h) {
    $b=New-Object Windows.Forms.Button
    $b.Text=$text; $b.SetBounds($x,$y,$w,$h)
    $b.FlatStyle='Flat'; $b.BackColor=[Drawing.Color]::FromArgb(58,94,155)
    $b.ForeColor=[Drawing.Color]::White; $b.UseVisualStyleBackColor=$false
    $form.Controls.Add($b); return $b
}
function New-PathInput([int]$y) {
    $t=New-Object Windows.Forms.TextBox
    $t.SetBounds(25,$y,865,29); $t.Anchor='Top,Left,Right'
    $t.BackColor=[Drawing.Color]::FromArgb(40,47,58);$t.ForeColor=[Drawing.Color]::WhiteSmoke
    $form.Controls.Add($t); return $t
}
function Get-Files([string]$root,[bool]$recursive) {
    # Skip directory junctions/symlinks, which can escape the selected folder.
    $stack=New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push($root)
    while($stack.Count -gt 0) {
        $path=$stack.Pop()
        try { $entries=[IO.Directory]::EnumerateFileSystemEntries($path) }
        catch { throw "Cannot read folder '$path': $($_.Exception.Message)" }
        foreach($entry in $entries) {
            try { $attr=[IO.File]::GetAttributes($entry) }
            catch { throw "Cannot inspect '$entry': $($_.Exception.Message)" }
            if(($attr -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            if(($attr -band [IO.FileAttributes]::Directory) -ne 0) {
                if($recursive) { $stack.Push($entry) }
            } else {
                try { $item=New-Object IO.FileInfo($entry); [pscustomobject]@{Name=$item.Name;Path=$item.FullName;Size=$item.Length} }
                catch { throw "Cannot inspect file '$entry': $($_.Exception.Message)" }
            }
        }
    }
}

$heading=New-Label 'DUPLICATE FILE FINDER' 25 15 900 43
$heading.Font=New-Object Drawing.Font('Segoe UI',21,[Drawing.FontStyle]::Bold)
[void](New-Label 'Source folder (A) — these are the files reported as possible duplicates' 25 65 850 24)
$source=New-PathInput 91
$sourceBrowse=New-Button 'Browse' 907 90 165 31
$sourceBrowse.Anchor='Top,Right'
[void](New-Label 'Destination folder (B) — search here for existing matches (never deleted)' 25 178 850 24)
$dest=New-PathInput 205
$destBrowse=New-Button 'Browse' 907 204 165 31
$destBrowse.Anchor='Top,Right'

$sourceSub=New-Object Windows.Forms.CheckBox
$sourceSub.Text='Include subfolders in source A';$sourceSub.Checked=$false;$sourceSub.SetBounds(25,130,330,30)
$form.Controls.Add($sourceSub)
$destSub=New-Object Windows.Forms.CheckBox
$destSub.Text='Include subfolders in destination B';$destSub.Checked=$true;$destSub.SetBounds(25,244,350,30)
$form.Controls.Add($destSub)
$verify=New-Object Windows.Forms.CheckBox
$verify.Text='Verify content (SHA-256) - required for deletion';$verify.Checked=$true;$verify.SetBounds(25,282,480,30)
$form.Controls.Add($verify)

$scan=New-Button 'Scan for duplicates' 25 325 200 38
$export=New-Button 'Export CSV' 235 325 145 38
$export.Enabled=$false
$deleteAll=New-Button 'DELETE ALL identical (A)' 391 325 235 38
$deleteAll.BackColor=[Drawing.Color]::FromArgb(136,48,53)
$deleteAll.Enabled=$false
$status=New-Label 'Only verified identical files from A can be deleted.' 635 329 440 33
$status.Anchor='Top,Left,Right'

$results=New-Object Windows.Forms.ListView
$results.SetBounds(25,381,1047,350)
$results.Anchor='Top,Bottom,Left,Right'
$results.View='Details';$results.FullRowSelect=$true;$results.GridLines=$true
$results.HideSelection=$false;$results.MultiSelect=$false
$results.BackColor=[Drawing.Color]::FromArgb(32,38,47)
$results.ForeColor=[Drawing.Color]::WhiteSmoke
[void]$results.Columns.Add('Status',135)
[void]$results.Columns.Add('Source file (A)',355)
[void]$results.Columns.Add('Matching file (B)',355)
[void]$results.Columns.Add('Bytes (A)',100)
[void]$results.Columns.Add('Bytes (B)',100)
[void]$results.Columns.Add('Action',105)
$form.Controls.Add($results)
$footer=New-Label 'Delete A moves a verified source file to Recycle Bin. Hover a row for preview. Double-click to locate source.' 25 746 1040 26
$footer.Anchor='Bottom,Left,Right'

$script:report=@()
$script:rootA='';$script:rootB='';$script:busy=$false
$script:previewPath='';$script:previewImage=$null
function Select-Folder($target) {
    $dialog=New-Object Windows.Forms.FolderBrowserDialog
    $dialog.Description='Select a folder'
    if(Test-Path -LiteralPath $target.Text -PathType Container){$dialog.SelectedPath=$target.Text}
    if($dialog.ShowDialog($form) -eq 'OK'){$target.Text=$dialog.SelectedPath}
    $dialog.Dispose()
}
$sourceBrowse.Add_Click({Select-Folder $source})
$destBrowse.Add_Click({Select-Folder $dest})
$results.Add_DoubleClick({
    if($results.SelectedItems.Count -gt 0) {
        $p=$results.SelectedItems[0].Tag.SourcePath
        if(Test-Path -LiteralPath $p) {Start-Process explorer.exe -ArgumentList ('/select,"'+$p+'"')}
    }
})
$export.Add_Click({
    if(@($script:report).Count -eq 0){return}
    $save=New-Object Windows.Forms.SaveFileDialog
    $save.Filter='CSV files (*.csv)|*.csv';$save.FileName='DuplicateFileFinder_Results.csv'
    if($save.ShowDialog($form) -eq 'OK') {
        try { $script:report | Export-Csv -LiteralPath $save.FileName -NoTypeInformation -Encoding UTF8
            [Windows.Forms.MessageBox]::Show('Report saved.','Export complete') | Out-Null
        } catch { [Windows.Forms.MessageBox]::Show($_.Exception.Message,'Export failed') | Out-Null }
    }
    $save.Dispose()
})
$scan.Add_Click({
    $a=$source.Text.Trim().Trim('"');$b=$dest.Text.Trim().Trim('"')
    if(!(Test-Path -LiteralPath $a -PathType Container) -or !(Test-Path -LiteralPath $b -PathType Container)) {
        [Windows.Forms.MessageBox]::Show('Select two existing folders.','Invalid folder') | Out-Null;return
    }
    $a=[IO.Path]::GetFullPath($a).TrimEnd('\')
    $b=[IO.Path]::GetFullPath($b).TrimEnd('\')
    if([string]::Equals($a,$b,[StringComparison]::OrdinalIgnoreCase) -or
       $a.StartsWith($b+'\',[StringComparison]::OrdinalIgnoreCase) -or
       $b.StartsWith($a+'\',[StringComparison]::OrdinalIgnoreCase)) {
        [Windows.Forms.MessageBox]::Show('Choose separate, non-overlapping folders.','Overlapping folders') | Out-Null;return
    }
    $script:busy=$true;$script:rootA=$a;$script:rootB=$b
    $scan.Enabled=$false;$export.Enabled=$false;$deleteAll.Enabled=$false;$results.Items.Clear();$script:report=@()
    $form.Cursor=[Windows.Forms.Cursors]::WaitCursor
    try {
        $status.Text='Reading destination files...';$status.Refresh()
        $index=New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[object]]' ([StringComparer]::OrdinalIgnoreCase)
        $destCount=0
        foreach($file in (Get-Files $b $destSub.Checked)) {
            $destCount++
            if(!$index.ContainsKey($file.Name)){$index[$file.Name]=New-Object 'System.Collections.Generic.List[object]'}
            $index[$file.Name].Add($file)
        }
        $sourceCount=0;$matches=0;$identical=0;$different=0
        $hashCache=@{}
        foreach($file in (Get-Files $a $sourceSub.Checked)) {
            $sourceCount++
            if(!$index.ContainsKey($file.Name)){continue}
            foreach($other in $index[$file.Name]) {
                $matches++
                $state='NAME MATCH'
                if($verify.Checked) {
                    if($file.Size -ne $other.Size) { $state='DIFFERENT CONTENT';$different++ }
                    else {
                        if(!$hashCache.ContainsKey($file.Path)){$hashCache[$file.Path]=(Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash}
                        if(!$hashCache.ContainsKey($other.Path)){$hashCache[$other.Path]=(Get-FileHash -LiteralPath $other.Path -Algorithm SHA256).Hash}
                        if($hashCache[$file.Path] -eq $hashCache[$other.Path]){$state='IDENTICAL';$identical++}
                        else{$state='DIFFERENT CONTENT';$different++}
                    }
                }
                $row=[pscustomobject]@{Status=$state;SourcePath=$file.Path;DestinationPath=$other.Path;SourceBytes=$file.Size;DestinationBytes=$other.Size}
                $script:report+= $row
                $item=New-Object Windows.Forms.ListViewItem($state)
                [void]$item.SubItems.Add($file.Path);[void]$item.SubItems.Add($other.Path)
                [void]$item.SubItems.Add([string]$file.Size);[void]$item.SubItems.Add([string]$other.Size)
                [void]$item.SubItems.Add($(if($state -eq 'IDENTICAL'){'Delete A'}else{'-'}))
                $item.Tag=$row
                if($state -eq 'IDENTICAL'){$item.ForeColor=[Drawing.Color]::LightGreen}
                elseif($state -eq 'DIFFERENT CONTENT'){$item.ForeColor=[Drawing.Color]::Khaki}
                [void]$results.Items.Add($item)
            }
            if(($sourceCount % 100) -eq 0){$status.Text="Checking source files: $sourceCount ...";$status.Refresh();[Windows.Forms.Application]::DoEvents()}
        }
        $status.Text="Scanned A: $sourceCount | B: $destCount | match pairs: $matches" + $(if($verify.Checked){" | identical: $identical | different: $different"}else{''})
        $export.Enabled=$matches -gt 0
        $deleteAll.Enabled=$identical -gt 0
    } catch {
        $status.Text='Scan failed: '+$_.Exception.Message
        [Windows.Forms.MessageBox]::Show($_.Exception.Message,'Scan error') | Out-Null
    } finally {$script:busy=$false;$form.Cursor=[Windows.Forms.Cursors]::Default;$scan.Enabled=$true}
})

# Floating image preview; release the image stream before showing.
$preview=New-Object Windows.Forms.Form
$preview.FormBorderStyle='FixedSingle';$preview.ShowInTaskbar=$false
$preview.StartPosition='Manual';$preview.ClientSize=New-Object Drawing.Size(300,280)
$preview.BackColor=[Drawing.Color]::FromArgb(30,36,45)
$picture=New-Object Windows.Forms.PictureBox
$picture.SetBounds(8,8,284,235);$picture.SizeMode='Zoom';$preview.Controls.Add($picture)
$previewLabel=New-Object Windows.Forms.Label
$previewLabel.SetBounds(8,248,284,25);$previewLabel.ForeColor='WhiteSmoke';$preview.Controls.Add($previewLabel)
function Hide-Preview {
    $preview.Hide();$picture.Image=$null
    if($script:previewImage){$script:previewImage.Dispose();$script:previewImage=$null}
    $script:previewPath=''
}
function Show-Preview($path) {
    if($script:previewPath -eq $path -and $preview.Visible){return}
    Hide-Preview
    if([IO.Path]::GetExtension($path) -notin @('.jpg','.jpeg','.png','.bmp','.gif','.tif','.tiff','.webp')){return}
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){return}
    try {
        $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try {
            $loaded=[Drawing.Image]::FromStream($stream)
            try {$script:previewImage=New-Object Drawing.Bitmap($loaded)}finally{$loaded.Dispose()}
        }finally{$stream.Dispose()}
        $picture.Image=$script:previewImage
        $previewLabel.Text=[IO.Path]::GetFileName($path)
        $point=[Windows.Forms.Cursor]::Position
        $bounds=[Windows.Forms.Screen]::FromPoint($point).WorkingArea
        $preview.Location=New-Object Drawing.Point([Math]::Min($point.X+18,$bounds.Right-310),[Math]::Min($point.Y+18,$bounds.Bottom-310))
        $preview.Show($form);$script:previewPath=$path
    }catch{Hide-Preview}
}
function Is-InRoot($path,$root) {
    if(!$root){return $false}
    $base=[IO.Path]::GetFullPath($root).TrimEnd('\','/')
    return [IO.Path]::GetFullPath($path).StartsWith(($base+'\'),[StringComparison]::OrdinalIgnoreCase)
}
function Verify-Match($r) {
    if($r.Status -ne 'IDENTICAL'){return $false}
    if(!(Is-InRoot $r.SourcePath $script:rootA) -or !(Is-InRoot $r.DestinationPath $script:rootB)){return $false}
    if(!(Test-Path -LiteralPath $r.SourcePath -PathType Leaf) -or !(Test-Path -LiteralPath $r.DestinationPath -PathType Leaf)){return $false}
    $a=Get-Item -LiteralPath $r.SourcePath -Force
    $b=Get-Item -LiteralPath $r.DestinationPath -Force
    if(($a.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or ($b.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){return $false}
    if($a.Length -ne $b.Length){return $false}
    return ((Get-FileHash -LiteralPath $r.SourcePath -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $r.DestinationPath -Algorithm SHA256).Hash)
}
function Delete-Sources($candidates) {
    if($script:busy){return}
    $chosen=@($candidates | Where-Object {$_.Status -eq 'IDENTICAL'} | Group-Object SourcePath | ForEach-Object {$_.Group[0]})
    if(!$chosen.Count){return}
    $message="Move $($chosen.Count) verified identical source file(s) from A to the Windows Recycle Bin? Destination B will NOT change. SHA-256 is checked again immediately before deletion."
    if([Windows.Forms.MessageBox]::Show($form,$message,'Confirm delete from A',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Warning,[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne [Windows.Forms.DialogResult]::Yes){return}
    $deleted=0;$errors=New-Object 'System.Collections.Generic.List[string]'
    $form.Cursor=[Windows.Forms.Cursors]::WaitCursor
    try {
        foreach($r in $chosen){
            try {
                if(!(Verify-Match $r)){throw 'The files changed, are missing or no longer match.'}
                [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($r.SourcePath,[Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,[Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,[Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
                $deleted++
            }catch{$errors.Add("$($r.SourcePath): $($_.Exception.Message)")}
        }
        $script:report=@($script:report | Where-Object {Test-Path -LiteralPath $_.SourcePath -PathType Leaf})
        $results.Items.Clear()
        foreach($r in $script:report){
            $item=New-Object Windows.Forms.ListViewItem($r.Status)
            [void]$item.SubItems.Add($r.SourcePath);[void]$item.SubItems.Add($r.DestinationPath)
            [void]$item.SubItems.Add([string]$r.SourceBytes);[void]$item.SubItems.Add([string]$r.DestinationBytes)
            [void]$item.SubItems.Add($(if($r.Status -eq 'IDENTICAL'){'Delete A'}else{'-'}))
            $item.Tag=$r
            if($r.Status -eq 'IDENTICAL'){$item.ForeColor=[Drawing.Color]::LightGreen}
            [void]$results.Items.Add($item)
        }
        $deleteAll.Enabled=@($script:report | Where-Object Status -eq 'IDENTICAL').Count -gt 0
        $export.Enabled=$script:report.Count -gt 0
        $status.Text="Moved $deleted source files to Recycle Bin. Failures: $($errors.Count)"
        if($errors.Count){[Windows.Forms.MessageBox]::Show($form,($errors -join [Environment]::NewLine),'Deletion errors') | Out-Null}
    }finally{$form.Cursor=[Windows.Forms.Cursors]::Default}
}
$results.Add_MouseMove({
    if($script:busy){return}
    $hit=$results.HitTest($_.X,$_.Y)
    if($hit.Item -and $hit.Item.Tag){Show-Preview $hit.Item.Tag.SourcePath}else{Hide-Preview}
})
$results.Add_MouseLeave({Hide-Preview})
$results.Add_MouseClick({
    if($_.Button -ne [Windows.Forms.MouseButtons]::Left){return}
    $hit=$results.HitTest($_.X,$_.Y)
    if($hit.Item -and $hit.SubItem -and $hit.SubItem -eq $hit.Item.SubItems[5] -and $hit.Item.Tag.Status -eq 'IDENTICAL'){
        Hide-Preview;Delete-Sources @($hit.Item.Tag)
    }
})
$deleteAll.Add_Click({Hide-Preview;Delete-Sources $script:report})
foreach($field in @($source,$dest,$sourceSub,$destSub,$verify)){
    if($field -is [Windows.Forms.CheckBox]){$field.Add_CheckedChanged({$script:report=@();$results.Items.Clear();$deleteAll.Enabled=$false;$export.Enabled=$false})}
    else{$field.Add_TextChanged({$script:report=@();$results.Items.Clear();$deleteAll.Enabled=$false;$export.Enabled=$false})}
}
$form.Add_FormClosing({Hide-Preview;$preview.Close()})
[void]$form.ShowDialog()
$form.Dispose();$preview.Dispose()
