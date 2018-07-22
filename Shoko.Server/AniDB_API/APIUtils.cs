﻿using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using NLog;
using Shoko.Server;
using Shoko.Server.AniDB_API;

namespace AniDBAPI
{
    public static class APIUtils
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public const int LastYear = 2050;

        public static string DownloadWebPage(string url)
        {
            try
            {
                AniDbRateLimiter.Instance.EnsureRate();

                HttpWebRequest webReq = (HttpWebRequest) WebRequest.Create(url);
                webReq.Timeout = 20000; // 20 seconds
                webReq.Headers.Add(HttpRequestHeader.AcceptEncoding, "gzip,deflate");
                webReq.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:40.0) Gecko/20100101 Firefox/40.1";

                webReq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                HttpWebResponse WebResponse = (HttpWebResponse) webReq.GetResponse();

                Stream responseStream = WebResponse.GetResponseStream();
                String enco = WebResponse.CharacterSet;
                Encoding encoding = null;
                if (!String.IsNullOrEmpty(enco))
                    encoding = Encoding.GetEncoding(WebResponse.CharacterSet);
                if (encoding == null)
                    encoding = Encoding.Default;
                StreamReader Reader = new StreamReader(responseStream, encoding);

                string output = Reader.ReadToEnd();

                WebResponse.Close();
                responseStream.Close();

                return output;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in APIUtils.DownloadWebPage: {0}");
                return string.Empty;
            }
        }

        public static Stream DownloadWebBinary(string url)
        {
            try
            {
                AniDbRateLimiter.Instance.EnsureRate();

                HttpWebResponse response = null;
                HttpWebRequest webReq = (HttpWebRequest) WebRequest.Create(url);
                // Note: some network proxies require the useragent string to be set or they will deny the http request
                // this is true for instance for EVERY thailand internet connection (also needs to be set for banners/episodethumbs and any other http request we send)
                webReq.UserAgent = "Anime2MP";
                webReq.Timeout = 20000; // 20 seconds
                response = (HttpWebResponse) webReq.GetResponse();

                return response != null 
                    ? response.GetResponseStream() 
                    : null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error in APIUtils.DownloadWebBinary: {0}");
                return null;
            }
        }

        public static XmlDocument LoadAnimeHTTPFromFile(int animeID)
        {
            string filePath = ServerSettings.AnimeXmlDirectory;


            if (!Directory.Exists(filePath))
                Directory.CreateDirectory(filePath);

            string fileName = $"AnimeDoc_{animeID}.xml";
            string fileNameWithPath = Path.Combine(filePath, fileName);

            if (!File.Exists(fileNameWithPath)) return null;
            using (StreamReader re = File.OpenText(fileNameWithPath))
            {
                string rawXML = re.ReadToEnd();

                var docAnime = new XmlDocument();
                docAnime.LoadXml(rawXML);
                return docAnime;
            }
        }

        public static void WriteAnimeHTTPToFile(int animeID, string xml)
        {
            try
            {
                string filePath = ServerSettings.AnimeXmlDirectory;

                if (!Directory.Exists(filePath))
                    Directory.CreateDirectory(filePath);

                string fileName = $"AnimeDoc_{animeID}.xml";
                string fileNameWithPath = Path.Combine(filePath, fileName);

                // First check to make sure we not rights issue
                if (!Utils.IsDirectoryWritable(filePath))
                    Utils.GrantAccess(filePath);

                // Check again and only if write-able we create it
                if (Utils.IsDirectoryWritable(filePath))
                {
                    using (var sw = File.CreateText(fileNameWithPath))
                    {
                        sw.Write(xml);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error occurred during WriteAnimeHTTPToFile(): {ex}");
            }
        }
    }
}