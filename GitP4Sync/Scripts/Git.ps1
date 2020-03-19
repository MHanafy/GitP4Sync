
function GitFetchMerge($type, $id, $branch){
    if($type -ne 'branch' -and ($type -ne 'commit')){throw "Git: invalid parameter value for type: '$type' has be be one of ['commit', 'branch']" }
    $log = git fetch origin $id
	if($LastExitCode -ne 0) {throw "Git: failed to fetch $type $id"}
    if($log){ Write-Host $log}
    $log = git reset --hard
	if($LastExitCode -ne 0) {throw "Git: failed to reset"}
	$log = git clean -df
	if($LastExitCode -ne 0) {throw "Git: failed to clean"}
    $nBranch = "$id-GitP4Submit";
    $log = git branch -D $nBranch #delete the branch if already there
    if($log){ Write-Host $log}
    
    if($type -eq 'commit'){
     $target = $id
	} else{
     $target = "origin/$id"
	}
    $log = git checkout -b $nbranch $target --no-track
  	if($LastExitCode -ne 0) {throw "Git: failed to checkout $type $id"}
    if($log){ Write-Host $log}
    
    $log = git merge $branch -s resolve
  	if($LastExitCode -ne 0) 
    {
        $log = git merge --abort
        throw "Git: failed to merge $branch into $type $id"
    }
    if($log){ Write-Host $log}
    $nBranch
}

function GitSetToken($token){
    $log = git remote -v
    $remote = ([regex]::Matches($log, 'https:\/\/(?>x\-access\-token:.*?@)?(github.com\/.*\/.*) \(fetch\)'))
    if(-not $remote){throw "Git: Failed to read git repo remote, ensure this is a github repo"}
    if($remote.Count -ne 1){throw "Git: Multiple remotes aren't supported, Please ensure to have a single remote in the repo'"}
    $repoUrl = $remote[0].Groups[1].Value
    $log = git remote set-url origin "https://x-access-token:$token@$repoUrl"
    if($LastExitCode -ne 0) {Write-Host "Git: failed to set github access token"}
}

function GitGetChanges($baseBranch, $branch =''){
    if($branch -eq ''){
        $data = git diff $baseBranch --name-status
    }
    else{
        $data = git diff $baseBranch $branch --name-status
    }
    
    $result = New-Object System.Collections.Generic.List[PSCustomObject]
    $files = $data | Select-String -Pattern "([MDA])\s*(.*)"
    foreach($match in $files.Matches){
        $result.Add([PSCustomObject]@{
            FileName = $match.Groups[2].Value
            State = $match.Groups[1].Value
        })
    }
    $result
}

function GitGetChanges2($baseBranch, $branch =''){
    if($branch -eq ''){
        $data = git diff $baseBranch --compact-summary
    }
    else{
        $data = git diff $baseBranch $branch --compact-summary
    }
    
    $countMatch = $data | Select-String -Pattern "(\d+) files changed"
    if($countMatch.Matches.Count -eq 0){ return}

    $count = $countMatch.Matches[0].Groups[1].Value
    $result = New-Object System.Collections.Generic.List[PSCustomObject]
    $files = $data | Select-String -Pattern "\s*(.+?)\s*(?>\((gone|new)\)\s*)?\|.*?"
    foreach($match in $files.Matches){
        switch ($match.Groups[2].Value) {
            gone { $state = 'D' }
            new {$state = 'A'}
            Default {$state = 'M'}
        }
        $result.Add([PSCustomObject]@{
            FileName = ($match.Groups[1].Value -replace "/","\")
            State = $state
        })
    }
    if($count -ne $result.Count) {
        throw "Git: Potential parsing error, non-matching data detected; count=$($count) rows=$($result.Count)"
    }
    $result
}

function GitGetRemote(){
    $data = git remote -v
    if($LastExitCode -ne 0) {throw "Git: failed to list remote"}
    $match = ([regex]::Matches($data, 'origin\s*.*https:\/\/(?>x\-access\-token:.*?@)?github\.com\/(.*)\/(.*).git\/?\s\(fetch\)'))[0]
    [PSCustomObject]@{
        Owner = $match.Groups[1].Value
        Repository = $match.Groups[2].Value
    }
}

function GitP4Sync($branch, $maxChanges = 10){
    Write-Host "P4 client: '$Env:P4Client'"
    $log = git p4 sync --branch $branch --max-changes $maxChanges
	if($LastExitCode -ne 0) {throw "Git: failed to do P4 sync: " + $log}
	$upToDate = $log -Contains "No changes to import!"
	if($log)
    { 
        Write-Host $log
        $changeCount = ([regex]::Matches($log, "Importing revision \d+" )).count
        $upToDate = $upToDate -or $changeCount -lt $maxChanges
    }
    #set local branch to the same point of P4 branch
    $log = git update-ref "refs/heads/$branch" "refs/remotes/p4/$branch"
    if($LastExitCode -ne 0) {throw "Git: failed to do update local branch ref: " + $log}
	if($log){ Write-Host $log}

    #push the branch, doesn't need checkout hence much faster
    $log = git push origin "${branch}:${branch}"
    if($LastExitCode -ne 0) {throw "Git: failed to push to remote: " + $log}
	if($log){ Write-Host $log}

    $log = git log -n 1 "p4/$branch"
    $change = ([regex]::Matches($log, '\[git-p4: depot-paths = "(.*?)": change = (\d+)]'))[0]
    $depotPath = $change.Groups[1].Value
    $lastChange = $change.Groups[2].Value

    [PSCustomObject]@{
        DepotPath = $depotPath
        ChangeCount = $changeCount
        LastChange = $lastChange
        UpToDate = $upToDate
    }
}

function GitP4Sync2($branch, $maxChanges = 10){
    Write-Host "P4 client: '$Env:P4Client'"
    $log = git reset --hard "origin/$branch"
	if($LastExitCode -ne 0) {throw "Git: failed to reset"}
	$log = git clean -df
	if($LastExitCode -ne 0) {throw "Git: failed to clean"}
	$log = git checkout $branch -f
	if($LastExitCode -ne 0) {throw "Git: failed to checkout $branch"}
	if($log){ Write-Host $log}
	$log = git p4 sync --branch $branch --max-changes $maxChanges
	if($LastExitCode -ne 0) {throw "Git: failed to do P4 sync: " + $log}
	$upToDate = $log -Contains "No changes to import!"
	if($log)
    { 
        Write-Host $log
        $changeCount = ([regex]::Matches($log, "Importing revision \d+" )).count
        $upToDate = $upToDate -or $changeCount -lt $maxChanges
    }
    #clean up any stale rebase
    $log = git rebase --abort

	$log = git rebase "p4/$branch"
	if($LastExitCode -ne 0) {throw "Git: failed to rebase on p4/$branch"}
	if($log){ Write-Host $log}
    $log = git log -n 1 "p4/$branch"
    $change = ([regex]::Matches($log, '\[git-p4: depot-paths = "(.*?)": change = (\d+)]'))[0]
    $depotPath = $change.Groups[1].Value
    $lastChange = $change.Groups[2].Value
	$log = git push
	if($LastExitCode -ne 0) {throw "Git: failed to push $branch"}
	if($log){ Write-Host $log}
    [PSCustomObject]@{
        DepotPath = $depotPath
        ChangeCount = $changeCount
        LastChange = $lastChange
        UpToDate = $upToDate
    }
}
