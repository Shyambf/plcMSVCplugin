# Подсистема сборки, трансляции и отладки (Build)

В этой папке находятся компоненты, отвечающие за сборку файлов Planning C, коррекцию отладочной информации и подключение нативного отладчика Visual Studio.

> Полное руководство и спецификация подсистемы сборки: [Build.md](Build.md).

---

## Общий конвейер сборки (Pipeline)

```text
[ Исходный код Planning C (.cpp) ]
              │
              ▼
   [ 1/3 Трансляция: Reenterable.exe ]
              │  Генерирует сырой C++ файл: `_out.cpp`
              ▼
   [ 2/3 Постобработка: LineDirectivesPostprocessor ]
              │  Корректирует директивы #line, изолирует служебные функции
              ▼
   [ 2/3 Компиляция: MSVC cl.exe ]
              │  Компилирует C++ в бинарный .exe и базу символов .pdb
              ▼
   [ 3/3 Запуск отладчика: VsShellUtilities.LaunchDebugger ]
```

---

## Состав файлов

### 1. `PlanningCDebuggerLauncher.cs`
Главный оркестратор жизненного цикла сборки и отладки.

* **Интеграция с UI:**
  * Создает и активирует отдельную вкладку **Planning C** в окне «Вывод» (`IVsOutputWindowPane`).
  * Отображает статусную анимацию и прогресс-бар в строке состояния Visual Studio (`IVsStatusbar`).
  * При ошибках сборки подсвечивает индикатор в окне Инспектора плана.
* **Метод `BuildAsync`:**
  * Определяет активный файл (`DTE.ActiveDocument` или выделенный элемент в Solution Explorer).
  * Создает изолированную подпапку `.plc_build` в каталоге с исходным файлом.
  * Формирует пути к артефактам (`_out.cpp`, `.exe`, `.pdb`).
  * Запускает транслятор `Reenterable.exe` в фоновом режиме с контролем таймаута (`ProcessStartInfo`).
  * Вызывает `LineDirectivesPostprocessor.ProcessFile(...)`.
  * Собирает строку параметров компилятора (`/EHsc /openmp /std:c++17 /Zi /Od /MDd /F8388608`) и запускает компилятор `cl.exe` в среде `vcvarsall.bat`.
* **Метод `LaunchDebugAsync`:**
  * Заполняет структуру `VsDebugTargetInfo`:
    ```csharp
    var debugTarget = new VsDebugTargetInfo
    {
        cbSize = (uint)Marshal.SizeOf(typeof(VsDebugTargetInfo)),
        dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
        bstrExe = targetExe,
        bstrCurDir = sourceDir,
        clsidCustom = VSConstants.DebugEnginesGuids.NativeOnly_guid,
        grfLaunch = (uint)__VSDBGLAUNCHFLAGS.DBGLAUNCH_StopDebuggingOnEnd
    };
    ```
  * Вызывает `VsShellUtilities.LaunchDebugger(_package, debugTarget)`, запуская программу под управлением нативного движка Visual Studio.
* **Метод `LaunchRunAsync`:**
  * Запускает скомпилированный `.exe` в интерактивном окне `cmd.exe` с паузой по завершении.
* **Метод `CleanBuildFilesAsync`:**
  * Удаляет все сгенерированные файлы из `.plc_build`.

---

### 2. `LineDirectivesPostprocessor.cs`
Специализированный модуль нормализации отладочных привязок `#line`.

#### Зачем нужна постобработка:
Транслятор `Reenterable.exe` при генерации C++ кода вставляет служебные директивы `#line`, которые при стандартной компиляции приводят к сбоям отладчика:
1. Ссылки на виртуальные буферы препроцессора (например `#line 1 "PATTERN[while_else]"`), из-за которых Visual Studio пытается открыть несуществующий файл на диске.
2. 14 лямбда-инициализаций в прологе `_call_` функций, которые наследуют номер строки заголовка процедуры.
3. «Проваливание» адреса возврата из `_call_` процедур в следующие по коду служебные процедуры (`__soft_atomic_`, `__atomic_`, `SumF`, `RecursivePutTree`).

#### Что делает алгоритм:
* **`ReenterableHeaderRegex` и `procStartLineMap`:**  
  Сканирует исходный `.cpp` файл и формирует карту объявлений процедур (`PutTree` -> строка 439).
* **Привязка диспетчера:**  
  Функции `inline void __PutTree__` присваивается `#line` заголовка процедуры.
* **Фильтрация макросов:**  
  Исключаются директивы, содержащие скобки или имена паттернов (`PATTERN[`, `while_else(`).
* **Смещение пролога:**  
  После оператора `__dummy_counter += 713;` выставляется директива на первый исполняемый оператор тела процедуры.
* **Изоляция выхода (`planning_c_internal.h`):**  
  Через подсчет глубины фигурных скобок (`callBraceDepth`) на закрывающей скобке тела процедуры `_call_` и перед всеми вспомогательными процедурами `__soft_atomic_` генерируется `#line 1 "planning_c_internal.h"`. Это изолирует стек вызовов и полностью предотвращает ложные переходы отладчика в соседние функции.

