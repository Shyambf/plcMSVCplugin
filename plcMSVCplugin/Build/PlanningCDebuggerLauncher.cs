using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace plcMSVCplugin
{
    /// <summary>
    /// Координатор сборки, запуска отладки, запуска без отладки и очистки для программ Planning C 2.0.
    /// </summary>
    internal class PlanningCDebuggerLauncher
    {
        private readonly AsyncPackage _package;
        private readonly DTE2 _dte;
        private IVsOutputWindowPane? _outputPane;
        private IVsStatusbar? _statusBar;
        private uint _statusBarCookie = 0;
        private object _buildIcon = (short)0; // 0 = SBAI_Build in VS Constants

        public PlanningCDebuggerLauncher(AsyncPackage package, DTE2 dte)
        {
            _package = package;
            _dte = dte;
        }

        private async Task<IVsStatusbar?> GetStatusBarAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_statusBar == null)
            {
                _statusBar = await _package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
            }
            return _statusBar;
        }

        private async Task<IVsOutputWindowPane?> GetOutputPaneAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_outputPane != null) return _outputPane;

            if (await _package.GetServiceAsync(typeof(SVsOutputWindow)) is IVsOutputWindow outWindow)
            {
                Guid paneGuid = new Guid("4971c6aa-4b89-4a94-9122-861a7a20c384");
                outWindow.GetPane(ref paneGuid, out _outputPane);
                if (_outputPane == null)
                {
                    outWindow.CreatePane(ref paneGuid, "Planning C", 1, 1);
                    outWindow.GetPane(ref paneGuid, out _outputPane);
                }
            }
            return _outputPane;
        }

        public async Task ShowOutputWindowAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                Window? outputWindow = _dte.Windows.Item(EnvDTE.Constants.vsWindowKindOutput);
                if (outputWindow != null)
                {
                    outputWindow.Visible = true;
                    outputWindow.Activate();
                }
            }
            catch { }

            var pane = await GetOutputPaneAsync();
            pane?.Activate();
        }

        public async Task SetProgressAsync(string message, uint step, uint totalSteps, bool inProgress)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var sb = await GetStatusBarAsync();
                if (sb != null)
                {
                    sb.SetText(message);
                    if (inProgress)
                    {
                        sb.Animation(1, ref _buildIcon);
                        sb.Progress(ref _statusBarCookie, 1, message, step, totalSteps);
                    }
                    else
                    {
                        sb.Animation(0, ref _buildIcon);
                        sb.Progress(ref _statusBarCookie, 0, "", 0, 0);
                    }
                }
            }
            catch { }

            try
            {
                System.Windows.Media.Color color = inProgress
                    ? System.Windows.Media.Color.FromRgb(255, 165, 0)
                    : System.Windows.Media.Color.FromRgb(40, 167, 69);
                PlanInspectorToolWindow.Instance?.Control?.SetBuildStatus(message, color);
            }
            catch { }
        }

        public async Task SetBuildErrorStatusAsync(string message)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var sb = await GetStatusBarAsync();
                if (sb != null)
                {
                    sb.SetText(message);
                    sb.Animation(0, ref _buildIcon);
                    sb.Progress(ref _statusBarCookie, 0, "", 0, 0);
                }
            }
            catch { }

            try
            {
                PlanInspectorToolWindow.Instance?.Control?.SetBuildStatus(message, System.Windows.Media.Color.FromRgb(220, 53, 69));
            }
            catch { }
        }

        public async Task LogAsync(string message)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var pane = await GetOutputPaneAsync();
            if (pane != null)
            {
                pane.OutputStringThreadSafe(message + Environment.NewLine);
                pane.Activate();
            }
        }

        /// <summary>
        /// Универсальный метод трансляции и компиляции активного файла Planning C.
        /// </summary>
        private async Task<(bool success, string targetExe, string buildDir, string sourceDir)> BuildAsync(string actionTitle)
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            Document? activeDoc = _dte.ActiveDocument ?? _dte.ActiveWindow?.Document;
            if (activeDoc == null || string.IsNullOrEmpty(activeDoc.FullName))
            {
                if (_dte.Documents != null)
                {
                    foreach (Document doc in _dte.Documents)
                    {
                        if (doc != null && !string.IsNullOrEmpty(doc.FullName))
                        {
                            string e = Path.GetExtension(doc.FullName).ToLowerInvariant();
                            if (e == ".cpp" || e == ".c" || e == ".plc")
                            {
                                activeDoc = doc;
                                break;
                            }
                        }
                    }
                }
            }

            if ((activeDoc == null || string.IsNullOrEmpty(activeDoc.FullName)) && _dte.SelectedItems != null)
            {
                try
                {
                    foreach (SelectedItem item in _dte.SelectedItems)
                    {
                        if (item.ProjectItem != null && item.ProjectItem.FileNames[1] != null)
                        {
                            string p = item.ProjectItem.FileNames[1];
                            string e = Path.GetExtension(p).ToLowerInvariant();
                            if (e == ".cpp" || e == ".c" || e == ".plc")
                            {
                                activeDoc = _dte.ItemOperations?.OpenFile(p)?.Document ?? _dte.Documents?.Open(p);
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            if (activeDoc == null || string.IsNullOrEmpty(activeDoc.FullName))
            {
                VsShellUtilities.ShowMessageBox(
                    _package,
                    "Нет активного открытого файла с кодом Planning C (.cpp / .c / .plc).\nПожалуйста, откройте файл с исходным кодом в редакторе.",
                    actionTitle,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return (false, "", "", "");
            }

            string sourceFile = activeDoc.FullName;
            string ext = Path.GetExtension(sourceFile).ToLowerInvariant();
            if (ext != ".cpp" && ext != ".c" && ext != ".plc" && ext != ".h")
            {
                VsShellUtilities.ShowMessageBox(
                    _package,
                    $"Текущий файл '{Path.GetFileName(sourceFile)}' не является файлом исходного кода C/C++/Planning C.",
                    actionTitle,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return (false, "", "", "");
            }

            // Открываем окно вывода "Planning C", чтобы пользователь сразу видел прогресс в реальном времени
            await ShowOutputWindowAsync();
            await SetProgressAsync($"Planning C: Подготовка к сборке {Path.GetFileName(sourceFile)}...", 0, 3, true);

            var options = (PlanningCOptionsPage)_package.GetDialogPage(typeof(PlanningCOptionsPage));

            if (options.AutoSaveBeforeDebug && !activeDoc.Saved)
            {
                activeDoc.Save();
            }

            string sourceDir = Path.GetDirectoryName(sourceFile)!;
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);
            string subFolder = string.IsNullOrWhiteSpace(options.BuildSubfolder) ? ".plc_build" : options.BuildSubfolder.Trim();
            string buildDir = Path.Combine(sourceDir, subFolder);

            if (!Directory.Exists(buildDir))
            {
                Directory.CreateDirectory(buildDir);
            }

            string translatedCpp = Path.Combine(buildDir, fileNameWithoutExt + "_out.cpp");
            string targetExe = Path.Combine(buildDir, fileNameWithoutExt + ".exe");
            string targetPdb = Path.Combine(buildDir, fileNameWithoutExt + ".pdb");

            string translatorExe = options.GetEffectiveTranslatorPath(sourceFile);
            if (!File.Exists(translatorExe))
            {
                await SetBuildErrorStatusAsync("[Planning C] Ошибка: Reenterable.exe не найден");
                await LogAsync($"[ОШИБКА] Транслятор Reenterable.exe не найден по пути: '{translatorExe}'. Укажите корректный путь в 'Сервис -> Параметры -> Planning C'.");
                VsShellUtilities.ShowMessageBox(
                    _package,
                    $"Транслятор Reenterable.exe не найден!\nПроверьте путь в Сервис -> Параметры -> Planning C.",
                    actionTitle,
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                return (false, "", "", "");
            }

            string translatorDir = Path.GetDirectoryName(translatorExe)!;
            string includeDir = options.GetEffectiveIncludeDirectory(sourceFile);
            string vcvarsBat = options.GetEffectiveVcVarsBat();

            await LogAsync("================================================================================");
            await LogAsync($"[Planning C] {actionTitle}: {Path.GetFileName(sourceFile)}");
            await LogAsync($"[1/3] Трансляция через {Path.GetFileName(translatorExe)}...");
            await SetProgressAsync($"Planning C: [1/3] Трансляция {Path.GetFileName(sourceFile)}...", 1, 3, true);

            // 1. Запуск транслятора Planning C (Reenterable.exe)
            var (transSuccess, transOut, transErr) = await Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = translatorExe,
                        Arguments = $"\"{sourceFile}\" \"{translatedCpp}\"",
                        WorkingDirectory = translatorDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc == null) return (false, "", "Не удалось запустить процесс транслятора");

                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();

                    int transTimeoutMs = Math.Max(1, options.TranslatorTimeoutSeconds) * 1000;
                    bool finished = proc.WaitForExit(transTimeoutMs);
                    if (!finished)
                    {
                        try { proc.Kill(); } catch { }
                        return (false, "", $"Превышено время ожидания трансляции Reenterable.exe (таймаут {options.TranslatorTimeoutSeconds} сек). Процесс остановлен.\nИзменить таймаут можно в 'Сервис -> Параметры -> Planning C'.");
                    }

                    string stdOut = outTask.Result;
                    string stdErr = errTask.Result;

                    bool ok = proc.ExitCode == 0 && File.Exists(translatedCpp);
                    return (ok, stdOut, stdErr);
                }
                catch (Exception ex)
                {
                    return (false, "", $"Исключение при трансляции: {ex.Message}");
                }
            });

            if (!string.IsNullOrWhiteSpace(transOut)) await LogAsync(transOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(transErr)) await LogAsync("[Предупреждения/Ошибки транслятора]\n" + transErr.TrimEnd());

            if (!transSuccess)
            {
                await SetBuildErrorStatusAsync($"Planning C: Ошибка трансляции {Path.GetFileName(sourceFile)}");
                await LogAsync($"[ОШИБКА] Трансляция Planning C завершилась с ошибкой. {actionTitle} остановлен.");
                return (false, "", "", "");
            }

            // Постобработка директив #line в сгенерированном C++ файле через выделенный процессор
            await Task.Run(() =>
            {
                try
                {
                    LineDirectivesPostprocessor.ProcessFile(translatedCpp, sourceFile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PlanningCDebuggerLauncher] Ошибка постобработки #line: {ex.Message}");
                }
            });

            await LogAsync($"[2/3] Компиляция C++ через MSVC (cl.exe) в {Path.GetFileName(targetExe)}...");
            await SetProgressAsync($"Planning C: [2/3] Компиляция C++ ({Path.GetFileName(targetExe)})...", 2, 3, true);

            // 2. Запуск компилятора MSVC cl.exe
            var (compileSuccess, compileOut, compileErr) = await Task.Run(() =>
            {
                try
                {
                    var incList = new System.Collections.Generic.List<string>();
                    if (!string.IsNullOrEmpty(includeDir) && Directory.Exists(includeDir)) incList.Add($"/I\"{includeDir}\"");
                    if (!string.IsNullOrEmpty(translatorDir) && Directory.Exists(translatorDir) && !string.Equals(includeDir, translatorDir, StringComparison.OrdinalIgnoreCase)) incList.Add($"/I\"{translatorDir}\"");
                    string incParam = string.Join(" ", incList);
                    string compilerFlags = options.CompilerFlags;
                    if (compilerFlags.Contains("/std:c++14"))
                    {
                        compilerFlags = compilerFlags.Replace("/std:c++14", "/std:c++17");
                    }
                    else if (!compilerFlags.Contains("/std:c++17") && !compilerFlags.Contains("/std:c++20") && !compilerFlags.Contains("/std:c++latest"))
                    {
                        compilerFlags += " /std:c++17";
                    }
                    if (!compilerFlags.Contains("/F") && !compilerFlags.Contains("/STACK"))
                    {
                        compilerFlags += " /F8388608";
                    }

                    string openClLibParam = File.Exists(Path.Combine(translatorDir, "OpenCL.lib")) ? $" /link /LIBPATH:\"{translatorDir}\" OpenCL.lib" : "";
                    string compileCmd = !string.IsNullOrEmpty(vcvarsBat)
                        ? $"call \"{vcvarsBat}\" > nul && cl.exe {compilerFlags} {incParam} \"{translatedCpp}\" /Fe\"{targetExe}\" /Fd\"{targetPdb}\"{openClLibParam}"
                        : $"cl.exe {compilerFlags} {incParam} \"{translatedCpp}\" /Fe\"{targetExe}\" /Fd\"{targetPdb}\"{openClLibParam}";

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{compileCmd}\"",
                        WorkingDirectory = buildDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc == null) return (false, "", "Не удалось запустить процесс компилятора");

                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();

                    int compTimeoutMs = Math.Max(1, options.CompilerTimeoutSeconds) * 1000;
                    bool finished = proc.WaitForExit(compTimeoutMs);
                    if (!finished)
                    {
                        try { proc.Kill(); } catch { }
                        return (false, "", $"Превышено время ожидания компилятора MSVC cl.exe (таймаут {options.CompilerTimeoutSeconds} сек). Процесс остановлен.\nИзменить таймаут можно в 'Сервис -> Параметры -> Planning C'.");
                    }

                    string stdOut = outTask.Result;
                    string stdErr = errTask.Result;

                    bool ok = proc.ExitCode == 0 && File.Exists(targetExe);
                    return (ok, stdOut, stdErr);
                }
                catch (Exception ex)
                {
                    return (false, "", $"Исключение при компиляции: {ex.Message}");
                }
            });

            if (!string.IsNullOrWhiteSpace(compileOut)) await LogAsync(compileOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(compileErr)) await LogAsync(compileErr.TrimEnd());

            if (!compileSuccess)
            {
                await SetBuildErrorStatusAsync($"Planning C: Ошибка компиляции C++");
                await LogAsync($"[ОШИБКА] Компиляция C++ завершилась с ошибкой. {actionTitle} остановлен.");
                return (false, "", "", "");
            }

            return (true, targetExe, buildDir, sourceDir);
        }

        /// <summary>
        /// Запуск отладки для активного документа (F5 / Shift+Alt+F5).
        /// </summary>
        public async Task LaunchDebugAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var (success, targetExe, _, sourceDir) = await BuildAsync("Запуск отладки Planning C");
                if (!success) return;

                await SetProgressAsync($"Planning C: [3/3] Подключение отладчика к {Path.GetFileName(targetExe)}...", 3, 3, true);
                await LogAsync($"[3/3] [Подключение отладчика] Запуск нативного отладчика Visual Studio к {Path.GetFileName(targetExe)}...");

                try
                {
                    var debugTarget = new VsDebugTargetInfo
                    {
                        cbSize = (uint)Marshal.SizeOf(typeof(VsDebugTargetInfo)),
                        dlo = DEBUG_LAUNCH_OPERATION.DLO_CreateProcess,
                        bstrExe = targetExe,
                        bstrCurDir = sourceDir,
                        bstrArg = "",
                        bstrRemoteMachine = null,
                        fSendStdoutToOutputWindow = 1,
                        clsidCustom = VSConstants.DebugEnginesGuids.NativeOnly_guid,
                        grfLaunch = (uint)__VSDBGLAUNCHFLAGS.DBGLAUNCH_StopDebuggingOnEnd
                    };

                    VsShellUtilities.LaunchDebugger(_package, debugTarget);
                    await SetProgressAsync($"Planning C: Отладчик запущен ({Path.GetFileName(targetExe)})", 3, 3, false);
                    await LogAsync("[Planning C] Отладчик Visual Studio успешно запущен! Точки останова в исходном файле активны.");
                }
                catch (Exception ex)
                {
                    await SetBuildErrorStatusAsync("[Planning C] Ошибка запуска отладчика");
                    await LogAsync($"[ОШИБКА запуска отладчика]: {ex.Message}");
                }
            }
            finally
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Сборка и запуск программы Planning C без отладчика в интерактивной консоли (Ctrl+F5).
        /// </summary>
        public async Task LaunchRunAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                var (success, targetExe, _, sourceDir) = await BuildAsync("Запуск Planning C без отладки");
                if (!success) return;

                await SetProgressAsync($"Planning C: [3/3] Запуск {Path.GetFileName(targetExe)} в интерактивной консоли...", 3, 3, true);
                await LogAsync($"[3/3] [Запуск без отладки] Запуск {Path.GetFileName(targetExe)} в интерактивной консоли...");

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"\"{targetExe}\" & echo. & echo ------------------------------------------------ & echo Нажмите любую клавишу для продолжения . . . & pause > nul\"",
                        WorkingDirectory = sourceDir,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };

                    System.Diagnostics.Process.Start(psi);
                    await SetProgressAsync($"Planning C: Процесс {Path.GetFileName(targetExe)} запущен", 3, 3, false);
                    await LogAsync($"[Planning C] Процесс {Path.GetFileName(targetExe)} успешно запущен без отладчика.");
                }
                catch (Exception ex)
                {
                    await SetBuildErrorStatusAsync("[Planning C] Ошибка запуска консоли");
                    await LogAsync($"[ОШИБКА запуска процесса]: {ex.Message}");
                }
            }
            finally
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Очистка промежуточных файлов сборки (.plc_build) для активного файла / решения.
        /// </summary>
        public async Task CleanBuildFilesAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            try
            {
                await ShowOutputWindowAsync();
                var options = (PlanningCOptionsPage)_package.GetDialogPage(typeof(PlanningCOptionsPage));
                string subFolder = string.IsNullOrWhiteSpace(options.BuildSubfolder) ? ".plc_build" : options.BuildSubfolder.Trim();

                Document? activeDoc = _dte.ActiveDocument;
                string? startDir = null;
                if (activeDoc != null && !string.IsNullOrEmpty(activeDoc.FullName))
                {
                    startDir = Path.GetDirectoryName(activeDoc.FullName);
                }
                else if (_dte.Solution != null && !string.IsNullOrEmpty(_dte.Solution.FullName))
                {
                    startDir = Path.GetDirectoryName(_dte.Solution.FullName);
                }

                if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir))
                {
                    await LogAsync("[Planning C] Нет активного документа или решения для определения папки сборки.");
                    return;
                }

                string buildDir = Path.Combine(startDir, subFolder);
                if (!Directory.Exists(buildDir))
                {
                    await LogAsync($"[Planning C] Папка сборки '{subFolder}' не существует в '{startDir}'. Очистка не требуется.");
                    return;
                }

                await LogAsync($"[Planning C] Очистка промежуточных файлов в '{buildDir}'...");

                int deletedCount = 0;
                await Task.Run(() =>
                {
                    try
                    {
                        var files = Directory.GetFiles(buildDir);
                        foreach (var f in files)
                        {
                            try
                            {
                                File.Delete(f);
                                deletedCount++;
                            }
                            catch { }
                        }

                        if (Directory.GetFiles(buildDir).Length == 0 && Directory.GetDirectories(buildDir).Length == 0)
                        {
                            Directory.Delete(buildDir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(ex);
                    }
                });

                await LogAsync($"[Planning C] Очистка завершена. Удалено файлов: {deletedCount}.");
            }
            finally
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Windows.Input.Mouse.OverrideCursor = null;
            }
        }
    }
}
