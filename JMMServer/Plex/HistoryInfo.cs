﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentNHibernate.Utils;
using JMMContracts.PlexContracts;

namespace JMMServer.Plex
{
    public class HistoryInfo 
    {

        public string Key { get; set; }
        public string ParentKey { get; set; }
        public string GrandParentKey { get; set; }
        public string Title { get; set; }
        public string ParentTitle { get; set; }
        public string GrandParentTitle { get; set; }
        public string Thumb { get; set; }
        public string ParentThumb { get; set; }
        public string GrandParentThumb { get; set; }
        public string Art { get; set; }
        public string ParentArt { get; set; }
        public string GrandParentArt { get; set; }

        private static int counter = 0;
        private static Dictionary<string, HistoryInfo> Cache=new Dictionary<string, HistoryInfo>(); //TODO CACHE EVICTION?
        
        public HistoryInfo Update(Video v)
        {
            HistoryInfo cache = new HistoryInfo();
            this.CopyTo(cache);
            cache.GrandParentKey = cache.ParentKey;
            cache.GrandParentTitle = cache.ParentTitle ?? "";
            cache.GrandParentArt = cache.ParentArt;
            cache.GrandParentThumb = cache.ParentThumb;
            cache.ParentKey = cache.Key;
            cache.ParentTitle = cache.Title ?? "";
            cache.ParentArt = cache.Art;
            cache.ParentThumb = cache.Thumb;
            cache.Key = v.Key;
            cache.Title = v.Title ?? "";
            cache.Art = v.Art;
            cache.Thumb = v.Thumb;
            return cache;
        }

        private string GenMd5()
        {
            StringBuilder bld=new StringBuilder();
            bld.AppendLine(ParentKey);
            bld.AppendLine(GrandParentKey);
            bld.AppendLine(Title);
            bld.AppendLine(ParentTitle);
            bld.AppendLine(GrandParentTitle);
            bld.AppendLine(Thumb);
            bld.AppendLine(ParentThumb);
            bld.AppendLine(GrandParentThumb);
            bld.AppendLine(Art);
            bld.AppendLine(ParentArt);
            bld.AppendLine(GrandParentArt);
            using (MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider())
            {
                return BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(bld.ToString()))).Replace("-", string.Empty);
            }
        }
        public string ToKey()
        {
            string md5 = GenMd5();
            if (Cache.ContainsKey(md5))
                return md5;
            counter++;
            HistoryInfo cache = new HistoryInfo();
            this.CopyTo(cache);
            Cache.Add(md5,cache);
            return md5;
        }

        public static HistoryInfo FromKey(string key)
        {
            if (Cache.ContainsKey(key))
                return Cache[key];
            return new HistoryInfo();
        }

    }
}
