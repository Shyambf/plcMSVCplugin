using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace plcMSVCplugin
{
    /// <summary>
    /// Configuration model mapped to syntax_rules.json.
    /// </summary>
    [DataContract]
    internal class SyntaxRulesConfig
    {
        [DataMember(Name = "keywords")]
        public List<string> Keywords { get; set; } = new List<string>();

        [DataMember(Name = "types")]
        public List<string> Types { get; set; } = new List<string>();

        [DataMember(Name = "preprocessor")]
        public List<string> Preprocessor { get; set; } = new List<string>();

        [DataMember(Name = "prologKeywords")]
        public List<string> PrologKeywords { get; set; } = new List<string>();

        [DataMember(Name = "russianTypes")]
        public List<string> RussianTypes { get; set; } = new List<string>();

        [DataMember(Name = "russianKeywords")]
        public List<string> RussianKeywords { get; set; } = new List<string>();
    }

    /// <summary>
    /// Loader for syntax_rules.json from VSIX output directory or assembly manifest.
    /// </summary>
    internal static class SyntaxRulesLoader
    {
        public static SyntaxRulesConfig Load()
        {
            // 1. Попытка прочитать файл рядом со сборкой в папке расширения
            try
            {
                string? assemblyLocation = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    string dir = Path.GetDirectoryName(assemblyLocation)!;
                    string filePath = Path.Combine(dir, "syntax_rules.json");
                    if (File.Exists(filePath))
                    {
                        using var stream = File.OpenRead(filePath);
                        var serializer = new DataContractJsonSerializer(typeof(SyntaxRulesConfig));
                        if (serializer.ReadObject(stream) is SyntaxRulesConfig cfg)
                        {
                            return cfg;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to embedded resource
            }

            // 2. Попытка прочитать из EmbeddedResource сборки
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var resStream = assembly.GetManifestResourceStream("plcMSVCplugin.syntax_rules.json");
                if (resStream != null)
                {
                    var serializer = new DataContractJsonSerializer(typeof(SyntaxRulesConfig));
                    if (serializer.ReadObject(resStream) is SyntaxRulesConfig cfg)
                    {
                        return cfg;
                    }
                }
            }
            catch
            {
                // Fallback to defaults
            }

            return GetDefaultFallbackConfig();
        }

        private static SyntaxRulesConfig GetDefaultFallbackConfig()
        {
            return new SyntaxRulesConfig
            {
                Keywords = new List<string> { "reenterable", "reentrant", "reent", "chain", "throw", "throw_stage", "plan_first", "plan_last", "plan_stop" },
                Types = new List<string> { "funnel", "cvar", "markupdef", "pre_id", "topology" }
            };
        }
    }

    /// <summary>
    /// MEF Classifier Provider for Planning C 2.0.
    /// </summary>
    [Export(typeof(IClassifierProvider))]
    [ContentType("C/C++")]
    [ContentType("code++.C++")]
    [ContentType("c")]
    [ContentType("cpp")]
    [ContentType("code")]
    [Order(After = Priority.High)]
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
    /// Tokens are loaded from syntax_rules.json.
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

        private static readonly Regex MasterTokenRegex = BuildRegex();

        private static Regex BuildRegex()
        {
            var config = SyntaxRulesLoader.Load();

            string preprocPattern = BuildPreprocPattern(config.Preprocessor);
            string ruTypePattern = BuildWordGroup("ru_type", config.RussianTypes);
            string ruKeywordPattern = BuildWordGroup("ru_keyword", config.RussianKeywords);
            string prologKeywordPattern = BuildWordGroup("prolog_keyword", config.PrologKeywords);
            string typePattern = BuildWordGroup("type", config.Types);
            string keywordPattern = BuildWordGroup("keyword", config.Keywords);

            string fullPattern =
                // 1. Комментарии
                @"(?<comment>//.*$|/\*[\s\S]*?\*/|%.*$)|" +

                // 2. Строки
                @"(?<string>""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*')|" +

                // 3. Мета-скобки РЯП #()
                @"(?<meta_paren>#\(|\))|" +

                // 4. Директивы (#...)
                preprocPattern +

                // 5. Правила Prolog (@rule)
                @"(?<prolog_rule>@[a-zA-Z0-9_]+)|" +

                // 6. Типы РЯП
                ruTypePattern +

                // 7. Действия РЯП
                ruKeywordPattern +

                // 8. Ключевые слова Prolog и XPath
                prologKeywordPattern +

                // 9. Типы данных, каналы и топологии
                typePattern +

                // 10. Ключевые слова планирования и C++
                keywordPattern +

                // 11. Операторы Prolog
                @"(?<prolog_op>:-|!|\|)|" +

                // 12. Числа
                @"(?<number>\b(?:0[xX][0-9a-fA-F]+|0[bB][01]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\b)|" +

                // 13. Операторы
                @"(?<operator><<|>>|->|::|&&|\|\||==|!=|<=|>=|\+\+|--|[+\-*/%=<>!&^~])";

            return new Regex(fullPattern, RegexOptions.Compiled | RegexOptions.Multiline);
        }

        private static string BuildWordGroup(string groupName, IEnumerable<string>? words)
        {
            if (words == null) return string.Empty;
            var list = words
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => Regex.Escape(w.Trim()))
                .ToList();

            if (list.Count == 0) return string.Empty;
            return $@"(?<{groupName}>\b(?:{string.Join("|", list)})\b)|";
        }

        private static string BuildPreprocPattern(IEnumerable<string>? directives)
        {
            if (directives == null) return string.Empty;
            var list = directives
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => Regex.Escape(w.Trim().TrimStart('#')))
                .ToList();

            if (list.Count == 0) return string.Empty;
            return $@"(?<preproc>#\s*(?:{string.Join("|", list)})\b)|";
        }

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
