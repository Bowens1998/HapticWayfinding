# bHaptics latency growth test (no Unity needed).
# Wear the vest, keep bHaptics Player running, close the HapticWayfinding demo, then run:
#   powershell -ExecutionPolicy Bypass -File tools\latency_test.ps1
#
# Three times you will hear a BEEP. At each beep the vibration switches from the FRONT middle columns
# to the BACK middle columns. For each beep, note how long it takes until you feel it on your back.
#   Beep 1  fresh connection                         -> baseline delay
#   Beep 2  after 60 s of fast direction changes     -> does traffic make the delay grow?
#   Beep 3  after 60 s holding still (1 refresh/s)   -> does the delay grow even without traffic?
# Interpretation:
#   2 >> 1  : too many changes for the Bluetooth link -> lower the change rate in the app
#   3 >> 1  : delay grows even when idle -> Bluetooth link / Player problem (other BT devices, distance, dongle)
#   all ~1  : vest path is fine; look at the app side

param([int]$LoadSeconds = 60, [int]$IdleSeconds = 60, [double]$ChangeHz = 16)

$uri = [Uri]"ws://127.0.0.1:15881/v2/feedbacks?app_id=edu.ufl.icic.hapticwayfinding&app_name=LatencyTest"
$ws = New-Object System.Net.WebSockets.ClientWebSocket
try { $ws.ConnectAsync($uri, [Threading.CancellationToken]::None).Wait(3000) | Out-Null } catch {}
if ($ws.State -ne 'Open') { Write-Host "Cannot connect to bHaptics Player at $uri"; exit 1 }
$sw = [Diagnostics.Stopwatch]::StartNew()
$sent = 0

function Send([string]$json) {
    $b = [Text.Encoding]::UTF8.GetBytes($json)
    $ws.SendAsync((New-Object ArraySegment[byte] -ArgumentList (,$b)), [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait() | Out-Null
    $script:sent++
}
function Stamp([string]$msg) { Write-Host ("[{0,7:N1} s] {1}" -f ($sw.ElapsedMilliseconds / 1000), $msg) }
function Beep { [System.Media.SystemSounds]::Exclamation.Play(); [console]::Beep(1200, 200) }

# Columns as (side, deviceCol) in clockwise order from the front-left (same as the Unity app with
# front idx0 = wearer's left, back idx0 = wearer's left). Middle columns are symmetric, so the beeps do
# not depend on the left/right mapping.
$ring = @(@('VestFront',0), @('VestFront',1), @('VestFront',2), @('VestFront',3), @('VestBack',3), @('VestBack',2), @('VestBack',1), @('VestBack',0))
$playing = @{ 'VestFront' = ''; 'VestBack' = '' }

function DotsFor([string]$side, [int[]]$cols) {
    $d = @(); foreach ($r in 0..4) { foreach ($c in $cols) { $d += ('{"index":' + ($r * 4 + $c) + ',"intensity":80}') } }; return ($d -join ',')
}
# Same strategy as the app: only changed keys are sent; frames last 3 s; optional full refresh.
function SetPattern([hashtable]$want, [bool]$full) {
    $items = @()
    foreach ($side in @('VestFront', 'VestBack')) {
        $dots = $want[$side]; if ($null -eq $dots) { $dots = '' }
        if ($dots -ne '') {
            if ($full -or $dots -ne $playing[$side]) { $items += ('{"Type":"frame","Key":"lt_' + $side + '","Frame":{"position":"' + $side + '","dotPoints":[' + $dots + '],"pathPoints":[],"durationMillis":3000}}') }
        } elseif ($full -or $playing[$side] -ne '') { $items += ('{"Type":"turnOff","Key":"lt_' + $side + '"}') }
        $playing[$side] = $dots
    }
    if ($items.Count -gt 0) { Send ('{"Submit":[' + ($items -join ',') + ']}') }
}
function PairPattern([int]$s) {
    $a = $ring[$s]; $b = $ring[($s + 1) % 8]; $want = @{ 'VestFront' = ''; 'VestBack' = '' }
    if ($a[0] -eq $b[0]) { $want[$a[0]] = DotsFor $a[0] @($a[1], $b[1]) }
    else { $want[$a[0]] = DotsFor $a[0] @($a[1]); $want[$b[0]] = DotsFor $b[0] @($b[1]) }
    return $want
}
$FRONT = PairPattern 1   # front middle two columns
$BACK = PairPattern 5    # back middle two columns

function HoldWithRefresh([hashtable]$pat, [double]$seconds) {
    $end = $sw.ElapsedMilliseconds + $seconds * 1000; $next = 0
    while ($sw.ElapsedMilliseconds -lt $end) {
        if ($sw.ElapsedMilliseconds -ge $next) { SetPattern $pat ($next -ne 0); $next = $sw.ElapsedMilliseconds + 1000 }
        Start-Sleep -Milliseconds 20
    }
}
function SwitchTest([int]$n) {
    HoldWithRefresh $FRONT 3
    Stamp "BEEP $n -> switching FRONT to BACK now (messages sent so far: $sent)"
    Beep
    SetPattern $BACK $false
    HoldWithRefresh $BACK 4
    SetPattern @{ 'VestFront' = ''; 'VestBack' = '' } $false
    Start-Sleep -Seconds 2
}

Stamp "Connected. Test 1 (baseline) starts in 3 s: front vibration, then BEEP -> back."
Start-Sleep -Seconds 3
SwitchTest 1

Stamp ("Load phase: {0} s of direction changes at {1} Hz (like dragging the figure around)..." -f $LoadSeconds, $ChangeHz)
$end = $sw.ElapsedMilliseconds + $LoadSeconds * 1000; $s = 0; $period = [int](1000 / $ChangeHz); $refresh = 0
while ($sw.ElapsedMilliseconds -lt $end) {
    $full = $sw.ElapsedMilliseconds -ge $refresh; if ($full) { $refresh = $sw.ElapsedMilliseconds + 1000 }
    SetPattern (PairPattern $s) $full
    $s = ($s + 1) % 8
    Start-Sleep -Milliseconds $period
}
SwitchTest 2

Stamp "Idle phase: holding FRONT for $IdleSeconds s with one refresh per second..."
HoldWithRefresh $FRONT $IdleSeconds
Stamp "BEEP 3 -> switching FRONT to BACK now (messages sent so far: $sent)"
Beep
SetPattern $BACK $false
HoldWithRefresh $BACK 4

Send '{"Submit":[{"Type":"turnOffAll","Key":""}]}'
$ws.Abort()
Stamp "Done. Report the delay you felt after beep 1, 2 and 3."
