$ErrorActionPreference = 'Stop'
$frameworkFolder = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
$framework = Join-Path $env:WINDIR "Microsoft.NET\$frameworkFolder\v4.0.30319"
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$output = Join-Path $PSScriptRoot 'AppDeployStudio.exe'
$references = @(
    (Join-Path $wpf 'WindowsBase.dll')
    (Join-Path $wpf 'PresentationCore.dll')
    (Join-Path $wpf 'PresentationFramework.dll')
    'System.Xaml.dll'
    'System.Windows.Forms.dll'
    'System.IO.Compression.dll'
    'System.IO.Compression.FileSystem.dll'
    'System.Net.Http.dll'
)
$compilerArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', ("/win32manifest:$PSScriptRoot\Studio.manifest"),
    "/out:$output", "/win32icon:$PSScriptRoot\Brand.ico",
    "/resource:$PSScriptRoot\Studio.xaml,Studio.xaml", "/resource:$PSScriptRoot\Brand.png,Brand.png",
    "/resource:$PSScriptRoot\Translations.txt,Translations.txt")
$compilerArgs += @($references | ForEach-Object { "/reference:$_" })
$compilerArgs += @("$PSScriptRoot\Studio.cs", "$PSScriptRoot\PackageBuilder.cs",
    "$PSScriptRoot\Localization.cs", "$PSScriptRoot\TemplateCatalog.cs")
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Compilation échouée ($LASTEXITCODE)." }
Get-Item -LiteralPath $output
