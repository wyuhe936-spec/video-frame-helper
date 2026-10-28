# 第三方组件说明

## FFmpeg

本项目的可执行文件中内置了一个 FFmpeg 5.0.1 构建，用于视频探测、解码、缩放和抽帧。

- 版本字符串：`ffmpeg version n5.0.1-4-ga5ebb3d25e-20220508`
- 架构：i686 / mingw32
- 构建选项包含：`--enable-gpl --enable-version3`、`--enable-ffnvcodec`、`--enable-cuda-llvm`、`--enable-libx264` 等。
- 许可：GNU General Public License v3（GPLv3）。完整的 GPLv3 文本见 [licenses/GPL-3.0.txt](licenses/GPL-3.0.txt)。
- 对应源码：
  - FFmpeg 5.0.1 发布包：<https://ffmpeg.org/releases/ffmpeg-5.0.1.tar.xz>
  - FFmpeg 源码仓库：<https://github.com/FFmpeg/FFmpeg>
  - 版本字符串中的提交：`a5ebb3d25e`

FFmpeg 是 Fabrice Bellard 发起的项目商标。本项目与 FFmpeg 官方不存在隶属关系，也未获得 FFmpeg 官方背书。

如果重新分发本项目的可执行文件，请同时保留本说明和 GPLv3 文本，并按 GPLv3 的要求提供对应 FFmpeg 源码的获取方式。

## .NET Framework

程序的目标框架是 .NET Framework 4.7.2。Windows 10/11 通常已包含运行时，本项目不捆绑 .NET Framework 安装包。

## 图标与素材

程序图标和界面文字由本项目自行维护；仓库中的测试截图来自本项目界面，不包含用户视频内容。
