import-module -force .\P4.ps1
import-module -force .\Git.ps1
set-location $GitDir
$changes = gitgetchanges master
$depotPath = ""
P4Checkout $changes $depotPath "Testing changes" 