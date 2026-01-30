using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface ITranslator
{
    Task<string> TranslateAsync(string text, string targetLanguage);
    Task<List<string>> TranslateBulkAsync(List<string> texts, string targetLanguage);
}
