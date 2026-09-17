using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace plcMSVCplugin
{
    /// <summary>
    /// MEF Classifier Provider for Planning C 2.0.
    /// </summary>
    [Export(typeof(IClassifierProvider))]
    [ContentType("C/C++")]
    [ContentType("code++.C++")]
    [ContentType("c")]
    [ContentType("cpp")]
    [ContentType("code")]
    internal class PlanningCClassifierProvider : IClassifierProvider
    {
        [Import]
        private IClassificationTypeRegistryService classificationRegistry = null!;

        public IClassifier GetClassifier(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(() =>
                new PlanningCClassifier(classificationRegistry));
        }
    }

    /// <summary>
    /// Planning C 2.0 syntax classifier for standard C/C++ files.
    /// </summary>
    internal class PlanningCClassifier : IClassifier
    {
        private readonly IClassificationType keywordType;
        private readonly IClassificationType commentType;
        private readonly IClassificationType stringType;
        private readonly IClassificationType numberType;
        private readonly IClassificationType preprocessorType;
        private readonly IClassificationType typeType;
        private readonly IClassificationType operatorType;

        // Регулярное выражение синтаксиса Planning C 2.0
        private static readonly Regex MasterTokenRegex = new Regex(
            // 1. Комментарии
            @"(?<comment>//.*$|/\*[\s\S]*?\*/|%.*$)|" +

            // 2. Строки
            @"(?<string>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')|" +

            // 3. Мета-скобки РЯП #()
            @"(?<meta_paren>#\(|\))|" +

            // 4. Директивы (#...)
            @"(?<preproc>#\s*(?:" +
            @"def_pattern|def_module|def_xpath|add_scan|scan|add_parse|parse|introduce|stop|preproc_passes|" +
            @"pragma|include|define|undef|ifdef|ifndef|if|else|elif|endif|line|error" +
            @")\b)|" +

            // 5. Правила Prolog (@rule)
            @"(?<prolog_rule>@[a-zA-Z0-9_]+)|" +

            // 6. Типы РЯП (бирюзовый)
            @"(?<ru_type>\b(?:" +
            @"вектор|вектора|вектору|вектором|векторе|векторы|" +
            @"скаляр|скаляра|скаляру|скаляром|скаляре|скаляры|" +
            @"матрица|матрицы|матрице|матрицу|матрицей|матрицах|" +
            @"элемент|элемента|элементу|элементом|элементе|элементы|элементов|элементам|элементами" +
            @")\b)|" +

            // 7. Действия РЯП (синий)
            @"(?<ru_keyword>\b(?:" +
            @"Составить|составить|программу|программа|" +
            @"Написать|написать|Определить|определить|посчитать|" +
            @"Ввести|ввести|Введем|введем|Зададим|зададим|" +
            @"Найдем|найдем|поместим|результат|также|минимум|максимум|" +
            @"среднее|арифметическое|Вывести|вывести|экран|клавиатуры|" +
            @"считать|будем|не|Тест|тест|дает" +
            @")\b)|" +

            // 8. Ключевые слова Prolog и XPath
            @"(?<prolog_keyword>\b(?:" +
            @"is|fail|true|repeat|append|try_link|getidx|hyper|member|length|" +
            @"write|nl|once|atom_concat|number_atom|retractall|assertz|predicate_property|" +
            @"xpathf|similar|eq|gid|brackets_off|prefix_off|prune" +
            @")\b)|" +

            // 9. Типы данных, каналы и топологии
            @"(?<type>\b(?:" +
            // Каналы и семафоры
            @"funnel|cfunnel|cvar|cvar_clique|csem|csem_clique|plan_csem_t|input_proc|empty_proc|in|out|" +
            // Типы и структуры
            @"plan_type|plan_item_type|int_array|float_array|double_array|char_array|" +
            @"markupdef|pre_id|state|depends|predicate|" +
            // Топологии
            @"topology|hypercube|torus|grid|tree|sun|ring|clique|cube|pyramide|lens|margueritte|" +
            @"MGUA_X1X2|TModelMGUA|__Predictor|__FeedForwardNet|" +
            // Стандартные типы C++
            @"int|char|bool|float|double|void|short|long|unsigned|signed|size_t|function|" +
            @"uint8_t|uint16_t|uint32_t|uint64_t|int8_t|int16_t|int32_t|int64_t|" +
            @"string|vector|map|set|pair|tuple|iostream|ostream|istream|NodeTag|Node" +
            @")\b)|" +

            // 10. Ключевые слова планирования и C++
            @"(?<keyword>\b(?:" +
            // Конвейеры и план
            @"reenterable|reentrant|reent|chain|throw|plan_chain|plan_parallel_chain|plan_parallel_reverse|" +
            @"throw_first|throw_last|reent_next_first|reent_next_last|plan_stop|clear_plan|clear_reent|_Yield|" +
            @"plan_first|plan_last|plan_get_first|plan_get_last|plan_empty|" +
            @"plan_group_first|plan_group_last|plan_group_parallelize|plan_group_vectorize|plan_group_typize|plan_group_atomize|plan_group_soft_atomize|" +
            @"plan_register|plan_registered_barrier|plan_critical|plan_topology|plan_topology_quit|" +
            @"plan_linear_num|plan_topology_num|plan_neighbours|plan_processor_id|plan_processors|" +
            @"reent_first|reent_last|reent_group_first|reent_group_last|reent_group_parallelize|reent_group_soft_atomize|" +
            @"reent_registered_barrier|reent_topology_quit|reent_linear_num|reent_topology_num|reent_neighbours|" +
            @"reent_processor_id|reent_processors|reent_num_stages|reent_stage|" +
            @"master|forw|input_index|set|reset|first|last|" +
            // Параллелизм и Cilk++
            @"cilk_spawn|cilk_sync|cilk_for|_Cilk_spawn|_Cilk_sync|_Cilk_for|" +
            @"clustered|vectorized|typized|gpu|parallelize|sequential|parallel|reduction|" +
            @"split_private|split_shared|split_tune|transaction_atomic|transacted|predict_|memoization|" +
            @"goal|PARALLEL|SEQUENTIAL|" +
            // Ключевые слова C++
            @"auto|break|case|catch|class|const|continue|default|delete|do|else|enum|explicit|export|extern|for|friend|goto|if|inline|mutable|namespace|new|noexcept|nullptr|operator|private|protected|public|register|reinterpret_cast|return|sizeof|static|static_assert|static_cast|struct|switch|template|this|true|false|try|typedef|typeid|typename|union|using|virtual|volatile|while" +
            @")\b)|" +

            // 11. Операторы Prolog
            @"(?<prolog_op>:-|!|\|)|" +

            // 12. Числа
            @"(?<number>\b(?:0[xX][0-9a-fA-F]+|0[bB][01]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\b)|" +

            // 13. Операторы
            @"(?<operator><<|>>|->|::|&&|\|\||==|!=|<=|>=|\+\+|--|[+\-*/%=<>!&^~])",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public PlanningCClassifier(IClassificationTypeRegistryService registry)
        {
            keywordType = registry.GetClassificationType(PredefinedClassificationTypeNames.Keyword);
            commentType = registry.GetClassificationType(PredefinedClassificationTypeNames.Comment);
            stringType = registry.GetClassificationType(PredefinedClassificationTypeNames.String);
            numberType = registry.GetClassificationType(PredefinedClassificationTypeNames.Number);
            preprocessorType = registry.GetClassificationType(PredefinedClassificationTypeNames.Keyword);
            typeType = registry.GetClassificationType(PredefinedClassificationTypeNames.Type);
            operatorType = registry.GetClassificationType(PredefinedClassificationTypeNames.Operator);
        }

        public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged
        {
            add { }
            remove { }
        }

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            var classifications = new List<ClassificationSpan>();
            string text = span.GetText();
            int spanStart = span.Start.Position;

            var matches = MasterTokenRegex.Matches(text);
            foreach (Match match in matches)
            {
                IClassificationType? type = null;

                if (match.Groups["comment"].Success)
                {
                    type = commentType;
                }
                else if (match.Groups["string"].Success)
                {
                    type = stringType;
                }
                else if (match.Groups["ru_type"].Success)
                {
                    type = typeType;
                }
                else if (match.Groups["ru_keyword"].Success)
                {
                    type = keywordType;
                }
                else if (match.Groups["meta_paren"].Success)
                {
                    type = operatorType;
                }
                else if (match.Groups["preproc"].Success)
                {
                    type = preprocessorType;
                }
                else if (match.Groups["prolog_rule"].Success)
                {
                    type = keywordType;
                }
                else if (match.Groups["prolog_keyword"].Success)
                {
                    type = keywordType;
                }
                else if (match.Groups["prolog_op"].Success)
                {
                    type = operatorType;
                }
                else if (match.Groups["type"].Success)
                {
                    type = typeType;
                }
                else if (match.Groups["keyword"].Success)
                {
                    type = keywordType;
                }
                else if (match.Groups["number"].Success)
                {
                    type = numberType;
                }
                else if (match.Groups["operator"].Success)
                {
                    type = operatorType;
                }

                if (type != null)
                {
                    var tokenSpan = new SnapshotSpan(span.Snapshot, spanStart + match.Index, match.Length);
                    classifications.Add(new ClassificationSpan(tokenSpan, type));
                }
            }

            return classifications;
        }
    }
}
