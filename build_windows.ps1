$ErrorActionPreference = 'Stop'
dotnet publish "$PSScriptRoot\YueXinCat.csproj" -c Release -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}
Write-Host "发布完成：$PSScriptRoot\bin\Release\net8.0-windows\win-x64\publish\YueXinCat.exe"
