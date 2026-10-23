# -*- coding: utf-8 -*-
"""
PCL 2.14.0.0 一键构建脚本（Release）
====================================
本机无 Visual Studio，使用 .NET SDK 9 + .NET Framework 4.8 参考程序集构建。

前置依赖（首次构建前需准备好，均与本脚本一同放在隔离目录）：
  - .NET SDK 9：C:/Users/sky/.workbuddy-ai/binaries/dotnet/sdk9/sdk/dotnet.exe
  - net48 参考程序集：C:/Users/sky/.workbuddy-ai/binaries/dotnet/refpack48/build/.NETFramework/v4.8/
  - MeloongCore 源码：PCL-2.14.0.0/MeloongCore/（git 子模块，缺失时从
    https://github.com/Meloong-Git/MeloongCore 按 PCL 仓库 .gitmodules 锁定的 commit 获取）

构建原理（为什么需要这些步骤）：
  1. 经典 VB/WPF 工程不会被 SDK 自动接入 WPF 标记编译（MarkupCompilePass1），
     因此构建时临时在 vbproj 末尾导入 SDK 自带的 Microsoft.WinFX.targets；
  2. dotnet MSBuild 下 VB 的 mscorlib / Microsoft.VisualBasic 走 FrameworkPathOverride
     隐式解析（默认指向 SDK 目录，没有这些 DLL），需要显式 HintPath 引用；
  3. 经典工程没有 NuGet 生成文件，跳过临时 WPF 工程对其的复制；
  4. 构建结束（无论成败）都会还原 vbproj，不留下任何痕迹。

产物：Plain Craft Launcher 2/bin/Plain Craft Launcher 2.exe
"""
import io, os, shutil, subprocess, sys

ROOT = os.path.dirname(os.path.abspath(__file__))                      # PCL-2.14.0.0 目录
VBPROJ = os.path.join(ROOT, "Plain Craft Launcher 2", "Plain Craft Launcher 2.vbproj")
BACKUP = os.path.join(os.environ.get("TEMP", "."), "vbproj.bak.build")
DOTNET = r"C:\Users\sky\.workbuddy-ai\binaries\dotnet\sdk9\sdk\dotnet.exe"
REFPACK = r"C:\Users\sky\.workbuddy-ai\binaries\dotnet\refpack48\build"
WINFX = r"C:\Users\sky\.workbuddy-ai\binaries\dotnet\sdk9\sdk\sdk\9.0.310\Sdks\Microsoft.NET.Sdk.WindowsDesktop\targets\Microsoft.WinFX.targets"

WINFX_IMPORT = '  <Import Project="%s" />\n</Project>' % WINFX
EXPLICIT_REFS = '''  <ItemGroup>
    <!-- 构建接线：显式引用 mscorlib / Microsoft.VisualBasic -->
    <Reference Include="mscorlib">
      <HintPath>%s\\.NETFramework\\v4.8\\mscorlib.dll</HintPath>
    </Reference>
    <Reference Include="Microsoft.VisualBasic">
      <HintPath>%s\\.NETFramework\\v4.8\\Microsoft.VisualBasic.dll</HintPath>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <Reference Include="CacheCow.Client, Version=2.13.1.0, Culture=neutral, processorArchitecture=MSIL">''' % (REFPACK, REFPACK)

def wire_project():
    with io.open(VBPROJ, "r", encoding="utf-8-sig") as f:
        s = f.read()
    old = s
    s = s.replace('  <Import Project="$(MSBuildToolsPath)\\Microsoft.VisualBasic.targets" />\n</Project>',
                  '  <Import Project="$(MSBuildToolsPath)\\Microsoft.VisualBasic.targets" />\n' + WINFX_IMPORT)
    assert s != old, "WinFX 导入接线失败：锚点未找到"
    old = s
    s = s.replace('  <ItemGroup>\n    <Reference Include="CacheCow.Client, Version=2.13.1.0, Culture=neutral, processorArchitecture=MSIL">',
                  EXPLICIT_REFS, 1)
    assert s != old, "显式引用接线失败：锚点未找到"
    with io.open(VBPROJ, "w", encoding="utf-8", newline="") as f:
        f.write(s)

def restore_project():
    if os.path.exists(BACKUP):
        shutil.copyfile(BACKUP, VBPROJ)

def main():
    if not os.path.exists(DOTNET):
        sys.exit("找不到 .NET SDK：%s（请先按脚本头部说明准备）" % DOTNET)
    shutil.copyfile(VBPROJ, BACKUP)
    try:
        print("== 正在写入临时构建接线 ==")
        wire_project()
        env = dict(os.environ)
        env["DOTNET_ROOT"] = os.path.dirname(os.path.dirname(DOTNET))
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        env["DOTNET_NOLOGO"] = "1"
        env["NUGET_PACKAGES"] = r"C:\Users\sky\.workbuddy-ai\binaries\dotnet\nuget"
        args = [
            DOTNET, "build",
            os.path.join("Plain Craft Launcher 2", "Plain Craft Launcher 2.vbproj"),
            "-c", "Release",
            "--configfile", r"C:\Users\sky\.workbuddy-ai\binaries\dotnet\NuGet.config",
            "-p:TargetFrameworkRootPath=" + REFPACK,
            "-p:IncludePackageReferencesDuringMarkupCompilation=false",
        ]
        print("== 正在构建 ==")
        rc = subprocess.call(args, cwd=ROOT, env=env)
        return rc
    finally:
        print("== 还原 vbproj ==")
        restore_project()

if __name__ == "__main__":
    sys.exit(main())
