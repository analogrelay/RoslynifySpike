using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;

namespace RoslynifySpike
{
    internal class CompilationSettings
    {
        public CSharpCompilationOptions CompilationOptions { get; set; }
        public IEnumerable<string> Defines { get; set; }
        public LanguageVersion LanguageVersion { get; set; }
    }
}