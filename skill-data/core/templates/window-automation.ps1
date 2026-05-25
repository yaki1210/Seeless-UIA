# SeelessUIA: Window Automation Template
# Launch Calculator, click 5+3=, verify result, close

Write-Host "=== Calculator Automation ==="

# Launch Calculator
$wN = seeless-uia app launch calc
Write-Host "Calculator launched: $wN"
Start-Sleep 2

# Snapshot to discover button refs
Write-Host "Taking snapshot..."
seeless-uia snapshot $wN -i

# Click 5 + 3 =
seeless-uia click @e28   # num5
Start-Sleep -Milliseconds 200
seeless-uia click @e21   # plus
Start-Sleep -Milliseconds 200
seeless-uia click @e26   # num3
Start-Sleep -Milliseconds 200
seeless-uia click @e22   # equals

# Read result
Start-Sleep -Milliseconds 500
$result = seeless-uia get text control:Text --json | ConvertFrom-Json
Write-Host "Result: $($result.data.text)"

# Close
seeless-uia close
Write-Host "Done."
