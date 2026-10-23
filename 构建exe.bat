@echo off
chcp 65001 >nul
rem PCL 2.14.0.0 一键构建（Release），依赖与原理详见 构建exe.py 头部注释
set PY=C:\Users\sky\.workbuddy-ai\binaries\python\versions\3.13.12\python.exe
if not exist "%PY%" set PY=python
"%PY%" "%~dp0构建exe.py"
pause
