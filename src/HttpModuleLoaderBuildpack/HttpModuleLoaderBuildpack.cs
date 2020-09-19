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
	        Console.WriteLine("==== HttpModule Loader ====");
            for (int i = 0; i < index; i++)
            {
                var buildpackDir = Path.Combine(depsPath, i.ToString());
                var httpModuleBuildpackManifest = Path.Combine(buildpackDir, ".httpModule");
                if (!File.Exists(httpModuleBuildpackManifest))
                {
	                continue;
                }
                
                var appBinDir =  (AbsolutePath)buildPath / "bin";
                var appLibsFolder = appBinDir / "libs";
                FileSystemTasks.CopyDirectoryRecursively(buildpackDir, appBinDir, DirectoryExistsPolicy.Merge, FileExistsPolicy.Skip);
                var moduleAssemblyFilename = File.ReadAllText(httpModuleBuildpackManifest);
                var moduleAssemblyFullPath = appBinDir / moduleAssemblyFilename;
                var moduleAssembly = Assembly.LoadFile(moduleAssemblyFullPath);
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
	                Console.WriteLine($"Installed {httpModule.AssemblyQualifiedName} http module");
                }

                
                // map side-by-side assembly loading from libs folder
                var libraryFiles = Directory.EnumerateFiles(appLibsFolder, "*.dll", SearchOption.AllDirectories).ToList();

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

                Console.WriteLine("Applying assembly loading information to web.config for following:");
                foreach (var file in libraryFiles)
                {
	                AssemblyName assemblyName = null;
	                try
	                {
		                assemblyName = Assembly.LoadFile(file).GetName();
	                }
	                catch (Exception)
	                {
		                Console.Error.WriteLine($"{file} is not a valid .NET assembly");
		                continue;
	                }
	                Console.WriteLine($"- {file}");
	                var assemblyPublicKeyToken = string.Join(string.Empty, Array.ConvertAll(assemblyName.GetPublicKeyToken(), x => x.ToString("X2"))).ToLower();
	                var publicKeyTokenSelector = !string.IsNullOrEmpty(assemblyPublicKeyToken) ? $" and @publicKeyToken='{assemblyPublicKeyToken}'" : string.Empty;
	                var cultureSelector = !string.IsNullOrEmpty(assemblyName.CultureName) ? $" and culture='{assemblyName.CultureName}'" : string.Empty;
	                var dependentAssemblyNode = assemblyBindingNode.SelectSingleNode($"ms:dependentAssembly[ms:assemblyIdentity/@name='{assemblyName.Name}'{publicKeyTokenSelector}{cultureSelector}]", ns);
	                if (dependentAssemblyNode == null)
	                {

		                dependentAssemblyNode = doc.CreateElement("dependentAssembly", msNamespace);
		                assemblyBindingNode.AppendChild(dependentAssemblyNode);
		                var assemblyIdentityNode = doc.CreateElement("assemblyIdentity", msNamespace);
		                if (!string.IsNullOrEmpty(assemblyPublicKeyToken))
		                {
			                assemblyIdentityNode.SetAttribute("publicKeyToken", string.Join(string.Empty, Array.ConvertAll(assemblyName.GetPublicKeyToken(), x => x.ToString("X2"))).ToLower());
		                }

		                if (!string.IsNullOrEmpty(assemblyName.CultureName))
		                {
			                assemblyIdentityNode.SetAttribute("culture", assemblyName.CultureName);
		                }

		                assemblyIdentityNode.SetAttribute("name", assemblyName.Name);
		                dependentAssemblyNode.AppendChild(assemblyIdentityNode);
		                
	                }
	                var codeBaseNode = (XmlElement) dependentAssemblyNode.SelectSingleNode($"ms:codeBase[@version='{assemblyName.Version}']", ns);
	                if (codeBaseNode == null)
	                {
		                codeBaseNode = doc.CreateElement("codeBase", msNamespace);
		                codeBaseNode.SetAttribute("version", assemblyName.Version.ToString());
		                codeBaseNode.SetAttribute("href", PathConstruction.GetRelativePath(buildPath, file));
		                dependentAssemblyNode.AppendChild(codeBaseNode);
	                }
                }

                doc.Save(webConfig);
                
            }
        }
    }
}
