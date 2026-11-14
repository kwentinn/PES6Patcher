# Defensive line height from a `pes6r positions` CSV. For each sample and team, oriented so that the own goal is at 0
# and the opposite goal at 23600: ball depth, back line (mean of the 4 deepest outfielders), deepest and
# highest outfielder, team length and width, split by who has the ball.
# Usage: pes6depth.ps1 -In positions.csv   (writes positions-p-teams.csv next to it)
param([string]$In, [string]$Field = 'p')
$rows = Import-Csv $In
$GOAL = 11800; $fx = "${Field}x"; $fz = "${Field}z"
$out = foreach ($s in ($rows | Group-Object ms)) {
    $g = $s.Group
    $carrier = [int]$g[0].carrier
    $ct = -1; if ($carrier -ge 1 -and $carrier -le 22) { $ct = [int]($g | Where-Object { [int]$_.id -eq $carrier }).team }
    foreach ($t in 0, 1) {
        $pl = $g | Where-Object { [int]$_.team -eq $t }
        $k = $pl | Where-Object { [int]$_.slot -eq 0 }
        if (-not $k) { continue }
        $dir = if ([double]$k.px -lt 0) { 1 } else { -1 }
        $of = $pl | Where-Object { [int]$_.slot -ne 0 }
        $xs = $of | ForEach-Object { [double]$_.$fx * $dir + $GOAL } | Sort-Object
        $zs = $of | ForEach-Object { [double]$_.$fz } | Sort-Object
        if ($xs.Count -lt 10) { continue }
        [pscustomobject]@{
            ms = [int]$s.Name; team = $t; poss = if ($ct -eq $t) { 'own' } elseif ($ct -ge 0) { 'opp' } else { 'none' }
            ball = [double]$g[0].bx * $dir + $GOAL
            line = ($xs[0..3] | Measure-Object -Average).Average
            deep = $xs[0]; high = $xs[9]; length = $xs[9] - $xs[0]; width = $zs[9] - $zs[0]
            st7 = @($of | Where-Object { $_.state -eq '7' }).Count
        }
    }
}
$out | Export-Csv ($In -replace '\.csv$', "-$Field-teams.csv") -NoTypeInformation
"field=$Field, samples=$(@($out).Count / 2)"
"Ball zone (thirds of the pitch from the own goal), by team and possession: mean back line, line - ball, length, width, n"
$out | Group-Object team, poss, { [math]::Min(5, [math]::Max(0, [math]::Floor($_.ball / 3934))) } | Sort-Object Name | ForEach-Object {
    $m = $_.Group
    '{0,-14} line={1,6:F0} ball={2,6:F0} line-ball={3,6:F0} deep={4,6:F0} length={5,6:F0} width={6,6:F0} st7={7,4:F1} n={8}' -f $_.Name,
        ($m.line | Measure-Object -Average).Average, ($m.ball | Measure-Object -Average).Average,
        (($m | ForEach-Object { $_.line - $_.ball }) | Measure-Object -Average).Average,
        ($m.deep | Measure-Object -Average).Average, ($m.length | Measure-Object -Average).Average,
        ($m.width | Measure-Object -Average).Average, ($m.st7 | Measure-Object -Average).Average, $m.Count
}
