using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Xml;
using Microsoft.VisualBasic;
using Newtonsoft.Json.Linq;
using Nuke.Common.IO;

namespace HttpModuleLoaderBuildpack
{
    public class HttpModuleLoaderBuildpack : SupplyBuildpack 
    {
        protected override void Apply(string buildPath, string cachePath, string depsPath, int index)
        {
            for (int i = 0; i < index; i++)
            {
                var buildpackDir = Path.Combine(depsPath, i.ToString());
                var httpModuleBuildpackManifest = Path.Combine(buildpackDir, ".httpModule");
                if (!File.Exists(httpModuleBuildpackManifest))
                {
	                continue;
                }
                FileSystemTasks.CopyDirectoryRecursively(buildpackDir, Path.Combine(buildPath, "bin"), DirectoryExistsPolicy.Merge, FileExistsPolicy.Skip);
                var moduleAssemblyFilename = File.ReadAllText(httpModuleBuildpackManifest);
                var moduleAssembly = Assembly.LoadFile(Path.Combine(buildPath, "bin", moduleAssemblyFilename));
                var httpModuleTypes = moduleAssembly.ExportedTypes.Where(x => typeof(IHttpModule).IsAssignableFrom(x));

                var doc = new XmlDocument();
                var nav = doc.CreateNavigator();
                var ns = new XmlNamespaceManager(nav.NameTable);
                var msNamespace = "urn:schemas-microsoft-com:asm.v1";
                ns.AddNamespace("ms", msNamespace);
                var webConfig = Path.Combine(buildPath, "web.config");
                doc.Load(webConfig);
                var webServerNode = doc.DocumentElement["system.webServer"];
                if (webServerNode == null)
                {
	                webServerNode = doc.CreateElement("system.webServer");
	                doc.DocumentElement.AppendChild(webServerNode);
                }
                var modulesNode = webServerNode["modules"];
                if (modulesNode == null)
                {
	                modulesNode = doc.CreateElement("modules");
	                webServerNode.AppendChild(modulesNode);
                }

                // install http module
                foreach (var httpModule in httpModuleTypes)
                {
	                var httpModuleNode = doc.CreateElement("add");
	                httpModuleNode.SetAttribute("name", httpModule.Name);
	                httpModuleNode.SetAttribute("type", httpModule.AssemblyQualifiedName);
	                modulesNode.AppendChild(httpModuleNode);
                }

                
                // map side-by-side assembly loading from libs folder
                var assetsFile = Path.Combine(buildpackDir, "project.assets.json");
                if (File.Exists(assetsFile))
                {
	                var assetsDoc = JObject.Parse(File.ReadAllText(assetsFile));

	                var referenceAssemblies = assetsDoc["targets"][".NETFramework,Version=v4.7.2"]
		                .Cast<JProperty>()
		                .Where(x => x.Value["type"].ToString() == "package")
		                .Select(item =>
		                {
			                var assemblyNameAndVersion = item.Name;
			                var parts = assemblyNameAndVersion.Split('/');
			                var assemblyName = parts[0];
			                var version = parts[1];
			                var srcFolder = assetsDoc["libraries"][assemblyNameAndVersion]["path"].ToString();
			                return new
			                {
				                AssemblyName = assemblyName,
				                Version = version,
				                Files = ((JObject) item.Value["runtime"]).Properties().Select(x => Path.Combine(srcFolder, x.Name).Replace('/', Path.DirectorySeparatorChar)),
			                };
		                });

	                // var nugetCache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

	                var root = doc.SelectSingleNode("/configuration");
	                var runtimeNode = root["runtime"];
	                if (runtimeNode == null)
	                {
		                runtimeNode = doc.CreateElement("runtime");
		                root.AppendChild(runtimeNode);
	                }

	                var assemblyBindingNode = runtimeNode["assemblyBinding"];
	                if (assemblyBindingNode == null)
	                {
		                assemblyBindingNode = doc.CreateElement("assemblyBinding", msNamespace);
		                runtimeNode.AppendChild(assemblyBindingNode);
	                }

	                foreach (var assembly in referenceAssemblies)
	                {
		                // foreach (var file in assembly.Files)
		                // {
			               //  FileSystemTasks.CopyFile(Path.Combine(nugetCache, file), Path.Combine(@"C:\projects\HarmonyLoader\SampleApp\bin\libs", file), FileExistsPolicy.OverwriteIfNewer);
		                // }

		                // var assemblyDll = assembly.Files.FirstOrDefault(x => Path.GetFileNameWithoutExtension(x) == assembly.AssemblyName) ??
		                //                   assembly.Files.Single(x => Path.GetExtension(x) == ".dll");

		                var dependentAssemblyNode = assemblyBindingNode.SelectSingleNode($"ms:dependentAssembly[ms:assemblyIdentity/@name='{assembly.AssemblyName}']", ns);
		                if (dependentAssemblyNode == null)
		                {
			                var assemblyName = Assembly.LoadFile(Path.Combine(buildPath, "bin", "libs", moduleAssemblyFilename)).GetName();

			                dependentAssemblyNode = doc.CreateElement("dependentAssembly", msNamespace);
			                assemblyBindingNode.AppendChild(dependentAssemblyNode);
			                var assemblyIdentityNode = doc.CreateElement("assemblyIdentity", msNamespace);
			                if (assemblyName.GetPublicKeyToken().Any())
			                {
				                assemblyIdentityNode.SetAttribute("publicKeyToken", string.Join(string.Empty, Array.ConvertAll(assemblyName.GetPublicKeyToken(), x => x.ToString("X2"))).ToLower());
			                }

			                assemblyIdentityNode.SetAttribute("name", assembly.AssemblyName);
			                dependentAssemblyNode.AppendChild(assemblyIdentityNode);
		                }

		                var codeBaseNode = (XmlElement) dependentAssemblyNode.SelectSingleNode($"ms:codeBase[@version='{assembly.Version}']", ns);
		                if (codeBaseNode == null)
		                {
			                codeBaseNode = doc.CreateElement("codeBase", msNamespace);
			                codeBaseNode.SetAttribute("version", $"{assembly.Version}.0");
			                codeBaseNode.SetAttribute("href", Path.Combine("bin", "libs", moduleAssemblyFilename));
			                dependentAssemblyNode.AppendChild(codeBaseNode);
		                }
	                }

	                doc.Save(webConfig);
                }
            }
        }
    }
}
