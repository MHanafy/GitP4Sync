function P4Checkout($changes, $depotPath, $desc){
    $output = "Change: new`r`nDescription:$desc" | p4 change -i
    if($LastExitCode -ne 0 -or -not ($output -match 'Change\s(\d+)\screated')) {throw "P4: failed to create a new change list`r`n$output"}
    $changelist = $Matches[1]
    Write-Host "P4: Change list $changelist created"
    try{
        $changes | ForEach-Object{
            $fileName = $_.FileName
            $depotFileName = "$depotPath$fileName"
            $localFileName = P4GetLocalFileName($depotFileName)
            switch($_.State){
                'A'{ 
                        #try fetching the file in case a file with the same name was added by another P4 user
                        $log = p4 sync -f "$localFileName" 2>&1
                        if($LastExitCode -ne 0) {throw "P4: failed to sync '$fileName'`r`n$log"}
                        if(-not ($log -match "no such file\(s\)")) {throw "P4: Same file name was added by another user '$localFileName'`r`n$log"}
                        CopyFile $fileName $localFileName
                        $log = P4 add -c $changelist "$localFileName" 2>&1
                        if($LastExitCode -ne 0) {throw "P4: failed to add '$fileName'`r`n$log"}
                        if($log){ Write-Host $log}
                    } 
                'D'{ 
                        $log = p4 sync -f "$localFileName" 
                        if($LastExitCode -ne 0) {throw "P4: failed to sync '$fileName'`r`n$log"}
                        $log = P4 delete -c $changelist "$localFileName" 2>&1
                        if($LastExitCode -ne 0) {throw "P4: failed to delete '$fileName'`r`n$log"}
                        if($log){ Write-Host $log}
                    }
                'M'{ 
                        $log = p4 sync -f "$localFileName"
                        if($LastExitCode -ne 0) {throw "P4: failed to sync '$fileName'`r`n$log"}
                        $log = p4 edit -c $changelist $localFileName 2>&1
                        if($LastExitCode -ne 0 -or -not ($log -match "opened for edit")) 
                        {
                            throw "P4: failed to checkout file '$fileName'`r`n$output"
                        }
                        if($log){ Write-Host $log}
                        CopyFile $fileName $localFileName
                    }
                Default  {throw "Git: Invalid file state $_.State"}
		    }
	    }
	}
    catch{
        Write-Host "P4: Failed to checkout, reverting ..."
        P4DeleteChange $changelist
        throw
	}
    Write-Host "P4: Finished checkout"
    $changelist
}

function CopyFile($src, $dest){
    #Create the parent directory if it doesn't exist, to avoid copy errors for non-existent paths
    $dir = [System.IO.Path]::GetDirectoryName($dest)
    $log = New-Item -Type dir $dir -ErrorAction Ignore
    $log = Copy-Item $src $dest
    if(-not $?) {throw "P4: failed to copy '$src'`r`n$log"}
}

function P4GetLocalFileName($depotFileName){
    $fileName = [regex]::Escape(([regex]::Matches($depotFileName, '.*\/(.*)$'))[0].Groups[1].Value)
    $log = p4 where $depotFileName
    $exp = ".*?$fileName.*?$fileName\s*(.*$fileName)$"
    ([regex]::Matches($log, $exp))[0].Groups[1].Value
}

function P4Submit($type, $id, $branch, $user, $desc, $shelve = 'y', $deleteShelveDays = 10){
    if($type -ne 'branch' -and $type -ne 'commit'){throw "Git: invalid parameter value for type: '$type' has be be one of ['commit', 'branch']" }
    Write-Host "P4: Starting P4Submit - $type '$id' User '$user' Desc '$desc' P4 client: '$Env:P4Client'"

    P4ClearAll $deleteShelveDays
    $syncResult = GitP4Sync $branch 1000 #force to sync all changes, typically shouldn't have more than a few if any due to continuous sync
    if(-not $syncResult.UpToDate){throw "P4: Coudn't sync all changes"}

    $lastChange = $syncResult.LastChange
    $depot = $syncResult.DepotPath

    $sBranch = GitFetchMerge $type $id $branch
    $Env:P4User = $user
    $changes = GitGetChanges $branch $sBranch
    $changelist = P4Checkout $changes $depot $desc
    $log = p4 changes -m 1 -s submitted "$depot..."
    if($LastExitCode -ne 0) {throw "P4: failed to get submitted changes"}
    $latestChange = ([regex]::Matches($log, 'Change (\d+) on \d{4}\/\d{2}\/\d{2} by'))[0].Groups[1].Value
    if($latestChange = $lastChange){
        if($shelve -eq 'y'){
            $log = p4 shelve -c $changelist 2>&1
            if($LastExitCode -ne 0) {throw "P4: failed to shelve changelist '$changelist'`r`n$log"}
            Write-Host "Shelved changelist '$changelist'"
		} 
        else
        {
            $log = p4 submit -c $changelist 2>&1
            if($LastExitCode -ne 0) {throw "P4: failed to submit changelist '$changelist'`r`n$log"}
            Write-Host "Submitted changelist '$changelist'"
		}
	} 
    else{
        throw "A change is detected, aborting submit"
	}

    $log = git checkout $branch
    if($log){ Write-Host $log}
    $log = git branch -D $sBranch
    if($log){ Write-Host $log}
    $changelist
}

function P4DeleteChange($changelist, $user = '', $deleteShelved = ''){
    if($user){$Env:P4User = $user}
    if($deleteShelved){
        $output = p4 shelve -d -c $changelist 2>&1
        if($LastExitCode -ne 0) {Write-Host "P4: failed to delete shelved files`r`n$output"}
    }
    $output = P4 revert -c $changelist "//$Env:P4Client/..." 2>&1
    if($LastExitCode -eq 0) {Write-Host "P4: Reverted all files for change list '$changelist'"}
    else {Write-Host "P4: failed to revert files`r`n$output"}
    $output = p4 change -d $changelist 2>&1
    if($LastExitCode -eq 0) {Write-Host "P4: Deleted change list '$changelist'"}
    else {Write-Host "P4: failed to delete change list '$changelist'`r`n$output"}
}

function P4ClearAll($deleteShelveDays){
    $workspace = $Env:P4Client
    Write-Host "P4: Started Deleting all change lists in workspace $workspace"
    $data = p4 changes -c $workspace -s pending
    if($data){
        $matches = [regex]::Matches($data, 'Change\s(\d+)\son\s(\d{4}\/\d{2}\/\d{2}) by (.*?)@')
	}
    if($matches){
        $changes = $matches | %{[PSCustomObject]@{User=$_.Groups[3].value; Number=$_.Groups[1].value; Date=[DateTime]::ParseExact($_.Groups[2].value,'yyyy/MM/dd',[CultureInfo].InvarianCulture)}}
	}
    if($changes.Count -eq 0){
        Write-Host "P4: No change lists found"
        return
	}
    foreach($change in $changes){
        if($change.Date -lt (Get-Date).AddDays(-1 * $deleteShelveDays)){
            Write-Host "P4: Deleting shelved files for $change"
            $deleteShelved = 'y'
        }
        P4DeleteChange $change.Number $change.User $deleteShelved
    }
    Write-Host "P4: Finished Deleting all change lists"
}

function P4Sync($fileName){
    $output = p4 sync $fileName 2>&1 
    if($LastExitCode -ne 0) {throw "P4: failed to get latest for '$fileName'`r`n$output"}
    if($output -match "file\(s\) up-to-date" -or ($output -match "\s\-\srefreshing\s")) {return}
    #try one more time with force
    $output = p4 sync -f $fileName 2>&1
    if($LastExitCode -ne 0 -or -not($output -match "file\(s\) up-to-date" -or ($output -match "\s\-\srefreshing\s")))
    {
        throw "P4: still failed to get latest for '$fileName'`r`n$output"
    }
    if($output) { Write-Host $output }
}

function P4UserExists($userName){
    $log = p4 users $userName 2>&1 
    if($LastExitCode -ne 0) {throw "P4: failed to list users"}
    -not ($log -match "no such user\(s\)")
}