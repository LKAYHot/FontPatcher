# FontPatcher

Universal font pipeline for Unity games:
`font file -> TMP Font Asset -> AssetBundle`.

The project includes:
- `FontPatcher.Cli` (automation and batch processing)
- `FontPatcher.Avalonia` (desktop UI that launches the same CLI)

---

## Русский

### 1) Что это
`FontPatcher` автоматически:
1. Подбирает/находит Unity Editor (или ставит его через Unity Hub, если разрешено).
2. Создает временный Unity-проект.
3. Импортирует шрифт (`.ttf/.otf/.ttc/.otc`).
4. Генерирует TMP Font Asset.
5. Собирает AssetBundle.

Итог: готовый bundle и `.manifest` в выходной папке.

### 2) Требования
- Windows 10/11 (в текущей реализации используются `Unity.exe` и `Unity Hub.exe`).
- .NET 8 SDK.
- Доступ в интернет, если включена автоустановка Unity/Hub.
- Лицензия Unity для соответствующей версии редактора.

Проверка окружения:
```powershell
dotnet --version
```

### 3) Установка и сборка
```powershell
git clone <your-repo-url>
cd FontPatcher
dotnet restore
dotnet build FontPatcher.sln -c Release
```

### 4) Быстрый запуск (CLI)
Одиночная конверсия:
```powershell
dotnet run --project .\FontPatcher.Cli\FontPatcher.Cli.csproj -- `
  --font "G:\TinyTools2\FontPatcher\Fonts\arialuni_sdf_u2019.ttf" `
  --output "G:\TinyTools2\FontPatcher\Fonts\Output" `
  --target-game "G:\SteamLibrary\steamapps\common\Heartworm\Heartworm.exe"
```

Пакетный режим (batch):
```powershell
dotnet run --project .\FontPatcher.Cli\FontPatcher.Cli.csproj -- `
  --jobs-file ".\sample.jobs.json" `
  --max-workers 2 `
  --continue-on-job-error
```

Справка CLI:
```powershell
dotnet run --project .\FontPatcher.Cli\FontPatcher.Cli.csproj -- --help
```

### 5) Запуск GUI (Avalonia)
```powershell
dotnet run --project .\FontPatcher.Avalonia\FontPatcher.Avalonia.csproj
```

Режимы в UI:
- `Single`: один шрифт.
- `Batch`: запуск по JSON-манифесту jobs.
- `Unity`: выбор/проверка Unity.
- `Advanced`: тонкие настройки генерации.

### 6) Полный список CLI параметров

| Параметр | По умолчанию | Описание |
|---|---:|---|
| `--font <path>` | - | Входной файл шрифта (`.ttf/.otf/.ttc/.otc`). Обязателен в single-режиме. |
| `--output <dir>` | - | Папка результата. Обязательна в single-режиме. |
| `--jobs-file <path>` | - | JSON с заданиями для batch-режима. |
| `--max-workers <int>` | `1` | Параллелизм в batch-режиме (>0). |
| `--continue-on-job-error` | `false` | Не останавливать batch после первой ошибки. |
| `--unity <path>` | auto | Явный путь к `Unity.exe`. |
| `--unity-hub <path>` | auto | Явный путь к `Unity Hub.exe`. |
| `--unity-version <version>` | auto | Целевая версия редактора, например `2021.3.38f1`. |
| `--target-game <path>` | - | `.exe`, `UnityPlayer.dll` или `*_Data` для автоопределения версии Unity игры. |
| `--unity-install-root <path>` | `%LOCALAPPDATA%\FontPatcher\UnityEditors` | Каталог установки/кеша Unity. |
| `--epoch <auto\|legacy\|mid\|modern>` | `auto` | Выбор адаптера эпохи Unity. |
| `--use-nographics` | adapter default | Принудительно использовать `-nographics`. |
| `--no-nographics` | adapter default | Принудительно выключить `-nographics`. |
| `--no-auto-install-unity` | `false` | Запретить автоустановку Unity Editor. |
| `--no-auto-install-hub` | `false` | Запретить автоустановку Unity Hub. |
| `--prefer-non-lts` | `false` | При авто-выборе предпочитать non-LTS. |
| `--bundle-name <name>` | `<fontname lower>` | Имя AssetBundle (санитизируется). |
| `--tmp-name <name>` | `TMP_<fontname>` | Имя TMP asset (санитизируется). |
| `--build-target <target>` | `StandaloneWindows64` | Unity `BuildTarget`. |
| `--atlas-sizes <csv>` | `1024,2048,4096` | Кандидаты размеров атласа (`256..8192`). |
| `--point-size <int>` | `90` | Размер семплинга шрифта (>0). |
| `--padding <int>` | `8` | Padding глифов (>=0). |
| `--scan-upper-bound <int>` | `1114111` | Верхняя граница скана Unicode (>=0). |
| `--force-static` | `false` | Принудительно статический режим. |
| `--force-dynamic` | `false` | Принудительно dynamic multi-atlas режим. |
| `--dynamic-warmup-limit <int>` | `20000` | Лимит pre-seed глифов в dynamic. |
| `--dynamic-warmup-batch <int>` | `1024` | Размер батча прогрева dynamic (>0). |
| `--include-control` | `false` | Включать control-символы `< U+0020`. |
| `--keep-temp` | `false` | Не удалять временный Unity worker проект. |
| `-h`, `--help` | - | Показать справку. |

Ограничение: `--force-static` и `--force-dynamic` взаимоисключающие.

### 7) Batch jobs JSON

#### Формат
Корень документа:
```json
{
  "jobs": [
    {
      "id": "job-1",
      "font": "G:/path/to/font.ttf",
      "output": "G:/path/to/output"
    }
  ]
}
```

#### Поля job
- `id`
- `font`
- `output`
- `unity`
- `unityVersion`
- `targetGame`
- `buildTarget`
- `bundleName`
- `tmpName`
- `epoch` (`auto|legacy|mid|modern`)
- `useNoGraphics`
- `pointSize`
- `padding`
- `scanUpperBound`
- `atlasSizes` (array of int)
- `includeControl`
- `keepTemp`
- `forceDynamic`
- `forceStatic`
- `dynamicWarmupLimit`
- `dynamicWarmupBatch`

#### Приоритет значений
В batch каждый job объединяется с базовыми CLI-опциями:
- если поле задано в job -> берется job-значение;
- иначе используется общий CLI параметр.

Обязательные данные после merge: `font` и `output`.

#### Пример полного jobs файла
```json
{
  "jobs": [
    {
      "id": "heartworm-main",
      "font": "G:/TinyTools2/FontPatcher/Fonts/arialuni_sdf_u2019.ttf",
      "output": "G:/TinyTools2/FontPatcher/Fonts/Output/heartworm",
      "targetGame": "G:/SteamLibrary/steamapps/common/Heartworm/Heartworm.exe",
      "buildTarget": "StandaloneWindows64",
      "epoch": "auto"
    },
    {
      "id": "legacy-2020-override",
      "font": "G:/TinyTools2/FontPatcher/Fonts/arialuni_sdf_u2019.ttf",
      "output": "G:/TinyTools2/FontPatcher/Fonts/Output/legacy",
      "unityVersion": "2020.3.49f1",
      "epoch": "legacy",
      "useNoGraphics": false,
      "pointSize": 90,
      "padding": 8
    }
  ]
}
```

### 8) Как выбирается Unity
Порядок разрешения редактора:
1. `--unity` (или `UNITY_EDITOR_PATH`).
2. Поиск установленной версии по `--unity-version`/`--target-game`.
3. Автоустановка через Unity Hub (если не отключена).

Для Hub:
- путь можно задать `--unity-hub` (или `UNITY_HUB_PATH`),
- при отсутствии может устанавливаться автоматически,
- install root настраивается через `--unity-install-root`.

### 9) Выходные артефакты и коды возврата
- Успех: bundle `<output>/<bundle-name>` и `<bundle-name>.manifest`.
- В single-режиме код выхода `0` при успехе, `1` при ошибке, `2` при ошибке парсинга аргументов.
- В batch код `0` только если все job успешны, иначе `1`.

### 10) Диагностика и типичные проблемы
- Ошибка `Exit code 199` от Unity: как правило, лицензирование.
  - Откройте нужную версию Unity один раз в интерактивном режиме,
  - завершите активацию,
  - повторите запуск.
- Если не находится Unity:
  - проверьте `--unity` или `--unity-version`,
  - либо включите автоустановку (не использовать `--no-auto-install-unity`).
- Для анализа пайплайна используйте `--keep-temp`.

### 11) Структура репозитория
```text
FontPatcher.Cli/
  Bootstrap/
  Cli/
  Batch/
  Pipeline/
  Common/
  Unity/
  BuilderScripts/
    Definitions/
    Sources/

FontPatcher.Avalonia/
  Views/
  ViewModels/
  Services/
```

---

## English

### 1) What It Does
`FontPatcher` automates:
1. Unity Editor resolution/provisioning.
2. Temporary Unity worker project creation.
3. Font import (`.ttf/.otf/.ttc/.otc`).
4. TMP Font Asset generation.
5. AssetBundle build.

Output: bundle and `.manifest` in your output directory.

### 2) Requirements
- Windows 10/11 (current implementation uses `Unity.exe` and `Unity Hub.exe`).
- .NET 8 SDK.
- Internet access if Unity/Hub auto-install is enabled.
- A valid Unity license for the selected editor version.

Environment check:
```powershell
dotnet --version
```

### 3) Install and Build
```powershell
git clone <your-repo-url>
cd FontPatcher
dotnet restore
dotnet build FontPatcher.sln -c Release
```

### 4) Quick Start (CLI)
Single conversion:
```powershell
dotnet run --project .\FontPatcher.Cli\FontPatcher.Cli.csproj -- `
  --font "G:\TinyTools2\FontPatcher\Fonts\arialuni_sdf_u2019.ttf" `
  --output "G:\TinyTools2\FontPatcher\Fonts\Output" `
  --target-game "G:\SteamLibrary\steamapps\common\Heartworm\Heartworm.exe"
```

Batch mode:
```powershell
dotnet run --project .\FontPatcher.Cli\FontPatcher.Cli.csproj -- `
  --jobs-file ".\sample.jobs.json" `
  --max-workers 2 `
  --continue-on-job-error
```

CLI help:
```powershell
dotnet run --project .\FontPatcher.Cli\FontPatcher.Cli.csproj -- --help
```

### 5) Run GUI (Avalonia)
```powershell
dotnet run --project .\FontPatcher.Avalonia\FontPatcher.Avalonia.csproj
```

UI modes:
- `Single`: one font conversion.
- `Batch`: job-manifest-based processing.
- `Unity`: Unity selection/checking.
- `Advanced`: generation tuning.

### 6) Full CLI Parameter Reference

| Option | Default | Description |
|---|---:|---|
| `--font <path>` | - | Input font (`.ttf/.otf/.ttc/.otc`). Required in single mode. |
| `--output <dir>` | - | Output directory. Required in single mode. |
| `--jobs-file <path>` | - | JSON job manifest for batch mode. |
| `--max-workers <int>` | `1` | Batch concurrency (>0). |
| `--continue-on-job-error` | `false` | Continue remaining jobs after a failure. |
| `--unity <path>` | auto | Explicit path to `Unity.exe`. |
| `--unity-hub <path>` | auto | Explicit path to `Unity Hub.exe`. |
| `--unity-version <version>` | auto | Target editor version (for example `2021.3.38f1`). |
| `--target-game <path>` | - | Game `.exe`, `UnityPlayer.dll`, or `*_Data` for version detection. |
| `--unity-install-root <path>` | `%LOCALAPPDATA%\FontPatcher\UnityEditors` | Unity install/cache root. |
| `--epoch <auto\|legacy\|mid\|modern>` | `auto` | Force/select epoch adapter. |
| `--use-nographics` | adapter default | Force `-nographics`. |
| `--no-nographics` | adapter default | Force disable `-nographics`. |
| `--no-auto-install-unity` | `false` | Disable Unity editor auto-install. |
| `--no-auto-install-hub` | `false` | Disable Unity Hub auto-install. |
| `--prefer-non-lts` | `false` | Prefer newest non-LTS when auto-selecting. |
| `--bundle-name <name>` | `<fontname lower>` | AssetBundle name (sanitized). |
| `--tmp-name <name>` | `TMP_<fontname>` | TMP asset name (sanitized). |
| `--build-target <target>` | `StandaloneWindows64` | Unity `BuildTarget`. |
| `--atlas-sizes <csv>` | `1024,2048,4096` | Atlas candidates (`256..8192`). |
| `--point-size <int>` | `90` | Font sampling size (>0). |
| `--padding <int>` | `8` | Glyph padding (>=0). |
| `--scan-upper-bound <int>` | `1114111` | Max Unicode code point scan (>=0). |
| `--force-static` | `false` | Force static mode. |
| `--force-dynamic` | `false` | Force dynamic multi-atlas mode. |
| `--dynamic-warmup-limit <int>` | `20000` | Dynamic pre-seed glyph limit. |
| `--dynamic-warmup-batch <int>` | `1024` | Dynamic warmup batch size (>0). |
| `--include-control` | `false` | Include control chars `< U+0020`. |
| `--keep-temp` | `false` | Keep temporary Unity worker project. |
| `-h`, `--help` | - | Show help. |

Constraint: use only one of `--force-static` or `--force-dynamic`.

### 7) Batch Jobs JSON

#### Format
Document root:
```json
{
  "jobs": [
    {
      "id": "job-1",
      "font": "G:/path/to/font.ttf",
      "output": "G:/path/to/output"
    }
  ]
}
```

#### Job fields
- `id`
- `font`
- `output`
- `unity`
- `unityVersion`
- `targetGame`
- `buildTarget`
- `bundleName`
- `tmpName`
- `epoch` (`auto|legacy|mid|modern`)
- `useNoGraphics`
- `pointSize`
- `padding`
- `scanUpperBound`
- `atlasSizes` (array of int)
- `includeControl`
- `keepTemp`
- `forceDynamic`
- `forceStatic`
- `dynamicWarmupLimit`
- `dynamicWarmupBatch`

#### Value precedence
Each job is merged with base CLI options:
- job field wins if provided,
- otherwise global CLI value is used.

Required after merge: `font` and `output`.

#### Full sample
```json
{
  "jobs": [
    {
      "id": "heartworm-main",
      "font": "G:/TinyTools2/FontPatcher/Fonts/arialuni_sdf_u2019.ttf",
      "output": "G:/TinyTools2/FontPatcher/Fonts/Output/heartworm",
      "targetGame": "G:/SteamLibrary/steamapps/common/Heartworm/Heartworm.exe",
      "buildTarget": "StandaloneWindows64",
      "epoch": "auto"
    },
    {
      "id": "legacy-2020-override",
      "font": "G:/TinyTools2/FontPatcher/Fonts/arialuni_sdf_u2019.ttf",
      "output": "G:/TinyTools2/FontPatcher/Fonts/Output/legacy",
      "unityVersion": "2020.3.49f1",
      "epoch": "legacy",
      "useNoGraphics": false,
      "pointSize": 90,
      "padding": 8
    }
  ]
}
```

### 8) Unity Resolution Logic
Editor resolution order:
1. `--unity` (or `UNITY_EDITOR_PATH`).
2. Installed editor search using `--unity-version`/`--target-game`.
3. Unity Hub auto-install flow (if enabled).

For Hub:
- set explicit path via `--unity-hub` (or `UNITY_HUB_PATH`),
- it can auto-install when missing,
- install root is controlled via `--unity-install-root`.

### 9) Outputs and Exit Codes
- Success output: `<output>/<bundle-name>` and `<bundle-name>.manifest`.
- Single mode exit codes: `0` success, `1` runtime failure, `2` CLI argument parse error.
- Batch mode: `0` only when all jobs succeed, otherwise `1`.

### 10) Troubleshooting
- Unity `Exit code 199`: usually licensing bootstrap.
  - Open that Unity editor version interactively once,
  - complete activation,
  - rerun FontPatcher.
- Unity not found:
  - verify `--unity` or `--unity-version`,
  - or allow auto-install (do not pass `--no-auto-install-unity`).
- Use `--keep-temp` for deep pipeline diagnostics.

### 11) Repository Layout
```text
FontPatcher.Cli/
  Bootstrap/
  Cli/
  Batch/
  Pipeline/
  Common/
  Unity/
  BuilderScripts/
    Definitions/
    Sources/

FontPatcher.Avalonia/
  Views/
  ViewModels/
  Services/
```
