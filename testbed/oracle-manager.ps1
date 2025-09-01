#!/usr/bin/env pwsh
# Oracle Container Manager (PowerShell)
# Usage: ./oracle-manager.ps1 [start|stop|status|logs|test]

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("start", "stop", "status", "logs", "test")]
    [string]$Command
)

$ContainerName = "oracle-xe-testbed"
$ImageName = "oracle/database:18.4.0-xe"
$OraclePassword = "mysecretpassword"
$HostPort = "1521"
$ContainerPort = "1521"

switch ($Command) {
    "start" {
        Write-Host "🔄 Starting Oracle container..." -ForegroundColor Yellow
        
        # Check if container already exists
        $existingContainer = docker ps -a --format "{{.Names}}" | Select-String "^$ContainerName$"
        
        if ($existingContainer) {
            Write-Host "📦 Container $ContainerName already exists" -ForegroundColor Blue
            $runningContainer = docker ps --format "{{.Names}}" | Select-String "^$ContainerName$"
            
            if ($runningContainer) {
                Write-Host "✅ Container is already running" -ForegroundColor Green
                exit 0
            } else {
                Write-Host "🔄 Starting existing container..." -ForegroundColor Yellow
                docker start $ContainerName
            }
        } else {
            Write-Host "🆕 Creating new Oracle container..." -ForegroundColor Blue
            docker run -d `
                --name $ContainerName `
                -p "${HostPort}:${ContainerPort}" `
                -e ORACLE_PWD=$OraclePassword `
                -e ORACLE_CHARACTERSET=AL32UTF8 `
                $ImageName
        }
        
        Write-Host "⏳ Waiting for Oracle to be ready (this takes 2-5 minutes)..." -ForegroundColor Yellow
        Write-Host "💡 You can monitor progress with: ./oracle-manager.ps1 logs" -ForegroundColor Cyan
        Write-Host "💡 Check status with: ./oracle-manager.ps1 status" -ForegroundColor Cyan
        
        # Wait for "DATABASE IS READY TO USE!" message
        $timeout = 600 # 10 minutes
        $elapsed = 0
        $ready = $false
        
        while ($elapsed -lt $timeout -and -not $ready) {
            $logs = docker logs $ContainerName 2>&1
            if ($logs -match "DATABASE IS READY TO USE!") {
                $ready = $true
                break
            }
            Start-Sleep 5
            $elapsed += 5
            Write-Host "." -NoNewline
        }
        
        Write-Host ""
        if ($ready) {
            Write-Host "✅ Oracle is ready!" -ForegroundColor Green
            Write-Host "🧪 You can now run tests with: `$env:INCLUDE_ORACLE='true'; dotnet run" -ForegroundColor Cyan
        } else {
            Write-Host "⏰ Timeout waiting for Oracle (10 minutes). Check logs: ./oracle-manager.ps1 logs" -ForegroundColor Red
            exit 1
        }
    }
    
    "stop" {
        Write-Host "🛑 Stopping Oracle container..." -ForegroundColor Yellow
        docker stop $ContainerName
        Write-Host "🗑️  Removing Oracle container..." -ForegroundColor Yellow
        docker rm $ContainerName
        Write-Host "✅ Oracle container stopped and removed" -ForegroundColor Green
    }
    
    "status" {
        $runningContainer = docker ps --format "{{.Names}}" | Select-String "^$ContainerName$"
        
        if ($runningContainer) {
            Write-Host "✅ Oracle container is running:" -ForegroundColor Green
            docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" | Select-String $ContainerName
            
            # Check if database is ready
            $logs = docker logs $ContainerName 2>&1
            if ($logs -match "DATABASE IS READY TO USE!") {
                Write-Host "✅ Oracle database is ready for connections" -ForegroundColor Green
                Write-Host "🧪 Run tests with: `$env:INCLUDE_ORACLE='true'; dotnet run" -ForegroundColor Cyan
            } else {
                Write-Host "⏳ Oracle is still initializing..." -ForegroundColor Yellow
                Write-Host "💡 Monitor with: ./oracle-manager.ps1 logs" -ForegroundColor Cyan
            }
        } else {
            $existingContainer = docker ps -a --format "{{.Names}}" | Select-String "^$ContainerName$"
            if ($existingContainer) {
                Write-Host "⏸️  Oracle container exists but is stopped:" -ForegroundColor Yellow
                docker ps -a --format "table {{.Names}}\t{{.Status}}" | Select-String $ContainerName
                Write-Host "🔄 Start it with: ./oracle-manager.ps1 start" -ForegroundColor Cyan
            } else {
                Write-Host "❌ Oracle container does not exist" -ForegroundColor Red
                Write-Host "🆕 Create it with: ./oracle-manager.ps1 start" -ForegroundColor Cyan
            }
        }
    }
    
    "logs" {
        Write-Host "📋 Oracle container logs (last 50 lines):" -ForegroundColor Blue
        Write-Host "==============================================="
        $existingContainer = docker ps -a --format "{{.Names}}" | Select-String "^$ContainerName$"
        if ($existingContainer) {
            docker logs --tail 50 $ContainerName
        } else {
            Write-Host "❌ Container $ContainerName does not exist" -ForegroundColor Red
        }
    }
    
    "test" {
        Write-Host "🧪 Testing Oracle connection..." -ForegroundColor Blue
        
        # Check if container is running
        $runningContainer = docker ps --format "{{.Names}}" | Select-String "^$ContainerName$"
        if (-not $runningContainer) {
            Write-Host "❌ Oracle container is not running. Start it with: ./oracle-manager.ps1 start" -ForegroundColor Red
            exit 1
        }
        
        # Test connection using sqlplus in container
        Write-Host "🔗 Testing connection to Oracle..." -ForegroundColor Yellow
        $result = docker exec $ContainerName bash -c "echo 'SELECT 1 FROM DUAL;' | sqlplus -s system/$OraclePassword@XE" 2>$null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Oracle connection successful!" -ForegroundColor Green
            Write-Host "🧪 Ready to run: `$env:INCLUDE_ORACLE='true'; dotnet run" -ForegroundColor Cyan
        } else {
            Write-Host "❌ Oracle connection failed. Database may still be initializing." -ForegroundColor Red
            Write-Host "💡 Check logs: ./oracle-manager.ps1 logs" -ForegroundColor Cyan
            exit 1
        }
    }
}