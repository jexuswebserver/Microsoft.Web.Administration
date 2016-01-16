// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Web.Administration.Properties;

namespace Microsoft.Web.Administration
{
    public class ServerManager
    {
        private Configuration _applicationHost;

        private Configuration _cleanHost;

        internal bool Initialized;

        private SiteDefaults _siteDefaults;

        internal SiteCollection SiteCollection;

        private VirtualDirectoryDefaults _virtualDirectoryDefaults;

        private WorkerProcessCollection _workerProcessCollection;

        private ApplicationDefaults _applicationDefaults;

        private ApplicationPoolDefaults _applicationPoolDefaults;

        internal ApplicationPoolCollection ApplicationPoolCollection;

        internal string LogFolder { get; set; }

        internal string HostName { get; }

        internal string Name { get; set; }

        internal string Credentials { get; set; }

        internal string Certificate { get; set; }

        internal string KeyFile { get; set; }

        internal string Title
        {
            get
            {
                return string.IsNullOrEmpty(HostName)
                           ? (string.IsNullOrEmpty(Name) ? "UNKNOWN" : Name)
                           : HostName.ExtractName();
            }
        }

        internal WorkingMode Mode { get; set; }

        public ServerManager()
            : this(null, true)
        {
        }

        public ServerManager(string hostName, bool local)
            : this(hostName, null)
        {
        }

        internal ServerManager(string hostName, string credentials)
            : this(hostName, credentials, false, null)
        {
        }

        public ServerManager(bool readOnly, string applicationHostConfigurationPath)
            : this("localhost", null, readOnly, applicationHostConfigurationPath)
        {
        }

        public ServerManager(string applicationHostConfigurationPath)
            : this(false, applicationHostConfigurationPath)
        {
        }

        internal ServerManager(string hostName, string credentials, bool readOnly, string fileName)
        {
            HostName = hostName;
            Credentials = credentials;
            ReadOnly = readOnly;
            FileName = fileName;
        }

        private void Initialize()
        {
            if (this.Initialized)
            {
                return;
            }

            this.Initialized = true;
            var machineConfig = Helper.IsRunningOnMono()
                ? "/Library/Frameworks/Mono.framework/Versions/Current/etc/mono/4.5/machine.config"
                : Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                    "Microsoft.NET",
                                    IntPtr.Size == 2 ? "Framework" : "Framework64",
                                    "v4.0.30319",
                                    "config",
                                    "machine.config");
            var machine =
                new Configuration(
                    new FileContext(
                        this,
                        machineConfig,
                        null,
                        null,
                        false,
                        true,
                        true));
            var webConfig = Helper.IsRunningOnMono()
                ? "/Library/Frameworks/Mono.framework/Versions/Current/etc/mono/4.5/web.config"
                : Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                "Microsoft.NET",
                                IntPtr.Size == 2 ? "Framework" : "Framework64",
                                "v4.0.30319",
                                "config",
                                "web.config");
            var web =
                new Configuration(
                    new FileContext(
                        this,
                        webConfig,
                        machine.FileContext,
                        null,
                        false,
                        true,
                        true));

            this.CreateCache();
            _applicationHost =
                new Configuration(
                new FileContext(this, this.FileName, web.FileContext, null, true, false, this.ReadOnly));

            this.LoadCache();

            var poolSection = _applicationHost.GetSection("system.applicationHost/applicationPools");
            _applicationPoolDefaults =
                new ApplicationPoolDefaults(poolSection.GetChildElement("applicationPoolDefaults"), poolSection);
            this.ApplicationPoolCollection = new ApplicationPoolCollection(poolSection, this);
            var siteSection = _applicationHost.GetSection("system.applicationHost/sites");
            _siteDefaults = new SiteDefaults(siteSection.GetChildElement("siteDefaults"), siteSection);
            _applicationDefaults = new ApplicationDefaults(
                siteSection.GetChildElement("applicationDefaults"),
                siteSection);
            _virtualDirectoryDefaults =
                new VirtualDirectoryDefaults(siteSection.GetChildElement("virtualDirectoryDefaults"), siteSection);
            this.SiteCollection = new SiteCollection(siteSection, this);

            if (this.Mode == WorkingMode.Jexus)
            {
                AsyncHelper.RunSync(this.LoadAsync);
            }
        }

        private void LoadCache()
        {
            // IMPORTANT: force to reload clean elements from file.
            _cleanHost = null;
            _cleanHost =
                new Configuration(
                new FileContext(this, this.FileName, _applicationHost.FileContext.Parent, null, true, false, false));
        }

        public void Dispose()
        {
        }

        public void CommitChanges()
        {
            AsyncHelper.RunSync(CommitChangesAsync);
        }

        public async Task CommitChangesAsync()
        {
            foreach (Site site in Sites)
            {
                foreach (Application application in site.Applications)
                {
                    application.Save();
                }
            }

            Save();

            if (this.Mode != WorkingMode.Jexus)
            {
                return;
            }

            foreach (Site site in Sites)
            {
                foreach (Application application in site.Applications)
                {
                    await application.SaveAsync();
                }
            }

            await this.SaveAsync();
        }

        public void Save()
        {
            _applicationHost.FileContext.Save();
            _applicationHost.OnCacheInvalidated();

            LoadCache();
        }

        public ApplicationDefaults ApplicationDefaults
        {
            get
            {
                if (_applicationDefaults != null)
                {
                    return _applicationDefaults;
                }

                this.Initialize();
                return _applicationDefaults;
            }
        }

        public ApplicationPoolDefaults ApplicationPoolDefaults
        {
            get
            {
                if (_applicationPoolDefaults != null)
                {
                    return _applicationPoolDefaults;
                }

                this.Initialize();
                return _applicationPoolDefaults;
            }
        }

        public ApplicationPoolCollection ApplicationPools
        {
            get
            {
                if (this.ApplicationPoolCollection != null)
                {
                    return this.ApplicationPoolCollection;
                }

                this.Initialize();
                return this.ApplicationPoolCollection;
            }
        }

        public SiteDefaults SiteDefaults
        {
            get
            {
                if (_siteDefaults != null)
                {
                    return _siteDefaults;
                }

                this.Initialize();
                return _siteDefaults;
            }
        }

        public SiteCollection Sites
        {
            get
            {
                if (this.SiteCollection != null)
                {
                    return this.SiteCollection;
                }

                this.Initialize();
                return this.SiteCollection;
            }
        }

        public VirtualDirectoryDefaults VirtualDirectoryDefaults
        {
            get
            {
                if (_virtualDirectoryDefaults != null)
                {
                    return _virtualDirectoryDefaults;
                }

                this.Initialize();
                return _virtualDirectoryDefaults;
            }
        }

        public WorkerProcessCollection WorkerProcesses
        {
            get
            {
                this.Initialize();
                return _workerProcessCollection;
            }
        }

        public object Status { get; internal set; }

        internal bool IsLocalhost { get; set; }

        internal bool ReadOnly { get; }

        internal string FileName { get; set; }

        internal SortedDictionary<string, List<string>> Extra { get; set; }

        internal string SiteFolder { get; set; }

        public Configuration GetAdministrationConfiguration()
        {
            return null;
        }

        public Configuration GetAdministrationConfiguration(WebConfigurationMap configMap, string configurationPath)
        {
            return null;
        }

        public Configuration GetApplicationHostConfiguration()
        {
            this.Initialize();
            return _applicationHost;
        }

        internal Configuration GetConfigurationCache()
        {
            this.Initialize();
            return _cleanHost;
        }

        public object GetMetadata(string metadataType)
        {
            return null;
        }

        public Configuration GetRedirectionConfiguration()
        {
            return null;
        }

        public Configuration GetWebConfiguration(string siteName)
        {
            return null;
        }

        public Configuration GetWebConfiguration(string siteName, string virtualPath)
        {
            return null;
        }

        public Configuration GetWebConfiguration(WebConfigurationMap configMap, string configurationPath)
        {
            return null;
        }

        public static ServerManager OpenRemote(string serverName)
        {
            return new ServerManager(serverName, false);
        }

        public void SetMetadata(string metadataType, object value)
        {
        }

        internal void VerifyLocation(string locationPath)
        {
            if (locationPath == null)
            {
                return;
            }

            // TODO: add deeper level check
            var parts = locationPath.Split('/');
            if (parts[0] != string.Empty && Sites.All(site => site.Name != parts[0]))
            {
                throw new FileNotFoundException(
                    string.Format(
                        "Filename: \r\nError: Unrecognized configuration path 'MACHINE/WEBROOT/APPHOST/{0}'\r\n\r\n",
                        parts[0]));
            }
        }

        private void CreateCache()
        {
            if (this.Mode != WorkingMode.Jexus)
            {
                return;
            }

            var name = this.HostName.Replace(':', '_');
            CacheFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Jexus Manager",
                "cache",
                name);
            FileName = Path.Combine(CacheFolder, "applicationHost.config");
            if (!Directory.Exists(CacheFolder))
            {
                Directory.CreateDirectory(CacheFolder);
            }

            File.WriteAllText(this.FileName, Resources.original);
        }

        internal string CacheFolder { get; set; }
        internal string Type { get; private set; }
    }
}
