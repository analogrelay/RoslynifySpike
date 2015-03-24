using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Framework.Runtime;

using RuntimeProject = Microsoft.Framework.Runtime.Project;

namespace RoslynifySpike
{
    public class Program
    {
        private readonly ILibraryManager _libraries;
        private readonly IApplicationEnvironment _env;
        private readonly IProjectResolver _projectResolver;

        public Program(ILibraryManager libraries, IApplicationEnvironment env, IProjectResolver projectResolver)
        {
            _libraries = libraries;
            _env = env;
            _projectResolver = projectResolver;
        }

        public void Main(string[] args)
        {
            // Get the export for the specified project
            var exports = _libraries.GetAllExports(_env.ApplicationName);
            var projectExport = exports.MetadataReferences.SingleOrDefault(m => m.Name.Equals(_env.ApplicationName)) as IMetadataProjectReference;
            var others = exports.MetadataReferences.Where(m => m != projectExport);

            RuntimeProject runtimeProject;
            if (!_projectResolver.TryResolveProject(_env.ApplicationName, out runtimeProject))
            {
                throw new Exception("Where the project at?");
            }

            // Collect the references
            var references = new List<MetadataReference>();
            foreach (var reference in others)
            {
                var converted = ConvertMetadataReference(reference);
                Console.WriteLine($"Using Reference: {converted.Display}");
                references.Add(converted);
            }

            // Create the project
            var runtimeSettings = ToCompilationSettings(runtimeProject.GetCompilerOptions(_env.RuntimeFramework, _env.Configuration), _env.RuntimeFramework);
            var prjId = ProjectId.CreateNewId();
            var projectInfo = ProjectInfo.Create(
                prjId,
                VersionStamp.Create(),
                runtimeProject.Name,
                runtimeProject.Name,
                runtimeProject.LanguageServices?.Name ?? "C#",
                compilationOptions: runtimeSettings.CompilationOptions,
                parseOptions: new CSharpParseOptions(
                    languageVersion: runtimeSettings.LanguageVersion,
                    preprocessorSymbols: runtimeSettings.Defines),
                documents: CreateDocuments(prjId, projectExport.GetSources()),
                metadataReferences: references);

            // Create the workspace
            var ws = new AdhocWorkspace();
            var project = ws.AddProject(projectInfo);
        }

        private IEnumerable<DocumentInfo> CreateDocuments(ProjectId prjId, IList<ISourceReference> list)
        {
            return list.OfType<ISourceFileReference>().Select(s => DocumentInfo.Create(
                DocumentId.CreateNewId(prjId),
                Path.GetFileName(s.Path),
                filePath: s.Path));
        }

        private MetadataReference ConvertMetadataReference(IMetadataReference metadataReference)
        {
            var embeddedReference = metadataReference as IMetadataEmbeddedReference;
            if (embeddedReference != null)
            {
                return MetadataReference.CreateFromImage(embeddedReference.Contents);
            }
            var fileMetadataReference = metadataReference as IMetadataFileReference;
            if (fileMetadataReference != null)
            {
                return MetadataReference.CreateFromFile(fileMetadataReference.Path);
            }
            var projectReference = metadataReference as IMetadataProjectReference;
            if (projectReference != null)
            {
                using (var ms = new MemoryStream())
                {
                    projectReference.EmitReferenceAssembly(ms);
                    return MetadataReference.CreateFromImage(ms.ToArray());
                }
            }
            throw new NotSupportedException();
        }

        private static CompilationSettings ToCompilationSettings(ICompilerOptions compilerOptions, FrameworkName targetFramework)
        {
            var options = GetCompilationOptions(compilerOptions);
            // Disable 1702 until roslyn turns this off by default
            options = options.WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
            {
                { "CS1702", ReportDiagnostic.Suppress },
                { "CS1705", ReportDiagnostic.Suppress }
            });
            AssemblyIdentityComparer assemblyIdentityComparer =
#if DNX451
IsDesktop(targetFramework) ?
            DesktopAssemblyIdentityComparer.Default :
#endif
null;
            options = options.WithAssemblyIdentityComparer(assemblyIdentityComparer);
            LanguageVersion languageVersion;
            if (!Enum.TryParse(value: compilerOptions.LanguageVersion, ignoreCase: true, result: out languageVersion))
            {
                languageVersion = LanguageVersion.CSharp6;
            }
            var settings = new CompilationSettings
            {
                LanguageVersion = languageVersion,
                Defines = compilerOptions.Defines ?? Enumerable.Empty<string>(),
                CompilationOptions = options
            };
            return settings;
        }

        private static CSharpCompilationOptions GetCompilationOptions(ICompilerOptions compilerOptions)
        {
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            string platformValue = compilerOptions.Platform;
            bool allowUnsafe = compilerOptions.AllowUnsafe ?? false;
            bool optimize = compilerOptions.Optimize ?? false;
            bool warningsAsErrors = compilerOptions.WarningsAsErrors ?? false;
            Platform platform;
            if (!Enum.TryParse(value: platformValue, ignoreCase: true, result: out platform))
            {
                platform = Platform.AnyCpu;
            }
            ReportDiagnostic warningOption = warningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default;
            return options.WithAllowUnsafe(allowUnsafe)
                .WithPlatform(platform)
                .WithGeneralDiagnosticOption(warningOption)
                .WithOptimizationLevel(optimize ? OptimizationLevel.Release : OptimizationLevel.Debug);
        }
        private static bool IsDesktop(FrameworkName frameworkName)
        {
            return frameworkName.Identifier == ".NETFramework" ||
            frameworkName.Identifier == "Asp.Net" ||
            frameworkName.Identifier == "DNX";
        }
    }
}
