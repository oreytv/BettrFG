Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.Text = 'BettrFG Build'
$form.ClientSize = New-Object System.Drawing.Size(280, 210)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true

function New-Check([string]$text, [int]$y, [bool]$checked) {
    $cb = New-Object System.Windows.Forms.CheckBox
    $cb.Text = $text
    $cb.Checked = $checked
    $cb.Location = New-Object System.Drawing.Point(18, $y)
    $cb.Size = New-Object System.Drawing.Size(250, 22)
    $form.Controls.Add($cb)
    return $cb
}

$cbSteam = New-Check 'Copy plugin to Steam' 16 $true
$cbEpic  = New-Check 'Copy plugin to Epic' 44 $true
$cbDl    = New-Check 'Copy installer to Downloads' 72 $true
$cbKill  = New-Check 'Kill and relaunch Fall Guys' 100 $true

$ok = New-Object System.Windows.Forms.Button
$ok.Text = 'Build'
$ok.Location = New-Object System.Drawing.Point(30, 150)
$ok.Size = New-Object System.Drawing.Size(100, 32)
$ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
$form.Controls.Add($ok)
$form.AcceptButton = $ok

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'Compile only'
$cancel.Location = New-Object System.Drawing.Point(150, 150)
$cancel.Size = New-Object System.Drawing.Size(100, 32)
$cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($cancel)
$form.CancelButton = $cancel

$result = $form.ShowDialog()

function B([bool]$v) { if ($v) { '1' } else { '0' } }

if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    $steam = B $cbSteam.Checked
    $epic  = B $cbEpic.Checked
    $dl    = B $cbDl.Checked
    $kill  = B $cbKill.Checked
} else {
    $steam = '0'; $epic = '0'; $dl = '0'; $kill = '0'
}

Write-Output "BFGCHOICE:STEAM=$steam;EPIC=$epic;DL=$dl;KILL=$kill"
