using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace plcMSVCplugin
{
    /// <summary>
    /// Постобработчик директив #line в сгенерированном транслятором C++ коде.
    /// Обеспечивает точную привязку точек останова, изоляцию служебных функций
    /// и корректную пошаговую трассировку в Visual Studio.
    /// </summary>
    internal static class LineDirectivesPostprocessor
    {
        private static readonly HashSet<string> IgnoredTrivialStatements = new HashSet<string>(StringComparer.Ordinal)
        {
            "{", "}", "return 0;", "return 1;", "return;", "return true;", "return false;",
            "break;", "continue;", "else"
        };

        private static readonly Regex ReenterableHeaderRegex = new Regex(
            @"reenterable(?:\[[^\]]*\])?(?:\s+(?:static|global|reent_in|funneled_in|funneled_out))*\s+([A-Za-z0-9_]+)\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex DispatcherHeaderRegex = new Regex(
            @"inline\s+void\s+__([A-Za-z0-9_]+)__\(",
            RegexOptions.Compiled);

        private static readonly Regex DispatcherCallRegex = new Regex(
            @"_call_([A-Za-z0-9_]+)\(",
            RegexOptions.Compiled);

        private static readonly Regex DispatcherEventsRegex = new Regex(
            @"_events_([A-Za-z0-9_]+)__LOC__",
            RegexOptions.Compiled);

        /// <summary>
        /// Выполняет постобработку директив #line в файле translatedCpp на основе исходного файла sourceFile.
        /// </summary>
        public static void ProcessFile(string translatedCpp, string sourceFile)
        {
            if (!File.Exists(translatedCpp)) return;

            string[] rawLines = File.ReadAllLines(translatedCpp);
            string[] srcLines = File.Exists(sourceFile) ? File.ReadAllLines(sourceFile) : Array.Empty<string>();

            var cleanLines = ProcessLines(rawLines, srcLines, sourceFile);
            File.WriteAllLines(translatedCpp, cleanLines);
        }

        /// <summary>
        /// Чистая функция преобразования строк для тестируемости и надежности.
        /// </summary>
        public static List<string> ProcessLines(string[] rawLines, string[] srcLines, string sourceFile)
        {
            // Карта строк исходного кода для привязки инструкций внутри развернутых макросов к реальным строкам файла
            var srcLineMap = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int sIdx = 0; sIdx < srcLines.Length; sIdx++)
            {
                string sTrim = srcLines[sIdx].Trim();
                if (sTrim.Length > 2 && !sTrim.StartsWith("//") && !sTrim.StartsWith("/*") && !sTrim.StartsWith("#"))
                {
                    if (!IgnoredTrivialStatements.Contains(sTrim) && !srcLineMap.ContainsKey(sTrim))
                    {
                        srcLineMap[sTrim] = sIdx + 1;
                    }
                }
            }

            // Карта строк объявлений планируемых процедур (reenterable Name(...))
            var procStartLineMap = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int sIdx = 0; sIdx < srcLines.Length; sIdx++)
            {
                var m = ReenterableHeaderRegex.Match(srcLines[sIdx]);
                if (m.Success)
                {
                    string pName = m.Groups[1].Value;
                    if (!procStartLineMap.ContainsKey(pName))
                    {
                        procStartLineMap[pName] = sIdx + 1;
                    }
                }
            }

            var cleanLines = new List<string>(rawLines.Length + 100);
            string normalizedSourceFile = Path.GetFullPath(sourceFile).Replace('\\', '/');
            string sourceFileNameWithoutExt = Path.GetFileNameWithoutExtension(sourceFile);

            bool inCallFunc = false;
            bool pendingCallBody = false;
            int callBraceDepth = 0;
            int callFuncFirstLine = 0;
            bool inVirtualMacro = false;

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i];
                string lineTrim = line.Trim();

                if (lineTrim.StartsWith("void _call_", StringComparison.Ordinal) && !lineTrim.EndsWith(";"))
                {
                    pendingCallBody = true;
                    callBraceDepth = 0;
                }

                // Привязка диспетчера inline void __Foo__ к строке заголовка Foo
                if (lineTrim.StartsWith("inline void __", StringComparison.Ordinal) && lineTrim.Contains("__("))
                {
                    var mDisp = DispatcherHeaderRegex.Match(lineTrim);
                    if (mDisp.Success)
                    {
                        string dispName = mDisp.Groups[1].Value;
                        if (procStartLineMap.TryGetValue(dispName, out int pLine))
                        {
                            cleanLines.Add($"#line {pLine} \"{normalizedSourceFile}\"");
                        }
                    }
                }

                if (line.StartsWith("#line ", StringComparison.Ordinal))
                {
                    int firstQuote = line.IndexOf('"');
                    int lastQuote = line.LastIndexOf('"');
                    if (firstQuote >= 0 && lastQuote > firstQuote)
                    {
                        string prefix = line.Substring(0, firstQuote + 1);
                        string pathPart = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1).Replace('\\', '/');
                        string suffix = line.Substring(lastQuote);

                        // Исключаем виртуальные буферы и макросы внутри кавычек
                        if (pathPart.StartsWith("PATTERN[", StringComparison.OrdinalIgnoreCase) ||
                            pathPart.StartsWith("while_else(", StringComparison.OrdinalIgnoreCase) ||
                            pathPart.StartsWith("make_", StringComparison.OrdinalIgnoreCase) ||
                            pathPart.IndexOf('(') >= 0 || pathPart.IndexOf(')') >= 0 ||
                            pathPart.IndexOf('[') >= 0 || pathPart.IndexOf(']') >= 0 ||
                            pathPart.IndexOf('\n') >= 0 || pathPart.IndexOf('\r') >= 0)
                        {
                            inVirtualMacro = true;
                            continue;
                        }

                        inVirtualMacro = false;

                        // Заменяем ссылки на .bak или временные имена на реальный исходник
                        if (pathPart.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                        {
                            pathPart = normalizedSourceFile;
                        }

                        string pathFileNameNoExt = Path.GetFileNameWithoutExtension(pathPart);
                        if (string.Equals(pathFileNameNoExt, sourceFileNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                        {
                            pathPart = normalizedSourceFile;
                        }

                        // Отслеживаем номер строки начала функции _call_
                        if (pendingCallBody && callFuncFirstLine == 0)
                        {
                            string[] parts = prefix.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && int.TryParse(parts[1], out int lineNum))
                            {
                                callFuncFirstLine = lineNum;
                            }
                        }

                        cleanLines.Add(prefix + pathPart + suffix);
                        continue;
                    }
                    else
                    {
                        cleanLines.Add(line);
                        continue;
                    }
                }

                // Начало тела функции _call_
                if (pendingCallBody && line.Contains("{"))
                {
                    inCallFunc = true;
                    pendingCallBody = false;
                    callBraceDepth = 0;
                }

                if (inCallFunc)
                {
                    for (int cIdx = 0; cIdx < line.Length; cIdx++)
                    {
                        char c = line[cIdx];
                        if (c == '{') callBraceDepth++;
                        else if (c == '}') callBraceDepth--;
                    }

                    // Точка перед первым исполняемым оператором процедуры (после lambda-инициализаций)
                    if (line.Contains("__dummy_counter += 713;"))
                    {
                        cleanLines.Add(line);
                        if (callFuncFirstLine > 0)
                        {
                            cleanLines.Add($"#line {callFuncFirstLine + 1} \"{normalizedSourceFile}\"");
                        }
                        continue;
                    }

                    // Выход из процедуры _call_: глубина фигурных скобок вернулась в 0
                    if (callBraceDepth == 0)
                    {
                        cleanLines.Add(line);
                        inCallFunc = false;
                        callFuncFirstLine = 0;
                        cleanLines.Add("#line 1 \"planning_c_internal.h\"");
                        continue;
                    }
                }

                // Безусловная изоляция служебных процедур __soft_atomic_ и __atomic_
                if (lineTrim.StartsWith("void __soft_atomic_", StringComparison.Ordinal) || lineTrim.StartsWith("void __atomic_", StringComparison.Ordinal))
                {
                    if (cleanLines.Count == 0 || cleanLines[cleanLines.Count - 1] != "#line 1 \"planning_c_internal.h\"")
                    {
                        cleanLines.Add("#line 1 \"planning_c_internal.h\"");
                    }
                    cleanLines.Add(line);
                    continue;
                }

                // Цикл диспетчера: при вызове следующей запланированной задачи возвращаем указатель на заголовок процедуры
                if (lineTrim.StartsWith("while (val__continue_plan__", StringComparison.Ordinal) || (lineTrim.StartsWith("_call_", StringComparison.Ordinal) && lineTrim.Contains("val__continue_plan__")))
                {
                    var mProc = DispatcherCallRegex.Match(lineTrim);
                    if (!mProc.Success) mProc = DispatcherEventsRegex.Match(lineTrim);
                    if (mProc != null && mProc.Success)
                    {
                        string pName = mProc.Groups[1].Value;
                        if (procStartLineMap.TryGetValue(pName, out int pLine))
                        {
                            cleanLines.Add($"#line {pLine} \"{normalizedSourceFile}\"");
                        }
                    }
                }

                // Внутри развернутых виртуальных макросов (например while_else) восстанавливаем директивы #line
                if (inVirtualMacro && srcLineMap.TryGetValue(lineTrim, out int mappedSourceLine))
                {
                    string expectedDirective = $"#line {mappedSourceLine} \"{normalizedSourceFile}\"";
                    if (cleanLines.Count == 0 || cleanLines[cleanLines.Count - 1] != expectedDirective)
                    {
                        cleanLines.Add(expectedDirective);
                    }
                    cleanLines.Add(line);
                    continue;
                }

                cleanLines.Add(line);
            }

            return cleanLines;
        }
    }
}

