// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Web.Administration
{
    using System.Collections.ObjectModel;
#if !__MonoCS__
    using System.Management.Automation;
#endif
    using System.Runtime.InteropServices;

    public sealed class Site : ConfigurationElement
    {
        private ApplicationCollection _collection;
        private BindingCollection _bindings;
        private SiteLogFile _logFile;
        private SiteLimits _limits;
        private SiteTraceFailedRequestsLogging _trace;
        private VirtualDirectoryDefaults _virtualDefaults;
        private ObjectState? _state;

        internal Site(SiteCollection parent)
            : this(null, parent)
        { }

        internal Site(ConfigurationElement element, SiteCollection parent)
            : base(element, "site", null, parent, null, null)
        {
            ApplicationDefaults = ChildElements["applicationDefaults"] == null
                ? new ApplicationDefaults(parent.ChildElements["applicationDefaults"], this)
                : new ApplicationDefaults(ChildElements["applicationDefaults"], this);
            Parent = parent;
            if (element == null)
            {
                return;
            }

            foreach (ConfigurationElement node in (ConfigurationElementCollection)element)
            {
                var app = new Application(node, Applications);
                Applications.InternalAdd(app);
            }
        }

        public ApplicationDefaults ApplicationDefaults { get; private set; }

        public ApplicationCollection Applications
        {
            get { return _collection ?? (_collection = new ApplicationCollection(this)); }
            internal set { _collection = value; }
        }

        public BindingCollection Bindings
        {
            get { return _bindings ?? (_bindings = new BindingCollection(ChildElements["bindings"], this)); }
            internal set { _bindings = value; }
        }

        public long Id
        {
            get { return (uint)this["id"]; }
            set { this["id"] = value; }
        }

        public SiteLimits Limits
        {
            get { return _limits ?? (_limits = new SiteLimits(ChildElements["limits"], this)); }
        }

        public SiteLogFile LogFile
        {
            get { return _logFile ?? (_logFile = new SiteLogFile(ChildElements["logFile"], this)); }
        }

        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }

        public bool ServerAutoStart
        {
            get { return (bool)this["serverAutoStart"]; }
            set { this["serverAutoStart"] = value; }
        }

        public ObjectState State
        {
            get
            {
                if (_state == null)
                {
                    var result = AsyncHelper.RunSync(GetStateAsync);
                    _state = result ? ObjectState.Started : ObjectState.Stopped;
                }

                return _state.Value;
            }

            private set
            {
                _state = value;
            }
        }

        public SiteTraceFailedRequestsLogging TraceFailedRequestsLogging
        {
            get { return _trace ?? (_trace = new SiteTraceFailedRequestsLogging(ChildElements["traceFailedRequestsLogging"], this)); }
        }

        public VirtualDirectoryDefaults VirtualDirectoryDefaults
        {
            get { return _virtualDefaults ?? (_virtualDefaults = new VirtualDirectoryDefaults(ChildElements["virtualDirectoryDefaults"], this)); }
        }

        internal SiteCollection Parent { get; private set; }

        public Configuration GetWebConfiguration()
        {
            foreach (Application app in Applications)
            {
                if (app.Path == Application.RootPath)
                {
                    return app.GetWebConfiguration();
                }
            }

            return new Configuration(new FileContext(Server, null, null, Name, true, true, true));
        }

        public ObjectState Start()
        {
            // TODO: add timeout.
            return AsyncHelper.RunSync(StartAsync);
        }

        public async Task<ObjectState> StartAsync()
        {
            State = ObjectState.Starting;
            if (Server.Mode == WorkingMode.IisExpress)
            {
                var name = Applications[0].ApplicationPoolName;
                var pool = Server.ApplicationPools.FirstOrDefault(item => item.Name == name);
                var fileName =
                    Path.Combine(
                        Environment.GetFolderPath(
                            pool != null && pool.Enable32BitAppOnWin64
                                ? Environment.SpecialFolder.ProgramFilesX86
                                : Environment.SpecialFolder.ProgramFiles),
                        "IIS Express",
                        "iisexpress.exe");
                if (!File.Exists(fileName))
                {
                    fileName = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "IIS Express",
                        "iisexpress.exe");
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = string.Format("/config:\"{0}\" /siteid:{1} /systray:false /trace:error", this.FileContext.FileName, Id),
                        WindowStyle = ProcessWindowStyle.Hidden,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                try
                {
                    process.Start();
                    process.WaitForExit(5000);
                    if (process.HasExited)
                    {
                        throw new InvalidOperationException("process terminated");
                    }

                    return ObjectState.Started;
                }
                catch (Exception ex)
                {
                    throw new COMException(
                        string.Format("cannot start site: {0}, {1}", ex.Message, process.StandardOutput.ReadToEnd()));
                }
                finally
                {
                    State = process.HasExited ? ObjectState.Stopped : ObjectState.Started;
                }
            }

            return ObjectState.Unknown;
        }

        public ObjectState Stop()
        {
            // TODO: add timeout.
            return AsyncHelper.RunSync(StopAsync);
        }

        public async Task<ObjectState> StopAsync()
        {
            State = ObjectState.Stopping;
            if (Server.Mode == WorkingMode.IisExpress)
            {
                var items = Process.GetProcessesByName("iisexpress");
                var found = items.Where(item =>
                    item.GetCommandLine().EndsWith(string.Format("/config:\"{0}\" /siteid:{1} /systray:false", this.FileContext.FileName, Id), StringComparison.Ordinal));
                foreach (var item in found)
                {
                    item.Kill();
                    item.WaitForExit();
                }

                return State = ObjectState.Stopped;
            }

            return ObjectState.Unknown;
        }

        public override string ToString()
        {
            return Name;
        }

        internal async Task RemoveApplicationsAsync()
        {
            foreach (Application application in Applications)
            {
                await application.RemoveAsync();
            }

            Applications = new ApplicationCollection(this);
        }

        internal ServerManager Server
        {
            get { return Parent.Parent; }
        }

        internal async Task<IEnumerable<DirectoryInfo>> GetPhysicalDirectoriesAsync()
        {
            if (Server.Mode != WorkingMode.Jexus)
            {
                var root = Applications[0].VirtualDirectories[0].PhysicalPath.ExpandIisExpressEnvironmentVariables();
                if (Directory.Exists(root))
                {
                    var result = new DirectoryInfo(root).GetDirectories();
                    return result;
                }

                return new DirectoryInfo[0];
            }

            return null;
        }

        internal async Task RestartAsync()
        {
            await StopAsync();
            await StartAsync();
        }

        internal async Task<bool> GetStateAsync()
        {
            if (Server.Mode == WorkingMode.IisExpress)
            {
                var items = Process.GetProcessesByName("iisexpress");
                var found = items.Where(item =>
                    item.GetCommandLine().EndsWith(string.Format("/siteid:{0} /systray:false", Id)));
                return found.Any();
            }

            if (this.Server.Mode == WorkingMode.Iis)
            {
#if !__MonoCS__
                using (PowerShell PowerShellInstance = PowerShell.Create())
                {
                    // use "AddScript" to add the contents of a script file to the end of the execution pipeline.
                    // use "AddCommand" to add individual commands/cmdlets to the end of the execution pipeline.
                    PowerShellInstance.AddScript("param($param1) [Reflection.Assembly]::LoadFrom('C:\\Windows\\system32\\inetsrv\\Microsoft.Web.Administration.dll'); Get-IISsite -Name \"$param1\"");

                    // use "AddParameter" to add a single parameter to the last command/script on the pipeline.
                    PowerShellInstance.AddParameter("param1", this.Name);

                    Collection<PSObject> PSOutput = PowerShellInstance.Invoke();

                    // check the other output streams (for example, the error stream)
                    if (PowerShellInstance.Streams.Error.Count > 0)
                    {
                        // error records were written to the error stream.
                        // do something with the items found.
                        return false;
                    }

                    dynamic site = PSOutput[1];
                    return site.State?.ToString() == "Started";
                }
#else
                return false;
#endif
            }

            return false;
        }

        internal async Task RemoveApplicationAsync(Application application)
        {
            Applications = await application.RemoveAsync();
        }

        internal void Save()
        {
            foreach (Application application in Applications)
            {
                application.Save();
            }
        }
    }
}
