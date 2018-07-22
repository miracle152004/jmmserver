﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Nancy;
using Pri.LongPath;
using Shoko.Commons;
using Shoko.Models.Server;
using Shoko.Server.API.v2.Models.core;

namespace Shoko.Server.API.v2.Modules
{
    public class Version : NancyModule
    {
        public Version()
        {
            Get["/api/version", true] = async (x,ct) => await Task.Factory.StartNew(GetVersion, ct);
        }

        /// <summary>
        /// Return current version of ShokoServer and several modules
        /// </summary>
        /// <returns></returns>
        private object GetVersion()
        {
            List<ComponentVersion> list = new List<ComponentVersion>();

            ComponentVersion version = new ComponentVersion
            {
                version = Utils.GetApplicationVersion(),
                name = "server"
            };
            list.Add(version);

            string versionExtra = Utils.GetApplicationExtraVersion();

            if (!string.IsNullOrEmpty(versionExtra))
            {
                version = new ComponentVersion
                {
                    version = versionExtra,
                    name = "servercommit"
                };
                list.Add(version);
            }

            version = new ComponentVersion
            {
                version = Assembly.GetAssembly(typeof(FolderMappings)).GetName().Version.ToString(),
                name = "commons"
            };
            list.Add(version);

            version = new ComponentVersion
            {
                version = Assembly.GetAssembly(typeof(AniDB_Anime)).GetName().Version.ToString(),
                name = "models"
            };
            list.Add(version);

            version = new ComponentVersion
            {
                version = Assembly.GetAssembly(typeof(INancyModule)).GetName().Version.ToString(),
                name = "Nancy"
            };
            list.Add(version);

            string dllpath = Assembly.GetEntryAssembly().Location;
            dllpath = Path.GetDirectoryName(dllpath);
            dllpath = Path.Combine(dllpath, "x86");
            dllpath = Path.Combine(dllpath, "MediaInfo.dll");

            if (File.Exists(dllpath))
            {
                version = new ComponentVersion
                {
                    version = FileVersionInfo.GetVersionInfo(dllpath).FileVersion,
                    name = "MediaInfo"
                };
                list.Add(version);
            }
            else
            {
                dllpath = Assembly.GetEntryAssembly().Location;
                dllpath = Path.GetDirectoryName(dllpath);
                dllpath = Path.Combine(dllpath, "x64");
                dllpath = Path.Combine(dllpath, "MediaInfo.dll");
                if (File.Exists(dllpath))
                {
                    version = new ComponentVersion
                    {
                        version = FileVersionInfo.GetVersionInfo(dllpath).FileVersion,
                        name = "MediaInfo"
                    };
                    list.Add(version);
                }
                else
                {
                    version = new ComponentVersion
                    {
                        version = @"DLL not found, using internal",
                        name = "MediaInfo"
                    };
                    list.Add(version);
                }
            }

            if (File.Exists("webui//index.ver"))
            {
                string webui_version = File.ReadAllText("webui//index.ver");
                string[] versions = webui_version.Split('>');
                if (versions.Length == 2)
                {
                    version = new ComponentVersion
                    {
                        name = "webui/" + versions[0],
                        version = versions[1]
                    };
                    list.Add(version);
                }
            }

            return list;
        }
    }
}