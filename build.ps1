$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler was not found.' }
$taskOutput = Join-Path $taskRoot 'NetWatch.exe'
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output',
    '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Runtime.Serialization.dll', '/r:Microsoft.CSharp.dll',
    ('/win32manifest:' + (Join-Path $taskRoot 'src\app.manifest')),
    ('/out:' + $taskOutput), (Join-Path $taskRoot 'src\Core.cs'), (Join-Path $taskRoot 'src\Program.cs'), (Join-Path $taskRoot 'src\Appearance.cs'))
if (Test-Path -LiteralPath (Join-Path $taskRoot 'src\app.ico')) { $arguments += '/win32icon:' + (Join-Path $taskRoot 'src\app.ico') }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output $taskOutput
