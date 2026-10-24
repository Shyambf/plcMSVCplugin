using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace plcMSVCplugin
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("PlanningCQuickInfoSourceProvider")]
    [ContentType("C/C++")]
    [ContentType("code++.C++")]
    [ContentType("c")]
    [ContentType("cpp")]
    [ContentType("code")]
    [Order(Before = "Default")]
    internal class PlanningCQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(() => new PlanningCQuickInfoSource(textBuffer));
        }
    }

    /// <summary>
    /// Источник всплывающих подсказок (QuickInfo / DataTips) для Planning C 2.0.
    /// В режиме отладки (Break Mode): инспектирует каналы funnel/cfunnel, переменные cvar и стадии throw_stage.
    /// В режиме редактирования: отображает документацию по ключевым словам и типам Planning C.
    /// </summary>
    internal class PlanningCQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _textBuffer;

        private static readonly Dictionary<string, (string Title, string Category, string Description)> LanguageDocs =
            new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                { "funnel", ("funnel<T>", "Канал передачи данных", "Асинхронная потокобезопасная очередь FIFO для обмена сообщениями между параллельными ветвями графа. Поддерживает операции << (запись) и >> (чтение).") },
                { "cfunnel", ("cfunnel<T>", "Кольцевой канал", "Циклический буфер фиксированной емкости с перезаписью старых элементов. Используется для потоковых сигналов и скользящих окон данных.") },
                { "cvar", ("cvar<T>", "Разделяемая переменная", "Синхронизированная разделяемая переменная Planning C. Защищает доступ из параллельных реэнтабельных блоков.") },
                { "cvar_in", ("cvar_in<T>", "Входной порт переменной", "Интерфейс только для чтения значения разделяемой переменной cvar.") },
                { "cvar_out", ("cvar_out<T>", "Выходной порт переменной", "Интерфейс записи нового значения в разделяемую переменную cvar.") },
                { "reenterable", ("reenterable", "Параллельная процедура", "Многократно входимая параллельная процедура Planning C. Исполняется асинхронно в пуле потоков OpenMP.") },
                { "reentrant", ("reentrant", "Блок параллелизма", "Обозначение реэнтабельного блока или параллельной стадии выполнения.") },
                { "chain", ("chain", "Цепочка стадий", "Оператор вызова цепочки стадий планирования или композиции параллельных процедур.") },
                { "throw", ("throw", "Активация стадии", "Генерация события планировщика или перевод выполнения на целевую стадию графа.") },
                { "throw_stage", ("throw_stage", "Текущая стадия плана", "Системный контекст Planning C: номер текущей исполняемой стадии параллельного плана.") },
                { "plan_first", ("plan_first", "Точка входа плана", "Начальная стадия инициализации и старта графа планирования.") },
                { "plan_last", ("plan_last", "Финальная стадия плана", "Завершающая стадия, сигнализирующая об успешном окончании параллельного алгоритма.") },
                { "plan_stop", ("plan_stop", "Остановка планировщика", "Аварийная или плановая остановка всех активных параллельных ветвей и цикла планирования.") },
                { "markupdef", ("markupdef", "Разметка топологии", "Декларативное описание связей, топологии каналов и зависимостей графа Planning C.") },
                { "pre_id", ("pre_id", "Идентификатор предшественника", "Идентификатор предшествующей стадии в графе выполнения.") },
                { "topology", ("topology", "Граф топологии", "Объект графа топологии параллельных процессов Planning C.") },
                { "__Topology", ("__Topology", "Дескриптор топологии", "Внутренняя структура планировщика, хранящая матрицу связей и очереди задач.") },
                { "num_stages", ("num_stages", "Число стадий", "Общее количество стадий, зарегистрированных в текущей топологии плана.") },
                { "__event_list__", ("__event_list__", "Очередь событий", "Системный список ожидающих выполнения событий и параллельных задач планировщика.") }
            };

        public PlanningCQuickInfoSource(ITextBuffer textBuffer)
        {
            _textBuffer = textBuffer;
        }

        public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            SnapshotPoint? triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
            if (!triggerPoint.HasValue) return null;

            ITextSnapshotLine line = triggerPoint.Value.GetContainingLine();
            string lineText = line.GetText();
            int offset = triggerPoint.Value.Position - line.Start.Position;

            if (offset > lineText.Length) offset = lineText.Length;
            if (offset > 0 && (offset == lineText.Length || !IsIdentChar(lineText[offset])))
            {
                if (IsIdentChar(lineText[offset - 1])) offset--;
            }

            if (offset < 0 || offset >= lineText.Length || !IsIdentChar(lineText[offset]))
            {
                return null;
            }

            int start = offset;
            while (start > 0 && IsIdentChar(lineText[start - 1])) start--;

            int end = offset;
            while (end < lineText.Length && IsIdentChar(lineText[end])) end++;

            string word = lineText.Substring(start, end - start);
            if (string.IsNullOrWhiteSpace(word)) return null;

            ITrackingSpan trackingSpan = _textBuffer.CurrentSnapshot.CreateTrackingSpan(
                line.Start.Position + start, word.Length, SpanTrackingMode.EdgeInclusive);

            // 1. Проверяем режим отладки (Break Mode)
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                if (Package.GetGlobalService(typeof(DTE)) is DTE2 dte && dte.Debugger != null)
                {
                    if (dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
                    {
                        var debugTip = TryCreateDebugDataTip(trackingSpan, word, dte);
                        if (debugTip != null)
                        {
                            return debugTip;
                        }
                    }
                }
            }
            catch { }

            // 2. Если не в отладке или переменная не найдена — проверяем документацию Planning C
            if (LanguageDocs.TryGetValue(word, out var docInfo))
            {
                var docTip = CreateLanguageQuickInfo(trackingSpan, docInfo.Title, docInfo.Category, docInfo.Description);
                return docTip;
            }

            return null;
        }

        private static bool IsIdentChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private QuickInfoItem? TryCreateDebugDataTip(ITrackingSpan span, string word, DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            EnvDTE.Expression? expr = null;
            try
            {
                expr = dte.Debugger.GetExpression(word, true, 400);
            }
            catch { }

            if (expr == null || !expr.IsValidValue)
            {
                return null;
            }

            string exprType = expr.Type ?? "";
            string exprVal = expr.Value ?? "";

            bool isChannel = exprType.IndexOf("funnel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             exprType.IndexOf("_funnel", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isCVar = exprType.IndexOf("cvar", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isList = exprType.IndexOf("reent_list", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isStage = word.Equals("throw_stage", StringComparison.OrdinalIgnoreCase);

            string category = isChannel ? "Канал-воронка (Planning C)" :
                             isCVar ? "Разделяемая переменная cvar" :
                             isList ? "Список задач reent_list" :
                             isStage ? "Стадия плана Planning C" :
                             "Переменная (Отладка Planning C)";

            // Сбор дополнительных сведений для каналов
            string? countInfo = null;
            string? frontData = null;

            if (isChannel)
            {
                try
                {
                    var countExpr = dte.Debugger.GetExpression($"{word}.getN()", true, 300);
                    if (!countExpr.IsValidValue) countExpr = dte.Debugger.GetExpression($"{word}.N", true, 300);
                    if (countExpr.IsValidValue)
                    {
                        countInfo = countExpr.Value;
                        if (int.TryParse(countInfo, out int cnt) && cnt > 0)
                        {
                            var dataExpr = dte.Debugger.GetExpression($"{word}.Data[0]", true, 300);
                            if (dataExpr.IsValidValue) frontData = dataExpr.Value;
                        }
                    }
                }
                catch { }
            }
            else if (isCVar)
            {
                try
                {
                    var valExpr = dte.Debugger.GetExpression($"{word}.Value", true, 300);
                    if (valExpr.IsValidValue) countInfo = valExpr.Value;
                }
                catch { }
            }
            else if (isStage)
            {
                try
                {
                    var totalExpr = dte.Debugger.GetExpression("num_stages", true, 300);
                    if (totalExpr.IsValidValue) countInfo = $"Всего стадий в плане: {totalExpr.Value}";
                }
                catch { }
            }

            var card = BuildDataTipUI(word, exprType, exprVal, category, countInfo, frontData);
            return new QuickInfoItem(span, card);
        }

        private static FrameworkElement BuildDataTipUI(
            string name, string type, string val, string category, string? extra1, string? extra2)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 74)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                MaxWidth = 550
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical };

            // Бейдж категории
            var badgeBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(60, 40, 95)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var badgeText = new TextBlock
            {
                Text = category,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 200, 255)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            badgeBorder.Child = badgeText;
            stack.Children.Add(badgeBorder);

            // Имя и тип
            var titleBlock = new TextBlock { Margin = new Thickness(0, 2, 0, 4) };
            titleBlock.Inlines.Add(new System.Windows.Documents.Run(type + " ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
                FontFamily = new FontFamily("Consolas")
            });
            titleBlock.Inlines.Add(new System.Windows.Documents.Run(name)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas")
            });
            stack.Children.Add(titleBlock);

            // Значение
            var valBlock = new TextBlock { Margin = new Thickness(0, 2, 0, 2) };
            valBlock.Inlines.Add(new System.Windows.Documents.Run("Значение: ")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(156, 220, 254)),
                FontWeight = FontWeights.SemiBold
            });
            valBlock.Inlines.Add(new System.Windows.Documents.Run(val)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(214, 157, 133)),
                FontFamily = new FontFamily("Consolas")
            });
            stack.Children.Add(valBlock);

            // Дополнительные параметры
            if (!string.IsNullOrEmpty(extra1))
            {
                var extra1Block = new TextBlock
                {
                    Text = extra1!.StartsWith("Всего") ? extra1 : $"Буферизовано элементов: {extra1}",
                    Foreground = new SolidColorBrush(Color.FromRgb(181, 206, 168)),
                    Margin = new Thickness(0, 2, 0, 2),
                    FontFamily = new FontFamily("Consolas")
                };
                stack.Children.Add(extra1Block);
            }

            if (!string.IsNullOrEmpty(extra2))
            {
                var extra2Block = new TextBlock
                {
                    Text = $"Первый элемент [0]: {extra2}",
                    Foreground = new SolidColorBrush(Color.FromRgb(206, 145, 120)),
                    Margin = new Thickness(0, 2, 0, 2),
                    FontFamily = new FontFamily("Consolas")
                };
                stack.Children.Add(extra2Block);
            }

            border.Child = stack;
            return border;
        }

        private static QuickInfoItem CreateLanguageQuickInfo(
            ITrackingSpan span, string title, string category, string description)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(32, 32, 36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(65, 65, 70)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                MaxWidth = 500
            };

            var stack = new StackPanel { Orientation = Orientation.Vertical };

            // Категория
            var catBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 60, 90)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var catText = new TextBlock
            {
                Text = $"Planning C 2.0 • {category}",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 215, 255)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            catBorder.Child = catText;
            stack.Children.Add(catBorder);

            // Название / сигнатура
            var titleText = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 4)
            };
            stack.Children.Add(titleText);

            // Описание
            var descText = new TextBlock
            {
                Text = description,
                Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 210)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16,
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(descText);

            border.Child = stack;
            return new QuickInfoItem(span, border);
        }

        public void Dispose()
        {
        }
    }
}
