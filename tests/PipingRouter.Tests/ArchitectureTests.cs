using System.Xml.Linq;
using Xunit;

namespace PipingRouter.Tests;

/// <summary>守住相依方向：Engine 不可依賴 UI／Windows／其他專案；Tests 只參考 Engine。</summary>
public class ArchitectureTests
{
    private static XDocument LoadCsproj(params string[] relative) =>
        XDocument.Load(RepoPaths.Combine(relative));

    [Fact]
    public void Engine_HasNoProjectOrPackageReferences()
    {
        var doc = LoadCsproj("src", "PipingRouter.Engine", "PipingRouter.Engine.csproj");
        Assert.Empty(doc.Descendants("ProjectReference"));
        Assert.Empty(doc.Descendants("PackageReference"));
        Assert.Empty(doc.Descendants("FrameworkReference"));
    }

    [Fact]
    public void Engine_UsesPlainSdk_NotWebOrWindowsTarget()
    {
        var doc = LoadCsproj("src", "PipingRouter.Engine", "PipingRouter.Engine.csproj");
        Assert.Equal("Microsoft.NET.Sdk", doc.Root!.Attribute("Sdk")!.Value);
        Assert.DoesNotContain(doc.Descendants("TargetFramework"),
            e => e.Value.Contains("windows", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(doc.Descendants("UseWPF"));
        Assert.Empty(doc.Descendants("UseWindowsForms"));
    }

    [Fact]
    public void Engine_SourcesDoNotMentionForbiddenNamespaces()
    {
        string[] forbidden =
        {
            "Microsoft.AspNetCore", "Microsoft.JSInterop", "System.Windows",
            "System.Drawing", "Microsoft.Win32", "System.Web"
        };
        var srcDir = RepoPaths.Combine("src", "PipingRouter.Engine");
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.Combine("obj", "")) || file.Contains(Path.Combine("bin", ""))) continue;
            var text = File.ReadAllText(file);
            foreach (var ns in forbidden)
                Assert.False(text.Contains(ns, StringComparison.Ordinal), $"{file} 含禁用命名空間 {ns}");
        }
    }

    [Fact]
    public void Tests_ReferenceOnlyEngine()
    {
        var doc = LoadCsproj("tests", "PipingRouter.Tests", "PipingRouter.Tests.csproj");
        var refs = doc.Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();
        Assert.Equal(new[] { "PipingRouter.Engine" }, refs);
    }

    [Fact]
    public void Solution_DoesNotYetContainDashboard()
    {
        var sln = File.ReadAllText(RepoPaths.Combine("PipingAutoRouter.sln"));
        Assert.Contains("PipingRouter.Engine", sln);
        Assert.Contains("PipingRouter.Tests", sln);
        Assert.DoesNotContain("PipingRouter.Dashboard", sln);
    }
}
