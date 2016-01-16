// Copyright (c) Lex Li. All rights reserved.
// 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Web.Administration
{
    public sealed class SiteLogFile : ConfigurationElement
    {
        private CustomLogFieldCollection _collection;

        internal SiteLogFile(ConfigurationElement parent)
            : this(null, parent)
        { }

        internal SiteLogFile(ConfigurationElement element, ConfigurationElement parent)
            : base(element, "logFile", null, parent, null, null)
        {
        }

        public CustomLogFieldCollection CustomLogFields
        {
            get { return _collection ?? (_collection = this.Schema.AttributeSchemas["customFields"] == null ? null : new CustomLogFieldCollection(ChildElements["customFields"], this)); }
        }

        public Guid CustomLogPluginClsid
        {
            get
            {
                Guid guid;
                Guid.TryParse(this["customLogPluginClsid"].ToString(), out guid);
                return guid;
            }

            set
            {
                this["customLogPluginClsid"] = value.ToString();
            }
        }
        public string Directory
        {
            get { return (string)this["directory"]; }
            set { this["directory"] = value; }
        }

        public bool Enabled
        {
            get { return (bool)this["enabled"]; }
            set { this["enabled"] = value; }
        }

        public bool LocalTimeRollover
        {
            get { return (bool)this["localTimeRollover"]; }
            set { this["localTimeRollover"] = value; }
        }

        public LogExtFileFlags LogExtFileFlags
        {
            get { return (LogExtFileFlags)Enum.ToObject(typeof(LogExtFileFlags), this["logExtFileFlags"]); }
            set { this["logExtFileFlags"] = value; }
        }

        public LogFormat LogFormat
        {
            get { return (LogFormat)Enum.ToObject(typeof(LogFormat), this["logFormat"]); }
            set { this["logFormat"] = (long)value; }
        }

        public LogTargetW3C LogTargetW3C
        {
            get { return (LogTargetW3C)Enum.ToObject(typeof(LogTargetW3C), this["logTargetW3C"]); }
            set { this["logTargetW3C"] = (long)value; }
        }

        public LoggingRolloverPeriod Period
        {
            get { return (LoggingRolloverPeriod)Enum.ToObject(typeof(LoggingRolloverPeriod), this["period"]); }
            set { this["period"] = (long)value; }
        }

        public long TruncateSize
        {
            get { return (long)this["truncateSize"]; }
            set { this["truncateSize"] = value; }
        }
    }
}
