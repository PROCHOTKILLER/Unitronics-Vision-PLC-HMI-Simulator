$req = "/00GS01330122"
Write-Host "Full req: $req"

# Let's test if last 2 chars '22' is checksum:
$body = $req.Substring(0, $req.Length - 2) # "/00GS013301"
Write-Host "Body: $body"

# Sum from index 0:
$sum0 = 0
foreach ($c in [char[]]$body) { $sum0 += [int]$c }
Write-Host "Sum from 0: hex = $(($sum0 % 256).ToString('X2')), 2s comp = $(((-$sum0) % 256).ToString('X2'))"

# Sum from index 1 (skipping '/'):
$body1 = $body.Substring(1)
$sum1 = 0
foreach ($c in [char[]]$body1) { $sum1 += [int]$c }
Write-Host "Sum from 1: hex = $(($sum1 % 256).ToString('X2')), 2s comp = $(((-$sum1) % 256).ToString('X2'))"
