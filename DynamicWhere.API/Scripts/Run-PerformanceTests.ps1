# DynamicWhere Performance Test Runner
# This script runs all performance test endpoints and displays results

param(
    [string]$BaseUrl = "https://localhost:5001",
    [switch]$RunAll,
    [switch]$Detailed
)

Write-Host "????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "?     DynamicWhere.ex Performance Test Runner    ?" -ForegroundColor Cyan
Write-Host "????????????????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Ignore SSL certificate errors for local development
if (-not ([System.Management.Automation.PSTypeName]'ServerCertificateValidationCallback').Type) {
    $certCallback = @"
    using System;
    using System.Net;
 using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;
    public class ServerCertificateValidationCallback {
        public static void Ignore() {
            ServicePointManager.ServerCertificateValidationCallback += 
   delegate (
    Object obj, 
         X509Certificate certificate, 
           X509Chain chain, 
          SslPolicyErrors errors
                ) {
 return true;
};
      }
    }
"@
    Add-Type $certCallback
}
[ServerCertificateValidationCallback]::Ignore()

function Invoke-PerformanceTest {
    param(
        [string]$Endpoint,
        [string]$DisplayName
    )
    
    Write-Host "?? Running: " -NoNewline -ForegroundColor Yellow
    Write-Host "$DisplayName" -ForegroundColor White
    
    try {
        $url = "$BaseUrl/api/performancetest/$Endpoint"
        $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        
        if ($response.success) {
     Write-Host "  ? Success" -ForegroundColor Green
         Write-Host "  ??  Translation: " -NoNewline -ForegroundColor Gray
      Write-Host "$([math]::Round($response.metrics.translationTimeMs, 2))ms" -ForegroundColor Cyan
       
Write-Host "  ?? Execution:   " -NoNewline -ForegroundColor Gray
            Write-Host "$([math]::Round($response.metrics.executionTimeMs, 2))ms" -ForegroundColor Cyan
       
      Write-Host "  ?? Total:       " -NoNewline -ForegroundColor Gray
            Write-Host "$([math]::Round($response.metrics.totalTimeMs, 2))ms" -ForegroundColor Cyan
            
  Write-Host "  ?? Records:     " -NoNewline -ForegroundColor Gray
     Write-Host "$($response.metrics.recordsReturned)" -ForegroundColor Cyan
          
            if ($Detailed -and $response.metrics.queryGenerated) {
      Write-Host "  ?? SQL Query:" -ForegroundColor Gray
          Write-Host "     $($response.metrics.queryGenerated.Substring(0, [Math]::Min(100, $response.metrics.queryGenerated.Length)))..." -ForegroundColor DarkGray
       }
        }
        else {
          Write-Host "  ? Failed: $($response.message)" -ForegroundColor Red
        }
    }
    catch {
        Write-Host "  ? Error: $($_.Exception.Message)" -ForegroundColor Red
    }
    
Write-Host ""
}

function Show-Menu {
    Write-Host "Select a test to run:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  1.  Select Simple                  11. ToList Sync" -ForegroundColor White
    Write-Host "  2.  Select Complex           12. ToList Async" -ForegroundColor White
    Write-Host "  3.  Where Simple      13. Segment Intersections" -ForegroundColor White
    Write-Host "  4.  Where Text Search              14. Complex Customers" -ForegroundColor White
    Write-Host "  5.  Where Group AND       15. Complex Orders" -ForegroundColor White
    Write-Host "  6.  Where Group Nested" -ForegroundColor White
    Write-Host "  7.  Order Single            88. Run All Tests (Sequential)" -ForegroundColor Yellow
    Write-Host "  8.  Order Multiple            99. Run Quick Suite (5 tests)" -ForegroundColor Yellow
    Write-Host "  9.  Page Basic" -ForegroundColor White
    Write-Host "  10. Filter Complete     0.  Exit" -ForegroundColor DarkGray
    Write-Host ""
}

# Check if API is running
try {
    Write-Host "Checking API availability at $BaseUrl..." -ForegroundColor Gray
 $healthCheck = Invoke-RestMethod -Uri "$BaseUrl/api/performancetest/select/simple" -Method Get -ErrorAction Stop -TimeoutSec 5
    Write-Host "? API is responding!" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "? Cannot connect to API at $BaseUrl" -ForegroundColor Red
    Write-Host "   Make sure the application is running." -ForegroundColor Yellow
    Write-Host ""
    exit
}

# Run all tests if flag is set
if ($RunAll) {
    Write-Host "???????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host "Running All Performance Tests" -ForegroundColor Cyan
    Write-Host "???????????????????????????????????????????????????" -ForegroundColor Cyan
    Write-Host ""
    
    $startTime = Get-Date
  
 try {
        $url = "$BaseUrl/api/performancetest/all"
        $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        
$endTime = Get-Date
    $duration = ($endTime - $startTime).TotalMilliseconds
        
        Write-Host "????????????????????????????????????????????????????????????????" -ForegroundColor Green
        Write-Host "?       TEST SUITE RESULTS           ?" -ForegroundColor Green
        Write-Host "????????????????????????????????????????????????????????????????" -ForegroundColor Green
        Write-Host ""
        
        Write-Host "?? Summary:" -ForegroundColor Cyan
        Write-Host "   Total Tests:        $($response.totalTests)" -ForegroundColor White
        Write-Host "   ? Successful:      $($response.successfulTests)" -ForegroundColor Green
    Write-Host "   ? Failed:          $($response.failedTests)" -ForegroundColor $(if ($response.failedTests -gt 0) { "Red" } else { "Gray" })
     Write-Host "   ??  Total Time:  $([math]::Round($response.totalExecutionTimeMs, 2))ms" -ForegroundColor Cyan
        Write-Host "   ?? Total Records:   $($response.totalRecordsReturned)" -ForegroundColor White
        Write-Host ""
        
        Write-Host "?? Averages:" -ForegroundColor Cyan
        Write-Host "   Translation Time:   $([math]::Round($response.averageTranslationTimeMs, 2))ms" -ForegroundColor Cyan
   Write-Host "   Execution Time:     $([math]::Round($response.averageExecutionTimeMs, 2))ms" -ForegroundColor Cyan
        Write-Host "   Total Time:       $([math]::Round($response.averageTotalTimeMs, 2))ms" -ForegroundColor Cyan
        Write-Host ""
        
        if ($Detailed) {
  Write-Host "?? Individual Results:" -ForegroundColor Cyan
 Write-Host ""
 $response.results | ForEach-Object {
         $icon = if ($_.success) { "?" } else { "?" }
      $color = if ($_.success) { "Green" } else { "Red" }
                
                Write-Host "  $icon " -NoNewline -ForegroundColor $color
        Write-Host "$($_.testName)" -NoNewline -ForegroundColor White
       Write-Host " - " -NoNewline -ForegroundColor Gray
      Write-Host "$([math]::Round($_.metrics.totalTimeMs, 2))ms" -NoNewline -ForegroundColor Cyan
              Write-Host " ($($_.metrics.recordsReturned) records)" -ForegroundColor Gray
    }
Write-Host ""
      }
   
        # Performance rating
   $avgTotal = $response.averageTotalTimeMs
     $rating = if ($avgTotal -lt 50) { "????? Excellent" }
   elseif ($avgTotal -lt 100) { "???? Very Good" }
         elseif ($avgTotal -lt 200) { "??? Good" }
           elseif ($avgTotal -lt 500) { "?? Fair" }
        else { "? Needs Optimization" }
        
        Write-Host "?? Performance Rating: " -NoNewline -ForegroundColor Cyan
        Write-Host "$rating" -ForegroundColor Yellow
     Write-Host ""
    }
    catch {
        Write-Host "? Error running all tests: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    exit
}

# Interactive menu
while ($true) {
    Show-Menu
    $choice = Read-Host "Enter your choice"
    Write-Host ""
    
    switch ($choice) {
        "1"  { Invoke-PerformanceTest "select/simple" "Select Simple" }
        "2"  { Invoke-PerformanceTest "select/complex" "Select Complex" }
        "3"  { Invoke-PerformanceTest "where/simple" "Where Simple" }
        "4"  { Invoke-PerformanceTest "where/text-search" "Where Text Search" }
 "5"  { Invoke-PerformanceTest "where/group-and" "Where Group AND" }
        "6"  { Invoke-PerformanceTest "where/group-nested" "Where Group Nested" }
        "7"  { Invoke-PerformanceTest "order/single" "Order Single" }
     "8"  { Invoke-PerformanceTest "order/multiple" "Order Multiple" }
        "9"  { Invoke-PerformanceTest "page/basic" "Page Basic" }
        "10" { Invoke-PerformanceTest "filter/complete" "Filter Complete" }
        "11" { Invoke-PerformanceTest "tolist/sync" "ToList Sync" }
        "12" { Invoke-PerformanceTest "tolist/async" "ToList Async" }
        "13" { Invoke-PerformanceTest "segment/intersections" "Segment Intersections" }
   "14" { Invoke-PerformanceTest "complex/customers" "Complex Customers" }
  "15" { Invoke-PerformanceTest "complex/orders" "Complex Orders" }
    "88" { 
            Write-Host "Running all tests..." -ForegroundColor Yellow
       Write-Host ""
            & $PSCommandPath -BaseUrl $BaseUrl -RunAll -Detailed:$Detailed
     exit
        }
        "99" {
            Write-Host "Running Quick Test Suite..." -ForegroundColor Yellow
            Write-Host ""
         Invoke-PerformanceTest "select/simple" "Select Simple"
      Invoke-PerformanceTest "where/simple" "Where Simple"
       Invoke-PerformanceTest "order/single" "Order Single"
            Invoke-PerformanceTest "filter/complete" "Filter Complete"
            Invoke-PerformanceTest "tolist/async" "ToList Async"
       Write-Host "? Quick suite completed!" -ForegroundColor Green
    Write-Host ""
        }
  "0"  { 
       Write-Host "Goodbye! ??" -ForegroundColor Cyan
     exit 
        }
        default { 
Write-Host "? Invalid choice. Please try again." -ForegroundColor Red
       Write-Host ""
        }
    }
}
