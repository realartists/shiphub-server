
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

PowerShell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Unrestricted -Command ".\GCSettingsManagement.ps1 -serverGC $true -concurrentGC $false"

Exit /b
