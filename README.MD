This buildpack registers HttpModules supplied by any previously run buildpacks that are based on `CloudFoundry.Buildpack.V2.HttpModule` template with the ASP.NET app. It does intelligent app assembly version registration such that assemblies provided by modules do not conflict with app or other modules. This is done by loading multiple versions of same assembly side by side into app domain, allowing originally compiled code to use its original assembly, similar to node/npm dependency system.

This buildpack should appear after all the HttpModule buildpacks have run, followed by `hwc_buildpack` which should appear last.

#### Sample manifest


```yaml
applications:
- name: simpleapp
  stack: windows
  buildpacks: 
    - https://github.com/macsux/aspnet-logging-buildpack/releases/download/v0.1.0/AspNetLoggingBuildpack-win-x64-0.1.0.zip
    - https://github.com/macsux/http-module-loader-buildpack/releases/download/v0.1.0/HttpModuleLoaderBuildpack-win-x64-0.1.0.zip
    - hwc_buildpack
```

