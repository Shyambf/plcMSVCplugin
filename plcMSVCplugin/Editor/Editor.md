# Подсистема редактора: Подсветка синтаксиса и подсказки (Editor)

Эта папка содержит компоненты расширения текстового редактора Visual Studio для языка Planning C 2.0. Архитектура построена на базе интерфейсов **MEF (Managed Extensibility Framework)**.

---

## Архитектура редактора Visual Studio

Visual Studio Core Editor использует конвейерную модель классификации текста:
1. `ITextBuffer` — символьный буфер открытого документа.
2. `IClassifierProvider` — фабрика, которая при открытии файла C++ создает экземпляр классификатора.
3. `IClassifier` — сервис разметки диапазонов текста (`ClassificationSpan`).
4. `IAsyncQuickInfoSourceProvider` — поставщик всплывающих подсказок при наведении курсора на ключевые слова.

---

## Состав файлов

### 1. `PlanningCClassifier.cs`
Основной классификатор синтаксиса.
* **Экспорт MEF:**
  ```csharp
  [Export(typeof(IClassifierProvider))]
  [ContentType("C/C++")]
  [Order(After = Priority.High)]
  ```
  * Атрибут `Order(After = Priority.High)` гарантирует, что наш классификатор вызывается **после** стандартного анализатора C++ (IntelliSense). Это предотвращает стирание цветов кастомных токенов (`reenterable`, `plan_first`).
* **Модель правил `SyntaxRulesConfig`:**
  Считывает структуру `syntax_rules.json` (списки ключевых слов, типов, директив, ключевых слов Prolog и русского языка описания программ).
* **Загрузчик `SyntaxRulesLoader`:**
  Использует трехуровневую стратегию поиска правил:
  1. Файл `syntax_rules.json` рядом с DLL расширения на диске.
  2. Вшитый ресурс `EmbeddedResource` внутри сборки.
  3. Аварийный минимальный словарь в памяти (fallback).
* **Регулярное выражение `MasterTokenRegex`:**
  Все ключевые слова собираются в один объединенный Regex с именованными группами захвата (`(?<keyword>...)`, `(?<type>...)`, `(?<ru_keyword>...)`). Он компилируется в бинарный код процессора с флагом `RegexOptions.Compiled`.
* **Метод `GetClassificationSpans`:**
  Принимает видимый отрезок текста экрана `SnapshotSpan` и нарезает его на цветные спаны со стандартными системными типами Visual Studio (`Keyword`, `Type`, `Operator`, `Preprocessor`).

### 2. `PlanningCQuickInfoSource.cs`
Подсистема всплывающих подсказок (QuickInfo / Tooltips).
* При наведении курсора мыши на токен Planning C (например, `plan_group_parallelize` или `reenterable`) находит границы слова под курсором через `ITextStructureNavigator`.
* Сопоставляет слово с внутренней базой документации Planning C 2.0.
* Возвращает форматированный блок текста с описанием синтаксиса и параметров процедуры.

### 3. `syntax_rules.json`
Внешний словарь языка Planning C 2.0 в формате JSON.
Содержит 6 категорий:
1. `keywords` — операторы планирования (`reenterable`, `chain`, `plan_first`, `plan_last`, `_Yield`...).
2. `types` — каналы и структуры (`funnel`, `cvar`, `csem`, `topology`, `hypercube`...).
3. `preprocessor` — директивы метапрограммирования (`#def_pattern`, `#def_module`, `#introduce`...).
4. `prologKeywords` — предикаты логического вывода (`@goal`, `xpathf`, `assertz`, `repeat`...).
5. `russianTypes` — существительные РЯП (`вектор`, `скаляр`, `матрица` во всех падежах).
6. `russianKeywords` — действия и глаголы РЯП (`Составить`, `Ввести`, `Найдем`, `поместим`...).

