@echo off
cd /d "%~dp0"
set ASPNETCORE_URLS=http://localhost:5282
set ASPNETCORE_ENVIRONMENT=Development
Ocelot.Gateway.exe
