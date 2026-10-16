using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using DteExpression = EnvDTE.Expression;

namespace plcMSVCplugin
{
    public class ThreadItemModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string State { get; set; } = "";
        public string Location { get; set; } = "";
        public string Priority { get; set; } = "";
        public Thread? DteThread { get; set; }
    }

    public class FunnelItemModel
    {
        public string Name { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Capacity { get; set; } = "";
        public string State { get; set; } = "";
        public string ContentPreview { get; set; } = "";
        public string DetailText { get; set; } = "";
    }

    /// <summary>
    /// Interaction logic for PlanInspectorControl.xaml
    /// Регулирует отображение: потоков отладки, содержимого каналов данных и состояния выполнения плана.
    /// </summary>
    public partial class PlanInspectorControl : UserControl
    {
        private readonly ObservableCollection<ThreadItemModel> _threads = new ObservableCollection<ThreadItemModel>();
        private readonly ObservableCollection<FunnelItemModel> _funnels = new ObservableCollection<FunnelItemModel>();
        private readonly ObservableCollection<string> _callStack = new ObservableCollection<string>();

        public PlanInspectorControl()
        {
            InitializeComponent();
            GridThreads.ItemsSource = _threads;
            GridFunnels.ItemsSource = _funnels;
            ListCallStack.ItemsSource = _callStack;
        }

        public bool AutoRefreshEnabled => ChkAutoRefresh.IsChecked == true;

#pragma warning disable VSSDK007
        public void AppendLog(string text)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
                TxtLog.ScrollToEnd();
            });
        }

        public void SetBuildStatus(string text, Color indicatorColor)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StatusIndicator.Fill = new SolidColorBrush(indicatorColor);
                StatusText.Text = text;
            });
        }
#pragma warning restore VSSDK007

        private void SetActionRunning(bool isRunning, string status)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            BtnStartDebug.IsEnabled = !isRunning;
            BtnRunWithoutDebug.IsEnabled = !isRunning;
            BtnCleanBuild.IsEnabled = !isRunning;
            if (isRunning)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                StatusText.Text = status;
                Mouse.OverrideCursor = Cursors.Wait;
            }
            else
            {
                Mouse.OverrideCursor = null;
            }
        }

#pragma warning disable VSSDK007
        private void BtnStartDebug_Click(object sender, RoutedEventArgs e)
        {
            SetActionRunning(true, "Сборка Planning C...");
            AppendLog("Нажата кнопка: Отладка (Shift+Alt+F5). Запуск сборки...");

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var package = plcMSVCpluginPackage.Instance;
                    if (package == null && ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) is IVsShell shell)
                    {
                        Guid pkgGuid = new Guid(plcMSVCpluginPackage.PackageGuidString);
                        shell.LoadPackage(ref pkgGuid, out _);
                        package = plcMSVCpluginPackage.Instance;
                    }

                    if (package != null && await package.GetServiceAsync(typeof(DTE)) is DTE2 dte)
                    {
                        var launcher = new PlanningCDebuggerLauncher(package, dte);
                        await launcher.LaunchDebugAsync();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[Ошибка запуска]: {ex.Message}");
                }
                finally
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    SetActionRunning(false, StatusText.Text);
                }
            });
        }

        private void BtnRunWithoutDebug_Click(object sender, RoutedEventArgs e)
        {
            SetActionRunning(true, "Сборка Planning C...");
            AppendLog("Нажата кнопка: Без отладки (Ctrl+F5). Запуск сборки...");

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var package = plcMSVCpluginPackage.Instance;
                    if (package == null && ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) is IVsShell shell)
                    {
                        Guid pkgGuid = new Guid(plcMSVCpluginPackage.PackageGuidString);
                        shell.LoadPackage(ref pkgGuid, out _);
                        package = plcMSVCpluginPackage.Instance;
                    }

                    if (package != null && await package.GetServiceAsync(typeof(DTE)) is DTE2 dte)
                    {
                        var launcher = new PlanningCDebuggerLauncher(package, dte);
                        await launcher.LaunchRunAsync();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[Ошибка запуска]: {ex.Message}");
                }
                finally
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    SetActionRunning(false, StatusText.Text);
                }
            });
        }

        private void BtnCleanBuild_Click(object sender, RoutedEventArgs e)
        {
            SetActionRunning(true, "Очистка .plc_build...");
            AppendLog("Нажата кнопка: Очистить .plc_build. Очистка...");

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var package = plcMSVCpluginPackage.Instance;
                    if (package == null && ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) is IVsShell shell)
                    {
                        Guid pkgGuid = new Guid(plcMSVCpluginPackage.PackageGuidString);
                        shell.LoadPackage(ref pkgGuid, out _);
                        package = plcMSVCpluginPackage.Instance;
                    }

                    if (package != null && await package.GetServiceAsync(typeof(DTE)) is DTE2 dte)
                    {
                        var launcher = new PlanningCDebuggerLauncher(package, dte);
                        await launcher.CleanBuildFilesAsync();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"[Ошибка очистки]: {ex.Message}");
                }
                finally
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    SetActionRunning(false, StatusText.Text);
                }
            });
        }
#pragma warning restore VSSDK007

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(DTE)) is DTE2 dte)
            {
                UpdateDebuggerState(dte);
            }
        }

        private void GridThreads_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _callStack.Clear();

            if (GridThreads.SelectedItem is ThreadItemModel item && item.DteThread != null)
            {
                try
                {
                    foreach (StackFrame frame in item.DteThread.StackFrames)
                    {
                        string line = !string.IsNullOrEmpty(frame.Module)
                            ? $"{frame.FunctionName} [{frame.Module}]"
                            : frame.FunctionName;
                        _callStack.Add(line);
                    }
                }
                catch
                {
                    _callStack.Add("(Стек вызовов недоступен для выбранного потока)");
                }
            }
        }

        private void GridFunnels_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridFunnels.SelectedItem is FunnelItemModel item)
            {
                TxtBufferDetail.Text = item.DetailText;
            }
        }

        /// <summary>
        /// Обновление визуального состояния при переходе отладчика в режим останова или выполнения.
        /// </summary>
        public void UpdateDebuggerState(DTE2 dte, string? breakReason = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte.Debugger == null)
            {
                SetModeDesign();
                return;
            }

            switch (dte.Debugger.CurrentMode)
            {
                case dbgDebugMode.dbgBreakMode:
                    SetModeBreak(dte, breakReason);
                    break;
                case dbgDebugMode.dbgRunMode:
                    SetModeRun();
                    break;
                default:
                    SetModeDesign();
                    break;
            }
        }

        private void SetModeDesign()
        {
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(128, 128, 128));
            StatusText.Text = "Отладчик остановлен";
            _threads.Clear();
            _callStack.Clear();
            _funnels.Clear();
            TreePlan.Items.Clear();

            TxtPlanStatus.Text = "План не активен";
            TxtPlanTasksCount.Text = "0";
            TxtCurrentStage.Text = "-";
            TxtTotalStages.Text = "-";
            TxtProcessorContext.Text = "-";
            TxtParallelMode.Text = "-";

            TxtBufferDetail.Text = "Выберите канал в таблице выше для детального просмотра его элементов.";
            TxtTopologyInfo.Text = "Топология не инициализирована";
            TxtProcessorId.Text = "Идентификатор процессора (plan_processor_id): -";
            TxtProcessorsCount.Text = "Всего процессоров (plan_processors): -";
            TxtLinearNum.Text = "Линейный номер узла (plan_linear_num): -";
            TxtTopologyNum.Text = "Номер в топологии (plan_topology_num): -";
        }

        private void SetModeRun()
        {
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(40, 167, 69));
            StatusText.Text = "Программа выполняется (Running)...";
            TxtPlanStatus.Text = "Выполняется планирование вычислений...";
        }

        private void SetModeBreak(DTE2 dte, string? reason)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 53, 69));
            StatusText.Text = $"Точка останова: {reason ?? "Выполнение приостановлено"}";

            // 1. Отображение состояния потоков
            _threads.Clear();
            try
            {
                if (dte.Debugger.CurrentProgram?.Threads != null)
                {
                    Thread? currentThread = dte.Debugger.CurrentThread;
                    foreach (Thread th in dte.Debugger.CurrentProgram.Threads)
                    {
                        string loc = "-";
                        try
                        {
                            if (th.StackFrames != null && th.StackFrames.Count > 0)
                            {
                                StackFrame top = th.StackFrames.Item(1);
                                loc = !string.IsNullOrEmpty(top.Module)
                                    ? $"{top.FunctionName} [{top.Module}]"
                                    : top.FunctionName;
                            }
                        }
                        catch { }

                        bool isCurrent = currentThread != null && currentThread.ID == th.ID;
                        string state = isCurrent ? "[Текущий] Точка останова" : (th.IsFrozen ? "Приостановлен" : "Активен");

                        var model = new ThreadItemModel
                        {
                            Id = th.ID,
                            Name = !string.IsNullOrEmpty(th.Name) ? th.Name : (isCurrent ? "Главный поток (Master/Worker)" : "Параллельный поток Planning C"),
                            State = state,
                            Location = loc,
                            Priority = th.Priority,
                            DteThread = th
                        };

                        _threads.Add(model);
                        if (isCurrent)
                        {
                            GridThreads.SelectedItem = model;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка инспекции потоков: {ex.Message}");
            }

            // 2. Инспекция состояния плана (стадии, очереди, статус выполнения)
            TreePlan.Items.Clear();
            try
            {
                // Проверка текущей стадии
                DteExpression expStage = dte.Debugger.GetExpression("__throw_stage__", false, 500);
                if (expStage == null || !expStage.IsValidValue)
                {
                    expStage = dte.Debugger.GetExpression("reent_stage()", false, 500);
                }
                string currentStageVal = (expStage != null && expStage.IsValidValue) ? expStage.Value : "0";
                TxtCurrentStage.Text = $"Стадия #{currentStageVal}";

                // Общее число стадий
                DteExpression expNumStages = dte.Debugger.GetExpression("__num_stages__", false, 500);
                if (expNumStages == null || !expNumStages.IsValidValue)
                {
                    expNumStages = dte.Debugger.GetExpression("reent_num_stages()", false, 500);
                }
                string totalStagesVal = (expNumStages != null && expNumStages.IsValidValue) ? expNumStages.Value : "1";
                TxtTotalStages.Text = totalStagesVal;

                // Очередь задач плана (__event_list__ / _events_...)
                DteExpression expListN = dte.Debugger.GetExpression("__event_list__->N", false, 500);
                int tasksCount = 0;
                if (expListN != null && expListN.IsValidValue && int.TryParse(expListN.Value, out int nVal))
                {
                    tasksCount = nVal;
                }
                TxtPlanTasksCount.Text = tasksCount.ToString();

                DteExpression expPlanEmpty = dte.Debugger.GetExpression("plan_empty()", false, 500);
                bool isEmpty = tasksCount == 0 || (expPlanEmpty != null && expPlanEmpty.IsValidValue && (expPlanEmpty.Value == "true" || expPlanEmpty.Value == "1"));

                TxtPlanStatus.Text = isEmpty ? "Очередь плана пуста (Все задачи выполнены)" : $"План активен ({tasksCount} задач в очереди)";

                // Процессорный контекст
                DteExpression expProcId = dte.Debugger.GetExpression("_reent_processor_id", false, 500);
                DteExpression expProcsCount = dte.Debugger.GetExpression("_reent_processors", false, 500);
                string procId = (expProcId != null && expProcId.IsValidValue) ? expProcId.Value : "0";
                string procsCount = (expProcsCount != null && expProcsCount.IsValidValue) ? expProcsCount.Value : "1";
                TxtProcessorContext.Text = $"Процессор #{procId} из {procsCount}";
                TxtProcessorId.Text = $"Идентификатор процессора (plan_processor_id): {procId}";
                TxtProcessorsCount.Text = $"Всего процессоров (plan_processors): {procsCount}";

                // Параллельный режим
                DteExpression expParallel = dte.Debugger.GetExpression("*__parallel_flag__", false, 500);
                if (expParallel != null && expParallel.IsValidValue)
                {
                    TxtParallelMode.Text = expParallel.Value != "0" ? "Параллельный (OpenMP/MPI)" : "Последовательный";
                }
                else
                {
                    TxtParallelMode.Text = "Параллельный конвейер";
                }

                // Линейный и топологический номер
                DteExpression expLinNum = dte.Debugger.GetExpression("plan_linear_num()", false, 500);
                if (expLinNum != null && expLinNum.IsValidValue) TxtLinearNum.Text = $"Линейный номер узла (plan_linear_num): {expLinNum.Value}";

                DteExpression expTopoNum = dte.Debugger.GetExpression("plan_topology_num()", false, 500);
                if (expTopoNum != null && expTopoNum.IsValidValue) TxtTopologyNum.Text = $"Номер в топологии (plan_topology_num): {expTopoNum.Value}";

                // Формирование дерева плана
                var nodePlan = new TreeViewItem
                {
                    Header = $"Состояние плана: {TxtPlanStatus.Text} [Стадия {currentStageVal}/{totalStagesVal}]",
                    IsExpanded = true,
                    FontWeight = FontWeights.Bold
                };

                nodePlan.Items.Add(new TreeViewItem { Header = $"Текущая стадия конвейера: #{currentStageVal} (всего стадий: {totalStagesVal})" });
                nodePlan.Items.Add(new TreeViewItem { Header = $"Задач ожидает исполнения в очереди: {tasksCount}" });
                nodePlan.Items.Add(new TreeViewItem { Header = $"Режим исполнения: {TxtParallelMode.Text}" });
                nodePlan.Items.Add(new TreeViewItem { Header = $"Контекст выполнения: Процессор #{procId} (всего узлов: {procsCount})" });
                TreePlan.Items.Add(nodePlan);

                var nodeQueue = new TreeViewItem
                {
                    Header = $"Очереди задач плана (__event_list__): {tasksCount} задач",
                    IsExpanded = true
                };

                if (tasksCount > 0)
                {
                    for (int i = 0; i < Math.Min(tasksCount, 10); i++)
                    {
                        DteExpression expTaskItem = dte.Debugger.GetExpression($"__event_list__->Begin[{i}].Data", false, 500);
                        string itemStr = (expTaskItem != null && expTaskItem.IsValidValue) ? expTaskItem.Value : $"Задача #{i + 1}";
                        nodeQueue.Items.Add(new TreeViewItem { Header = $"[Задача {i + 1}]: {itemStr}" });
                    }
                    if (tasksCount > 10)
                    {
                        nodeQueue.Items.Add(new TreeViewItem { Header = $"... еще {tasksCount - 10} задач в очереди" });
                    }
                }
                else
                {
                    nodeQueue.Items.Add(new TreeViewItem { Header = "(Очередь задач пуста, ожидание генерации throw / plan_first)" });
                }
                TreePlan.Items.Add(nodeQueue);
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка инспекции состояния плана: {ex.Message}");
            }

            // 3. Отображение содержимого (каналы данных, воронки, буферы)
            _funnels.Clear();
            try
            {
                // Поиск каналов и воронок в текущей области видимости стека вызовов (Locals)
                if (dte.Debugger.CurrentStackFrame?.Locals != null)
                {
                    foreach (DteExpression local in dte.Debugger.CurrentStackFrame.Locals)
                    {
                        string type = local.Type ?? "";
                        if (type.Contains("funnel") || type.Contains("cfunnel") || type.Contains("cvar") || type.Contains("TChannel"))
                        {
                            string dir = type.Contains("funneled_in") ? "Входной канал (in)"
                                       : type.Contains("funneled_out") ? "Выходной канал (out)"
                                       : type.Contains("cfunnel") ? "Кольцевой канал (cfunnel)"
                                       : "Канал данных (funnel)";

                            // Получаем емкость
                            DteExpression expCap = dte.Debugger.GetExpression($"{local.Name}.getN()", false, 500);
                            string cap = (expCap != null && expCap.IsValidValue) ? expCap.Value : "-";

                            // Получаем состояние пустоты
                            DteExpression expEmpty = dte.Debugger.GetExpression($"{local.Name}.empty()", false, 500);
                            string state = (expEmpty != null && expEmpty.IsValidValue && (expEmpty.Value == "true" || expEmpty.Value == "1"))
                                ? "Пуст (Ready)" : "Содержит данные";

                            // Извлечение содержимого буфера канала
                            var sbDetail = new StringBuilder();
                            sbDetail.AppendLine($"Канал данных: {local.Name}");
                            sbDetail.AppendLine($"Тип: {type}");
                            sbDetail.AppendLine($"Направление: {dir}");
                            sbDetail.AppendLine($"Емкость буфера (N): {cap}");
                            sbDetail.AppendLine($"Состояние: {state}");
                            sbDetail.AppendLine($"Значение (Natvis): {local.Value}");
                            sbDetail.AppendLine();
                            sbDetail.AppendLine("--- Содержимое элементов буфера Data[] ---");

                            // Читаем элементы массива Data
                            DteExpression expData = dte.Debugger.GetExpression($"{local.Name}.Data", false, 500);
                            string contentPreview = local.Value ?? "";

                            if (expData != null && expData.IsValidValue && int.TryParse(cap, out int capN) && capN > 0)
                            {
                                int previewCount = Math.Min(capN, 8);
                                var previewSb = new StringBuilder();
                                for (int idx = 0; idx < previewCount; idx++)
                                {
                                    DteExpression elem = dte.Debugger.GetExpression($"{local.Name}.Data[{idx}]", false, 500);
                                    string val = (elem != null && elem.IsValidValue) ? elem.Value : "?";
                                    sbDetail.AppendLine($"  [{idx}] = {val}");
                                    if (idx < 4) previewSb.Append($"[{val}] ");
                                }
                                if (capN > previewCount) sbDetail.AppendLine($"  ... и еще {capN - previewCount} элементов");
                                contentPreview = previewSb.ToString().TrimEnd();
                            }

                            _funnels.Add(new FunnelItemModel
                            {
                                Name = local.Name,
                                Direction = dir,
                                Capacity = cap,
                                State = state,
                                ContentPreview = !string.IsNullOrEmpty(contentPreview) ? contentPreview : (local.Value ?? ""),
                                DetailText = sbDetail.ToString()
                            });
                        }
                    }
                }

                // Инспекция глобального реестра воронок __Funnels
                DteExpression expFunnels = dte.Debugger.GetExpression("__Funnels", false, 500);
                if (expFunnels != null && expFunnels.IsValidValue)
                {
                    TxtTopologyInfo.Text = $"Глобальный реестр каналов зарегистрирован. Объектов в системе: {expFunnels.Value}";
                }

                if (_funnels.Count > 0)
                {
                    GridFunnels.SelectedIndex = 0;
                }
                else
                {
                    TxtBufferDetail.Text = "В текущем фрейме выполнения локальных каналов funnel/cfunnel не обнаружено.\nИспользуйте окно Watch для произвольных выражений.";
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Ошибка отображения содержимого каналов: {ex.Message}");
            }
        }
    }
}
