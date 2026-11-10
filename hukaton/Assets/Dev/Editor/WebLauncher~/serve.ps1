# Local server for the Hakaton web build when Python is not installed (Windows PowerShell).
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$types = @{ '.html' = 'text/html; charset=utf-8'; '.js' = 'application/javascript'; '.wasm' = 'application/wasm';
            '.data' = 'application/octet-stream'; '.json' = 'application/json'; '.css' = 'text/css';
            '.png' = 'image/png'; '.jpg' = 'image/jpeg'; '.ico' = 'image/x-icon'; '.svg' = 'image/svg+xml' }
$port = 8800
while ($true) {
    $listener = New-Object System.Net.HttpListener
    $listener.Prefixes.Add("http://localhost:$port/")
    try { $listener.Start(); break } catch { $port++ }
}
$url = "http://localhost:$port/"
Write-Host "Game: $url"
Write-Host "Close this window to stop the server."
Start-Process $url
while ($listener.IsListening) {
    $context = $listener.GetContext()
    $response = $context.Response
    try {
        $path = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath.TrimStart('/'))
        if ($path -eq '') { $path = 'index.html' }
        $file = Join-Path $root $path
        if (Test-Path $file -PathType Leaf) {
            $bytes = [IO.File]::ReadAllBytes($file)
            $name = $file
            foreach ($suffix in '.unityweb', '.gz', '.br') { if ($name.EndsWith($suffix)) { $name = $name.Substring(0, $name.Length - $suffix.Length) } }
            $ext = [IO.Path]::GetExtension($name).ToLower()
            if ($types.ContainsKey($ext)) { $response.ContentType = $types[$ext] } else { $response.ContentType = 'application/octet-stream' }
            if ($file.EndsWith('.br')) { $response.AddHeader('Content-Encoding', 'br') }
            elseif ($file.EndsWith('.gz') -or ($file.EndsWith('.unityweb') -and $bytes.Length -gt 1 -and $bytes[0] -eq 0x1f -and $bytes[1] -eq 0x8b)) { $response.AddHeader('Content-Encoding', 'gzip') }
            $response.AddHeader('Cache-Control', 'no-cache')
            $response.ContentLength64 = $bytes.Length
            $response.OutputStream.Write($bytes, 0, $bytes.Length)
        } else { $response.StatusCode = 404 }
    } catch { $response.StatusCode = 500 }
    $response.Close()
}
