<#
确保本地 NuGet feed(packages/)包含 Dsh.Runtime 依赖的 DryIoc.dll。
需要的版本直接从 src/Dsh.Runtime/Dsh.Runtime.csproj 的 PackageReference 读取。
构建用的 commit 取 dadhi/DryIoc 默认分支的最新 commit(运行时解析, 不硬编码)。
该版本不在 nuget.org 的 DryIoc 线上, 且包 ID 是 DryIoc.dll 而非 DryIoc。
默认从源码构建, 已有则跳过, 不依赖 GitHub CLI。

用法:
    pwsh -File scripts/fetch-dryioc-feed.ps1
    pwsh -File scripts/fetch-dryioc-feed.ps1 -Proxy http://<host>:<port>   # 访问 GitHub 走指定代理, 不传则直连
    pwsh -File scripts/fetch-dryioc-feed.ps1 -UseGhArtifact   # 本机有已登录的 gh CLI 时, 优先直接从 CI 产物下载

依赖: git 与 dotnet SDK。
#>
[CmdletBinding()]
param(
    [string]$Proxy,
    [switch]$UseGhArtifact
)

$ErrorActionPreference = 'Stop'

# 指定代理时对本脚本启动的子进程生效(git/gh/dotnet 都识别这些标准变量); 未指定则直连。
if ($Proxy) {
    if ($Proxy -notmatch '^\w+://') {
        $Proxy = "http://$Proxy"
    }
    $env:HTTP_PROXY = $Proxy
    $env:HTTPS_PROXY = $Proxy
    $env:ALL_PROXY = $Proxy
    Write-Host "使用代理: $Proxy"
}

$RootDir = Split-Path -Parent $PSScriptRoot
$FeedDir = Join-Path $RootDir 'packages'
$WorkDir = Join-Path $RootDir 'artifacts/.dryioc-src'
$Csproj = Join-Path $RootDir 'src/Dsh.Runtime/Dsh.Runtime.csproj'

$PackageId = 'DryIoc.dll'
$RepoUrl = 'https://github.com/dadhi/DryIoc.git'
$TargetFramework = 'net9.0'

$GhRepo = 'dadhi/DryIoc'
$GhWorkflow = 'ci.yml'
$GhArtifact = 'packages'

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "缺少命令: $Name"
    }
}

function Assert-DotnetSdk {
    Assert-Command dotnet
    if (-not (dotnet --list-sdks 2>$null)) {
        throw 'dotnet 未安装 .NET SDK(只找到运行时); 请安装 .NET SDK, 或把含 SDK 的 dotnet 目录加入 PATH。'
    }
}

function Get-RequiredPackageVersion {
    $text = Get-Content -LiteralPath $Csproj -Raw
    $match = [regex]::Match($text, 'Include="DryIoc\.dll"\s+Version="([^"]+)"')
    if (-not $match.Success) {
        throw "无法从 $Csproj 读取 DryIoc.dll 的版本。"
    }
    return $match.Groups[1].Value
}

function Get-LatestCommit {
    Assert-Command git
    $line = git ls-remote $RepoUrl HEAD 2>$null | Select-Object -First 1
    if (-not $line) {
        throw "无法从 $RepoUrl 解析最新 commit。"
    }
    return ($line -split '\s+')[0]
}

# 从 CI 产物下载; 无 gh CLI 或下载不到时返回 $false, 由调用方回退。
function Get-FromGhArtifact([string]$TargetNupkg) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        return $false
    }
    gh auth status *> $null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }

    $runs = gh run list --repo $GhRepo --workflow $GhWorkflow --status success --limit 20 --json databaseId | ConvertFrom-Json
    foreach ($run in $runs) {
        $json = gh api "repos/$GhRepo/actions/runs/$($run.databaseId)/artifacts" 2>$null
        if (-not $json) {
            continue
        }
        $hasArtifact = ($json | ConvertFrom-Json).artifacts |
            Where-Object { $_.name -eq $GhArtifact -and -not $_.expired }
        if (-not $hasArtifact) {
            continue
        }

        if (Test-Path -LiteralPath $WorkDir) {
            Remove-Item -LiteralPath $WorkDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
        $staged = Join-Path $WorkDir ([System.IO.Path]::GetFileName($TargetNupkg))
        gh run download $run.databaseId --repo $GhRepo --name $GhArtifact --dir $WorkDir *> $null
        if (Test-Path -LiteralPath $staged) {
            New-Item -ItemType Directory -Path $FeedDir -Force | Out-Null
            Copy-Item -LiteralPath $staged -Destination $TargetNupkg -Force
            Remove-Item -LiteralPath $WorkDir -Recurse -Force
            return $true
        }
        Remove-Item -LiteralPath $WorkDir -Recurse -Force
        return $false
    }
    return $false
}

# 在默认分支最新 commit 上本地构建; 与 CI 一样产出包 ID 为 DryIoc.dll 的 nupkg。
function Build-FromSource([string]$Commit) {
    Assert-DotnetSdk

    if (Test-Path -LiteralPath $WorkDir) {
        Remove-Item -LiteralPath $WorkDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null

    Write-Host "从源码构建 $PackageId (commit $($Commit.Substring(0, 12)))..."
    git clone --filter=blob:none --no-checkout $RepoUrl $WorkDir
    git -C $WorkDir fetch --depth 1 origin $Commit
    git -C $WorkDir checkout --quiet $Commit

    # 部分克隆(blob:none)下 SourceLink 读取 git 会失败, 需关闭版本查询。
    dotnet build (Join-Path $WorkDir 'src/DryIoc/DryIoc.csproj') -c Release `
        -p:LatestSupportedNet=$TargetFramework -p:TargetFrameworks=$TargetFramework `
        -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false

    New-Item -ItemType Directory -Path $FeedDir -Force | Out-Null
    dotnet pack (Join-Path $WorkDir 'src/DryIoc/DryIoc.csproj') -c Release --no-build `
        -p:LatestSupportedNet=$TargetFramework -p:TargetFrameworks=$TargetFramework `
        -p:NoWarn=NU5129 -p:TreatWarningsAsErrors=false `
        -p:EnableSourceControlManagerQueries=false -p:EnableSourceLink=false `
        -o $FeedDir

    Remove-Item -LiteralPath $WorkDir -Recurse -Force
}

# 检查 feed 里是否出现与 csproj 要求不符的新版本, 给出明确提示。
function Assert-ProducedVersion([string]$TargetNupkg, [string]$Version) {
    if (Test-Path -LiteralPath $TargetNupkg) {
        return
    }
    $produced = Get-ChildItem -LiteralPath $FeedDir -Filter "$PackageId.*.nupkg" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($produced) {
        throw "上游最新产出的版本是 $($produced.Name), 与 csproj 要求的 $Version 不一致; 请同步更新 $Csproj 的 PackageReference。"
    }
    throw "构建结束但未在 $FeedDir 找到任何 $PackageId.*.nupkg, 请检查上面的输出。"
}

$PackageVersion = Get-RequiredPackageVersion
$TargetNupkg = Join-Path $FeedDir "$PackageId.$PackageVersion.nupkg"
if (Test-Path -LiteralPath $TargetNupkg) {
    Write-Host "本地 feed 已包含 $PackageId $PackageVersion, 跳过: $TargetNupkg"
    exit 0
}

if ($UseGhArtifact) {
    if (Get-FromGhArtifact $TargetNupkg) {
        Write-Host "已从 CI 产物写入本地 feed: $TargetNupkg"
        exit 0
    }
    Write-Warning 'gh CLI 不可用或 CI 产物下载失败, 回退到源码构建。'
}

$Commit = Get-LatestCommit
Build-FromSource $Commit
Assert-ProducedVersion $TargetNupkg $PackageVersion

Write-Host "已写入本地 feed: $TargetNupkg"
Write-Host '下一步: dotnet restore'