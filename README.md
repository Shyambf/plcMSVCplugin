# Visual Studio Extension: Planning C 2.0 Language Support

[![Build and Release VSIX](https://github.com/Shyambf/plcMSVCplugin/actions/workflows/build-release.yml/badge.svg)](https://github.com/Shyambf/plcMSVCplugin/actions/workflows/build-release.yml)
[![Visual Studio](https://img.shields.io/badge/Visual%20Studio-2022%20%7C%202026-blue.svg)](https://visualstudio.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Расширение для Microsoft Visual Studio 2022 и Visual Studio 2026, реализующее полноценную подсветку синтаксиса и языковую поддержку для языка параллельного конвейерного программирования и автоматического планирования **Planning C 2.0**.

---

## Возможности

- **Полная поддержка синтаксиса Planning C 2.0**:
  - **Конвейеры и реентрабельность**: `reenterable`, `chain`, `throw`, `throw_stage`, `throw_first`, `throw_last`, `_Yield`, `global`, `local`;
  - **Функции планирования**: `plan_first`, `plan_last`, `plan_get_first`, `plan_empty`, `plan_after_continue`, `plan_group_*`, `reent_*`;
  - **Каналы данных и очереди**: `funnel`, `cfunnel`, `cvar`, `csem`, `input_proc`;
  - **Виртуальные топологии**: `hypercube`, `torus`, `grid`, `ring`, `clique`, `tree`, `sun`;
  - **Встроенный Prolog и предикаты**: правила `@goal`, `@rule`, оператор `:-`, предикаты `xpathf`, `similar`, `gid`, `brackets_off`;
  - **Естественно-языковое метапрограммирование (РЯП / ЯОП)**: предложения `#(Составить программу.)`, склонения существительных-типов (`вектор`, `матрица`, `скаляр`, `элемент`) и глаголов-действий (`ввести`, `найдем`, `поместим`, `вывести`);
  - **Директивы мета-транслятора**: `#def_pattern`, `#def_module`, `#def_xpath`, `#introduce`, `#stop`, `#scan`;
  - **Параллелизм в стандарте Cilk++**: `cilk_spawn`, `cilk_sync`, `cilk_for`.
- **Конфигурация токенов через JSON ([`syntax_rules.json`](plcMSVCplugin/syntax_rules.json))**:
  - Все ключевые слова, типы, каналы, препроцессор и термины РЯП вынесены в удобный JSON-файл;
  - Добавление новых токенов не требует правок C#-кода и регулярных выражений.
- **Нативная работа со стандартными файлами .cpp (ISO C99/C11 / C++)**:
  - Вся разработка и примеры языка ведутся непосредственно в стандартных файлах `.cpp` (а также `.h`);
  - Плагин автоматически подсвечивает синтаксис Planning C в любых `.cpp` файлах без необходимости переименования или создания отдельных расширений.
- **Нативный MEF-классификатор**:
  - Высокая скорость работы без задержек (`RegexOptions.Compiled`);
  - Прямое подключение к стандартному редактору кода Visual Studio (`Source Code Editor`) без конфликтов TextMate COM.

---

## Установка

1. Скачайте файл **`PlanningC.vsix`** из раздела [Releases](https://github.com/Shyambf/plcMSVCplugin/releases) (или соберите его локально из исходников).
2. Закройте Visual Studio и запустите файл `PlanningC.vsix` двойным кликом.
3. В окне `VSIX Installer` нажмите **Install**.
4. После завершения откройте Visual Studio и откройте любой `.cpp` файл (например, тестовые примеры `test_samples/test_syntax.cpp` или `test_samples/reent.cpp`).

Подробная инструкция: [INSTALL.md](INSTALL.md).

> [!TIP]
> **Рекомендация:** Чтобы встроенный C++ семантический анализатор IntelliSense не перекрашивал специфичные конструкции языка Planning C (`reenterable`, `chain` и т.д.) в стандартный цвет текста, рекомендуется отключить параметр: *Сервис -> Параметры -> Текстовый редактор -> C/C++ -> Дополнительно -> Цветовая схема IntelliSense -> «Окрашивать токены на основании IntelliSense» = False*. Подробнее в [INSTALL.md](INSTALL.md).

---

## Сборка из исходного кода

Для самостоятельной сборки потребуется .NET SDK (рекомендуется .NET 8 SDK или выше):

```powershell
# Вариант 1: Запуск встроенного PowerShell скрипта
.\build_vsix.ps1

# Вариант 2: Сборка через .NET CLI
dotnet build plcMSVCplugin/plcMSVCplugin.csproj -c Release
```

Скомпилированный файл расширения сохраняется в:
`plcMSVCplugin/bin/Release/net472/PlanningC.vsix` и копируется в корень репозитория под именем `PlanningC.vsix`.

---

## Разработчикам: Доработка синтаксиса языка

Если вы дорабатываете язык Planning C и вам требуется добавить новые ключевые слова, типы, мета-директивы или операторы:
- Все токены редактируются в файле конфигурации [`plcMSVCplugin/syntax_rules.json`](plcMSVCplugin/syntax_rules.json);
- Править C#-код не требуется;
- Подробные инструкции и описание разделов приведены в [Руководстве разработчика (MANUAL.md)](MANUAL.md).

---

## Структура репозитория

```
plcMSVCplugin/
├── .github/
│   └── workflows/
│       └── build-release.yml         # GitHub Actions: автоматическая сборка VSIX и создание релиза по тегу v*
├── .gitignore                        # Исключения временных файлов и сборки
├── plcMSVCplugin/
│   ├── test_samples/
│   │   ├── test_syntax.cpp           # Тестовый файл со всеми конструкциями языка (.cpp)
│   │   └── reent.cpp                 # Реальный проектный пример реентрабельных процедур и топологий
│   ├── syntax_rules.json             # Конфигурация синтаксиса: ключевые слова, типы, препроцессор, Пролог и РЯП
│   ├── PlanningCClassifier.cs        # MEF-классификатор (динамическая загрузка правил из JSON, построение Regex)
│   ├── plcMSVCpluginPackage.cs       # AsyncPackage для интеграции в Visual Studio
│   ├── plcMSVCplugin.csproj          # Файл проекта C# (SDK-style)
│   ├── source.extension.vsixmanifest # Манифест VSIX расширения
│   └── build_vsix.ps1                # Скрипт быстрой сборки без внешних зависимостей
├── plcMSVCplugin.slnx                # Решение Visual Studio
├── README.md                         # Документация проекта
├── INSTALL.md                        # Инструкция по установке и настройке IntelliSense
└── MANUAL.md                         # Руководство по добавлению ключевых слов через JSON
```
