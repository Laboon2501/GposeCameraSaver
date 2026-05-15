# Gpose Camera Saver

Gpose Camera Saver 是一款 Dalamud 插件，用于保存、加载与管理 GPose 镜头预设。

## Features

- Save and restore GPose camera presets.
- Create, overwrite, rename, and delete presets.
- Quickly switch presets in a compact GPose window.
- Persist presets per-character and keep metadata for quick filtering.

## Installation

1. Open Dalamud Plugin Installer.
2. Open **Third-Party Plugin Repositories** settings.
3. Add the following repository URL:
   - `https://raw.githubusercontent.com/Laboon2501/GposeCameraSaver/main/repo.json`
4. Update plugin list and install **Gpose Camera Saver**.

## Custom Plugin Repository URL

`https://raw.githubusercontent.com/Laboon2501/GposeCameraSaver/main/repo.json`

## Usage

1. Enter GPose.
2. Open the plugin via `/gcs` (main window) or `/gcs settings` (settings tab).
3. Save your current camera position to a preset.
4. Select a preset to restore camera state.
5. Use rename/delete actions to keep your preset list organized.

## Building

1. Ensure .NET 10 SDK is installed.
2. Restore and build:

```bash
dotnet build -c Release
```

3. Re-package artifact from `Plugin/bin/Release` for release distribution.

## Disclaimer

此插件通过内存读取方式获取 GPose 数据，仅用于单机设置管理用途；请勿用于任何违反服务条款的行为。

## License

AGPL-3.0-only. See `LICENSE.md`.

