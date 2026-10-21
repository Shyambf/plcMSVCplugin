using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace plcMSVCplugin
{
    [Guid("43b27b99-3543-4e6a-bdf0-4b52b36f78df")]
    public class PlanningCOptionsPage : DialogPage
    {
        private const string CategoryGeneral = "Транслятор и компилятор";
        private const string CategoryDebugger = "Отладчик и среда";

        [Category(CategoryGeneral)]
        [DisplayName("Путь к транслятору (Reenterable.exe)")]
        [Description("Полный путь к Reenterable.exe. Если поле пустое, плагин попытается найти транслятор автоматически в папке решения или соседней папке Planning_C_Translator.")]
        [Editor(typeof(System.Windows.Forms.Design.FileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string TranslatorPath { get; set; } = "";

        [Category(CategoryGeneral)]
        [DisplayName("Каталоги заголовков (Include Directories)")]
        [Description("Каталог(и) заголовочных файлов Planning C (reentera.h, memoization.h и т.д.). Если пусто, определяется автоматически по расположению транслятора.")]
        [Editor(typeof(System.Windows.Forms.Design.FolderNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string IncludeDirectories { get; set; } = "";

        [Category(CategoryGeneral)]
        [DisplayName("Путь к vcvars64.bat или cl.exe")]
        [Description("Путь к скрипту инициализации среды компилятора MSVC (vcvars64.bat) или самому cl.exe. Если пусто, определяется через Visual Studio Setup API.")]
        [Editor(typeof(System.Windows.Forms.Design.FileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string CompilerPath { get; set; } = "";

        [Category(CategoryGeneral)]
        [DisplayName("Флаги компилятора C++")]
        [Description("Параметры компилятора MSVC cl.exe для сборки отладочной версии.")]
        public string CompilerFlags { get; set; } = "/EHsc /openmp /std:c++17 /Zi /Od /MDd /F8388608";

        [Category(CategoryGeneral)]
        [DisplayName("Таймаут транслятора (сек)")]
        [Description("Максимальное время ожидания завершения Reenterable.exe в секундах (по умолчанию 15 сек).")]
        [DefaultValue(15)]
        public int TranslatorTimeoutSeconds { get; set; } = 15;

        [Category(CategoryGeneral)]
        [DisplayName("Таймаут компилятора C++ (сек)")]
        [Description("Максимальное время ожидания завершения MSVC cl.exe в секундах (по умолчанию 30 сек).")]
        [DefaultValue(30)]
        public int CompilerTimeoutSeconds { get; set; } = 30;

        [Category(CategoryDebugger)]
        [DisplayName("Подпапка для файлов сборки")]
        [Description("Имя подкаталога рядом с исходным файлом для сохранения промежуточного C++ кода, .exe и .pdb.")]
        public string BuildSubfolder { get; set; } = ".plc_build";

        [Category(CategoryDebugger)]
        [DisplayName("Автосохранение перед запуском")]
        [Description("Автоматически сохранять текущий редактируемый документ перед вызовом транслятора и отладчика.")]
        public bool AutoSaveBeforeDebug { get; set; } = true;

        [Category(CategoryDebugger)]
        [DisplayName("Открывать инспектор плана при останове")]
        [Description("Автоматически активировать окно «Инспектор плана и потоков Planning C» при остановке программы на точке останова (Break Mode).")]
        public bool AutoOpenInspectorOnBreak { get; set; } = true;

        /// <summary>
        /// Разрешает эффективный путь к Reenterable.exe
        /// </summary>
        public string GetEffectiveTranslatorPath(string? currentDocPath)
        {
            if (!string.IsNullOrWhiteSpace(TranslatorPath) && File.Exists(TranslatorPath))
            {
                return TranslatorPath;
            }

            // Пробуем искать относительно открытого документа
            if (!string.IsNullOrEmpty(currentDocPath))
            {
                string dir = Path.GetDirectoryName(currentDocPath);
                while (!string.IsNullOrEmpty(dir))
                {
                    string candidate = Path.Combine(dir, "Planning_C_Translator", "Reenterable.exe");
                    if (File.Exists(candidate)) return candidate;

                    string candidateDirect = Path.Combine(dir, "Reenterable.exe");
                    if (File.Exists(candidateDirect)) return candidateDirect;

                    DirectoryInfo? parent = Directory.GetParent(dir);
                    if (parent == null || parent.FullName == dir) break;
                    dir = parent.FullName;
                }
            }

            // Стандартное расположение в рабочей папке пользователя
            string userDocsCandidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "plagin", "Planning_C_Translator", "Reenterable.exe");
            if (File.Exists(userDocsCandidate)) return userDocsCandidate;

            return "Reenterable.exe";
        }

        /// <summary>
        /// Разрешает каталог заголовочных файлов Planning C
        /// </summary>
        public string GetEffectiveIncludeDirectory(string? currentDocPath)
        {
            if (!string.IsNullOrWhiteSpace(IncludeDirectories) && Directory.Exists(IncludeDirectories))
            {
                return IncludeDirectories;
            }

            string translatorPath = GetEffectiveTranslatorPath(currentDocPath);
            if (File.Exists(translatorPath))
            {
                string transDir = Path.GetDirectoryName(translatorPath);
                if (File.Exists(Path.Combine(transDir, "reentera.h")))
                {
                    return transDir;
                }
            }

            if (!string.IsNullOrEmpty(currentDocPath))
            {
                string dir = Path.GetDirectoryName(currentDocPath);
                while (!string.IsNullOrEmpty(dir))
                {
                    string cand = Path.Combine(dir, "Planning_C_Translator");
                    if (Directory.Exists(cand) && File.Exists(Path.Combine(cand, "reentera.h")))
                        return cand;

                    DirectoryInfo? parent = Directory.GetParent(dir);
                    if (parent == null || parent.FullName == dir) break;
                    dir = parent.FullName;
                }
            }

            return "";
        }

        /// <summary>
        /// Разрешает путь к vcvars64.bat
        /// </summary>
        public string GetEffectiveVcVarsBat()
        {
            if (!string.IsNullOrWhiteSpace(CompilerPath) && File.Exists(CompilerPath))
            {
                return CompilerPath;
            }

            // Проверяем известные пути Visual Studio
            string[] knownPaths = new[]
            {
                @"C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat",
                @"C:\Program Files\Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
                @"C:\Program Files\Microsoft Visual Studio\18\Professional\VC\Auxiliary\Build\vcvars64.bat",
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat",
                @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat",
                @"C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat"
            };

            foreach (var p in knownPaths)
            {
                if (File.Exists(p)) return p;
            }

            return "";
        }
    }
}
