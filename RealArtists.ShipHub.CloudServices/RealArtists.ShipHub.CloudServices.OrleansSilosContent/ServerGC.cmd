
REM *********************************************************
REM 
REM     Copyright (c) Microsoft. All rights reserved.
REM     This code is licensed under the Microsoft Public License.
REM     THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
REM     ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
REM     IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
REM     PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
REM 
REM *********************************************************

REM Check if the script is running in the Azure emulator and if so do not run
IF "%IsEmulated%"=="true" goto :EOF 

If "%ServerGCEnabled%"=="false" GOTO :ValidateBackground
If "%ServerGCEnabled%"=="False" GOTO :ValidateBackground
If "%ServerGCEnabled%"=="0" GOTO :ValidateBackground
SET ServerGCEnabled="true"

:ValidateBackground
If "%ConcurrentGCEnabled%"=="true" GOTO :CommandExecution
If "%ConcurrentGCEnabled%"=="True" GOTO :CommandExecution
If "%ConcurrentGCEnabled%"=="1" GOTO :CommandExecution
SET ConcurrentGCEnabled="false"

:CommandExecution

PowerShell.exe -executionpolicy unrestricted -command ".\GCSettingsManagement.ps1" -serverGC %ServerGCEnabled% -concurrentGC %ConcurrentGCEnabled%

Exit /b
