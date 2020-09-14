function P4Checkout($changes, $depotPath, $desc){
    
    $output = "Change: new`r`nDescription:$desc" | p4 change -i
    if($LastExitCode -ne 0 -or -not ($output -match 'Change\s(\d+)\screated')) {throw "P4: failed to create a new change list`r`n$output"}
    
    $changelist = $Matches[1]
    Write-Host "P4: Change list $changelist created"
    
    try{
        $log = $changes | ForEach-Object{$_ | add-member -NotePropertyName LocalFileName -NotePropertyValue $(P4GetLocalFileName("$depotPath$($_.FileName)")) -force}
        Write-Host "P4: Populated local perforce files"
        
        $log = $changes | Where-Object State -ne 'A' | ForEach-Object{$_.LocalFileName} | p4 -x - sync -f 2>&1
        if($LastExitCode -ne 0) {throw "P4: failed to sync '$($file.FileName)'`r`n$log"}

        #try fetching added files in case a file with the same name was added by another P4 user
        $added = $changes | Where-Object State -eq 'A'
        $log = $added | ForEach-Object{$_.LocalFileName} | p4 -x - sync -f 2>&1
        if($LastExitCode -ne 0 -or [regex]::Matches($log, "(deleted as|no such file)").Count -ne $added.Count) {throw "P4: failed to sync '$($file.FileName)'`r`n$log"}
        Write-Host "P4: Refreshed all files"

        #process all added files
        $added | ForEach-Object {CopyFile $_.FileName $_.LocalFileName}
        $log = $added | ForEach-Object{$_.LocalFileName} | p4 -x - add -c $changelist 2>&1
        if($LastExitCode -ne 0) {throw "P4: failed to add files.`r`n$log"}
        if($log){ Write-Host $log}
        Write-Host "P4: Checked out added files"

        #process all deleted files
        $log = $changes | Where-Object State -eq 'D' | ForEach-Object{$_.LocalFileName} | p4 -x - delete -c $changelist 2>&1
        if($LastExitCode -ne 0) {throw "P4: failed to delete files.`r`n$log"}
        if($log){ Write-Host $log}
        Write-Host "P4: Checked out deleted files"

        #process all modified files
        $modified = $changes | Where-Object State -eq 'M'
        $log = $modified | ForEach-Object{$_.LocalFileName} | p4 -x - edit -c $changelist 2>&1
        if($LastExitCode -ne 0 -or [regex]::Matches($log, "opened for edit").Count -ne $modified.Count) {throw "P4: failed to checkout files.`r`n$log"}
        if($log){ Write-Host $log}
        $modified | ForEach-Object {CopyFile $_.FileName $_.LocalFileName}
        Write-Host "P4: Checked out modified files"
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

function P4Submit($type, $id, $branch, $svcUser, $user, $desc, $shelve = 'y', $deleteShelveDays = 10){
    if($type -ne 'branch' -and $type -ne 'commit'){throw "Git: invalid parameter value for type: '$type' has be be one of ['commit', 'branch']" }
    Write-Host "P4: Starting P4Submit - $type '$id' User '$user' Desc '$desc' P4 client: '$Env:P4Client'"

    P4ClearAll $svcUser $deleteShelveDays
    $syncResult = GitP4Sync $svcUser $branch 1000 #force to sync all changes, typically shouldn't have more than a few if any due to continuous sync
    if(-not $syncResult.UpToDate){throw "P4: Coudn't sync all changes"}

    $lastChange = $syncResult.LastChange
    $depot = $syncResult.DepotPath

    $sBranch = GitFetchMerge $type $id $branch
    $changes = GitGetChanges $branch $sBranch
    P4LoginFor $svcUser $user
    $changelist = P4Checkout $changes $depot $desc
    $log = p4 changes -m 1 -s submitted "$depot..."
    if($LastExitCode -ne 0) {throw "P4: failed to get submitted changes"}
    $latestChange = ([regex]::Matches($log, 'Change (\d+) on \d{4}\/\d{2}\/\d{2} by'))[0].Groups[1].Value
    if($latestChange -eq $lastChange){
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

function P4Login($svcUser, $pass){
    $Env:P4User = $svcUser
    $log = $pass | p4 login
    if($LastExitCode -ne 0) {throw "P4: failed to login by '$svcUser'`r`n$log"}
}

function P4LoginFor($svcUser, $user){
    $Env:P4User = $svcUser
    $log = p4 login $user
    if($LastExitCode -ne 0) {throw "P4: failed to login by '$svcUser' on behalf of '$user'`r`n$log"}
    $Env:P4User = $user
}

function P4DeleteChange($changelist, $deleteShelved = ''){
    if($deleteShelved -eq 'y'){
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

function P4ClearAll($svcUser, $deleteShelveDays){
    $workspace = $Env:P4Client
    Write-Host "P4: Started Deleting all change lists in workspace $workspace"
    $data = p4 changes -c $workspace -s pending
    if($data){
        $changeLists = [regex]::Matches($data, 'Change\s(\d+)\son\s(\d{4}\/\d{2}\/\d{2}) by (.*?)@')
	}
    if($changeLists){
        $changes = $changeLists | ForEach-Object{[PSCustomObject]@{User=$_.Groups[3].value; Number=$_.Groups[1].value; Date=[DateTime]::ParseExact($_.Groups[2].value,'yyyy/MM/dd',[CultureInfo].InvarianCulture)}}
	}
    if($changes.Count -eq 0){
        Write-Host "P4: No change lists found"
        return
	}
    foreach($change in $changes){
        if($change.Date -lt (Get-Date).AddDays(-1 * $deleteShelveDays)){
            Write-Host "P4: Deleting shelved files for $change"
            $deleteShelved = 'y'
        } else{
            $deleteShelved = 'n'
		}
        P4LoginFor $svcUser $change.User
        P4DeleteChange $change.Number $deleteShelved
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