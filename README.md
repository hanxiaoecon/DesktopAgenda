# 桌面日程与待办

一个本地运行的 Windows 日程小组件。左侧用时间轴查看一天的安排，右侧按优先级管理待办。日程和待办可以分别建立，也可以关联后一起编辑。程序支持声音提醒，保存前会检查日程时间冲突。

## 下载与使用

从 [Releases](../../releases) 下载 `DesktopAgenda-Portable.exe`，放在可写的文件夹内双击运行。程序使用 Windows 10/11 自带的 .NET Framework。便携版手动启动，运行期间会按日程设置提醒。点窗口右上角 X 会收起到系统托盘。

程序首次打开时没有任何记录。日程与待办保存在程序旁的 `PortableData` 文件夹。换电脑时，先右键托盘图标选择「完全退出」，再把 EXE 和 `PortableData` 一起复制过去。把程序分享给别人时，只需分享 EXE。

## 从源码构建

在 Windows PowerShell 中，从仓库根目录运行：

```powershell
.\src\Build.ps1 -Portable
```

生成的 `DesktopAgenda-Portable.exe` 位于仓库根目录。源码中的自检可用 `--self-test <结果文件路径>` 运行。
