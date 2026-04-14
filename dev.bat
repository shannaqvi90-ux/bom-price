@echo off
start "API (port 7300)" cmd /k "dotnet run --project BomPriceApproval.API"
start "Web (port 5300)" cmd /k "cd bom-web && npm run dev"
