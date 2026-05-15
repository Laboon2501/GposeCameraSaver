# Gpose Camera Saver / GPose 镜头保存器

Gpose Camera Saver is a Dalamud plugin for saving, loading, and managing GPose camera presets.

Gpose Camera Saver 是一款 Dalamud 插件，用于保存、加载与管理 GPose 镜头预设。

---

## Features / 功能

- Save and restore GPose camera presets.  
  保存并恢复 GPose 镜头预设。

- Create, overwrite, rename, and delete presets.  
  创建、覆盖、重命名和删除预设。

- Quickly switch presets in a compact GPose window.  
  在紧凑的 GPose 窗口中快速切换预设。

- Persist presets per character and keep metadata for quick filtering.  
  按角色保存预设，并保留元数据以便快速筛选。

---

## Installation / 安装

1. Open Dalamud Plugin Installer.  
   打开 Dalamud 插件安装器。

2. Open **Third-Party Plugin Repositories** settings.  
   打开 **第三方插件仓库** 设置。

3. Add the following repository URL:  
   添加以下插件仓库地址：

   ```text
   https://raw.githubusercontent.com/Laboon2501/GposeCameraSaver/main/repo.json
   ```

4. Update the plugin list and install **Gpose Camera Saver**.  
   更新插件列表，然后安装 **Gpose Camera Saver**。

---

## Custom Plugin Repository URL / 自定义插件仓库地址

```text
https://raw.githubusercontent.com/Laboon2501/GposeCameraSaver/main/repo.json
```

---

## Usage / 使用方法

1. Enter GPose.  
   进入 GPose。

2. Open the plugin with `/gcs` for the main window, or `/gcs settings` for the settings tab.  
   使用 `/gcs` 打开主窗口，或使用 `/gcs settings` 打开设置选项卡。

3. Save your current camera position as a preset.  
   将当前镜头位置保存为预设。

4. Select a preset to restore the camera state.  
   选择一个预设以恢复镜头状态。

5. Use rename and delete actions to keep your preset list organized.  
   使用重命名和删除功能整理你的预设列表。
   
7. Delete preset by right clicking "load".
   右键“加载”来删除预设。

---

## Building / 构建

1. Ensure that the .NET 10 SDK is installed.  
   确保已安装 .NET 10 SDK。

2. Restore dependencies and build the project:  
   还原依赖并构建项目：

   ```bash
   dotnet build -c Release
   ```

3. Re-package the artifact from `Plugin/bin/Release` for release distribution.  
   从 `Plugin/bin/Release` 重新打包构建产物，用于发布分发。

---

## Disclaimer / 免责声明

This plugin reads GPose data from memory and is intended only for local preset management. Do not use it for any behavior that violates the game’s Terms of Service.

此插件通过内存读取方式获取 GPose 数据，仅用于本地预设管理用途。请勿将其用于任何违反游戏服务条款的行为。

---

## License / 许可证

AGPL-3.0-only. See `LICENSE.md`.

本项目采用 AGPL-3.0-only 许可证。详情请参阅 `LICENSE.md`。
