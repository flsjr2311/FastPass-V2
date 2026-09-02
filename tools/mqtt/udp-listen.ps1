# Escuta UDP na porta informada e imprime tudo que chegar (texto e hex).
# Uso: powershell -ExecutionPolicy Bypass -File tools\mqtt\udp-listen.ps1 -Port 17001 -Seconds 60
param(
    [int]$Port = 17001,
    [int]$Seconds = 60
)

$ErrorActionPreference = 'Stop'
$udp = New-Object System.Net.Sockets.UdpClient($Port)
$udp.Client.ReceiveTimeout = 1000
$remote = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
$deadline = (Get-Date).AddSeconds($Seconds)
Write-Host "Escutando UDP na porta $Port por $Seconds s..."
$count = 0
while ((Get-Date) -lt $deadline) {
    try {
        $bytes = $udp.Receive([ref]$remote)
        $count++
        $text = [System.Text.Encoding]::ASCII.GetString($bytes)
        $hex = ($bytes | ForEach-Object { $_.ToString('X2') }) -join ' '
        Write-Host ("[{0}] de {1}: '{2}'  | hex: {3}" -f (Get-Date -Format HH:mm:ss), $remote.Address, $text, $hex)
    } catch [System.Net.Sockets.SocketException] {
        # timeout de 1s — continua até o deadline
    }
}
$udp.Close()
Write-Host "Fim. Pacotes recebidos: $count"
