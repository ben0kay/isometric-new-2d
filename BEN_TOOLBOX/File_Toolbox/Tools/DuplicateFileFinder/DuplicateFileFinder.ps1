# Duplicate File Finder - read-only Windows PowerShell 5.1 GUI
# Compare source A to destination B by filename, optionally verifying SHA-256.
# No files are moved, renamed, or deleted.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Duplicate File Finder | File Toolbox'
$form.ClientSize = New-Object System.Drawing.Size(1100, 730)
$form.MinimumSize = New-Object System.Drawing.Size(900, 650)
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
[void](New-Label 'Destination folder (B) — search here for existing matches' 25 140 850 24)
$dest=New-PathInput 167
$destBrowse=New-Button 'Browse' 907 166 165 31
$destBrowse.Anchor='Top,Right'

$sourceSub=New-Object Windows.Forms.CheckBox
$sourceSub.Text='Include subfolders in A';$sourceSub.Checked=$false;$sourceSub.SetBounds(25,215,245,30)
$form.Controls.Add($sourceSub)
$destSub=New-Object Windows.Forms.CheckBox
$destSub.Text='Include subfolders in B';$destSub.Checked=$true;$destSub.SetBounds(290,215,245,30)
$form.Controls.Add($destSub)
$verify=New-Object Windows.Forms.CheckBox
$verify.Text='Verify content (SHA-256)';$verify.Checked=$true;$verify.SetBounds(570,215,270,30)
$form.Controls.Add($verify)

$scan=New-Button 'Scan for duplicates' 25 259 200 38
$export=New-Button 'Export CSV' 235 259 145 38
$export.Enabled=$false
$status=New-Label 'Ready. Filename matching is case-insensitive.' 395 263 680 33
$status.Anchor='Top,Left,Right'

$results=New-Object Windows.Forms.ListView
$results.SetBounds(25,316,1047,365)
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
$form.Controls.Add($results)
$footer=New-Label 'Read-only tool. No files are deleted, moved or renamed. Double-click a result to open its source folder.' 25 688 1040 26
$footer.Anchor='Bottom,Left,Right'

$script:report=@()
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
        $p=$results.SelectedItems[0].Tag
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
    $scan.Enabled=$false;$export.Enabled=$false;$results.Items.Clear();$script:report=@()
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
                $item.Tag=$file.Path
                if($state -eq 'IDENTICAL'){$item.ForeColor=[Drawing.Color]::LightGreen}
                elseif($state -eq 'DIFFERENT CONTENT'){$item.ForeColor=[Drawing.Color]::Khaki}
                [void]$results.Items.Add($item)
            }
            if(($sourceCount % 100) -eq 0){$status.Text="Checking source files: $sourceCount ...";$status.Refresh();[Windows.Forms.Application]::DoEvents()}
        }
        $status.Text="Scanned A: $sourceCount | B: $destCount | match pairs: $matches" + $(if($verify.Checked){" | identical: $identical | different: $different"}else{''})
        $export.Enabled=$matches -gt 0
    } catch {
        $status.Text='Scan failed: '+$_.Exception.Message
        [Windows.Forms.MessageBox]::Show($_.Exception.Message,'Scan error') | Out-Null
    } finally {$form.Cursor=[Windows.Forms.Cursors]::Default;$scan.Enabled=$true}
})
[void]$form.ShowDialog()
$form.Dispose()
