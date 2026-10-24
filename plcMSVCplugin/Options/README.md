# Подсистема параметров и настроек (Options)

В этой папке находится реализация страницы глобальных настроек плагина в окне **«Сервис -> Параметры -> Planning C»** Visual Studio.

> Полное руководство и спецификация параметров: [Options.md](Options.md).

---

## Архитектура страницы настроек

* **Класс:** `PlanningCOptionsPage.cs`
* **Базовый класс:** `Microsoft.VisualStudio.Shell.DialogPage`
* **Регистрация в пакете (`plcMSVCpluginPackage.cs`):**
  ```csharp
  [ProvideOptionPage(typeof(PlanningCOptionsPage), "Planning C", "Общие", 0, 0, true)]
  ```
  Благодаря этому атрибуту Visual Studio автоматически встраивает страницу настроек в общее дерево диалога `Сервис -> Параметры` и берет на себя сохранение/загрузку значений в системный реестр Visual Studio.

---

## Доступные параметры

| Категория | Свойство | Значение по умолчанию | Описание |
| :--- | :--- | :--- | :--- |
| **Пути** | `TranslatorExecutablePath` | Поиск рядом со сплитом или в PATH | Полный путь к исполняемому файлу транслятора `Reenterable.exe`. |
| **Пути** | `IncludeDirectoryPath` | Каталог с заголовочными файлами | Путь к папке с заголовками Planning C (`reentera.h`, `stdtopo.h`...). |
| **Пути** | `VcVarsBatPath` | Автопоиск `vcvarsall.bat` | Путь к командному файлу инициализации среды MSVC. |
| **Компиляция** | `CompilerFlags` | `/EHsc /openmp /std:c++17 /Zi /Od /MDd /F8388608` | Ключи компилятора `cl.exe`. |
| **Компиляция** | `BuildSubfolder` | `.plc_build` | Имя подпапки для промежуточных файлов сборки. |
| **Общие** | `TranslatorTimeoutSeconds` | `15` | Максимальное время ожидания трансляции в секундах. |

---

## Автоматическое определение путей по умолчанию

Класс `PlanningCOptionsPage` содержит логику автоматического поиска инструментов (Auto-discovery):
1. **Поиск `vcvarsall.bat`:**  
   Сканирует стандартные каталоги Visual Studio 2022 / 2026 (`C:\Program Files\Microsoft Visual Studio\...`) и переменные окружения `VSINSTALLDIR` / `VCINSTALLDIR`.
2. **Поиск `Reenterable.exe`:**  
   Проверяет пути относительно текущего решения (`.sln`), родительские папки репозитория (`Planning_C_Translator`), стандартный PATH и заданные вручную значения.
3. **Методы разрешения путей:**  
   Методы `GetEffectiveTranslatorPath(sourceFile)` и `GetEffectiveIncludeDirectory(sourceFile)` позволяют переопределять пути индивидуально под открытый файл проекта.

