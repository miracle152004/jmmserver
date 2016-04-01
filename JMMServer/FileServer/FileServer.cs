using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using JMMFileHelper.Subtitles;
using JMMServer.Entities;
using JMMServer.Repositories;
using NLog;
using UPnP;

namespace JMMServer.FileServer
{
    public class FileServer
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private HttpListener _listener;

        private const double WatchedThreshold=0.89; //89% Should be enough to not touch matroska offsets and give us some margin


        private void Run()
        {


            Task.Factory.StartNew(() => {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx = _listener.GetContext(); 
                    new Thread(() =>Process(ctx)).Start(); 
                } 
                }); 

            /*
            ThreadPool.QueueUserWorkItem((o) =>
            {
                Console.WriteLine("FileServer running...");
                try
                {
                    while (_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var obj = c as HttpListenerContext;
                            try
                            {
                                if (obj != null)
                                    Process(obj);
                            }
                            catch { } // suppress any exceptions
                            finally
                            {
                                // always close the stream
                            }
                        }, _listener.GetContext());
                    }
                }
                catch { } // suppress any exceptions
            });
             */

        }

        private static void RunNetSh(string parameter)
        {
            ProcessStartInfo psi = new ProcessStartInfo("netsh", parameter);

            psi.Verb = "runas";
            psi.RedirectStandardOutput = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            System.Diagnostics.Process.Start(psi);
        }

        public static void RegisterFirewallAndHttpUser(int jmmport, int jmmfileport)
        {
            string everyone = new System.Security.Principal.SecurityIdentifier("S-1-1-0").Translate(typeof(System.Security.Principal.NTAccount)).ToString();
            
            //RunNetSh(@"http delete urlacl url=http://*:"+jmmfileport+"/ user=\\" + everyone);
            RunNetSh(@"http add urlacl url=http://*:" + jmmfileport + "/ user=\\" + everyone);
            RunNetSh("advfirewall firewall delete rule name=\"JMM Server - Client Port\"");
            RunNetSh("advfirewall firewall delete rule name=\"JMM Server - File Port\"");
            RunNetSh("advfirewall firewall add rule name=\"JMM Server - Client Port\" dir=in action=allow protocol=TCP localport=" + jmmport);
            RunNetSh("advfirewall firewall add rule name=\"JMM Server - File Port\" dir=in action=allow protocol=TCP localport=" + jmmfileport);
        }

        public static bool UPnPJMMFilePort(int jmmfileport)
        {

            try
            {
                if (NAT.Discover())
                {
                    NAT.ForwardPort(jmmfileport, ProtocolType.Tcp, "JMM File Port");
                    UPnPPortAvailable = true;
                }
                else
                    UPnPPortAvailable = false;
            }
            catch (Exception)
            {
                UPnPPortAvailable = false;
            }

            return UPnPPortAvailable;
        }

        public static bool UPnPPortAvailable { get; private set; }
        private static IPAddress CachedAddress;
        private static DateTime LastChange = DateTime.MinValue;
        private static bool IPThreadLock;
        private static bool IPFirstTime;
        public static IPAddress GetExternalAddress()
        {
            try
            {
                if (LastChange < DateTime.Now)
                {
                    if (IPFirstTime)
                    {
                        IPFirstTime = false;
                        CachedAddress = NAT.GetExternalIP();
                    }
                    else if (!IPThreadLock)
                    {
                        IPThreadLock = true;
                        LastChange = DateTime.Now.AddMinutes(2);
                        ThreadPool.QueueUserWorkItem((a) =>
                        {
                            CachedAddress = NAT.GetExternalIP();
                            IPThreadLock = false;
                        });
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            return CachedAddress;
        }

        public static string Base64DecodeUrl(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData.Replace("-", "+").Replace("_", "/").Replace(",", "="));
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        private static string GetMime(string fullname)
        {
            string ext = Path.GetExtension(fullname).Replace(".", string.Empty).ToLower();
            switch (ext)
            {
                case "png":
                    return "image/png";
                case "jpg":
                    return "image/jpeg";
                case "mkv":
                    return "video/x-matroska";
                case "mka":
                    return "audio/x-matroska";
                case "mk3d":
                    return "video/x-matroska-3d";
                case "avi":
                    return "video/avi";
                case "mp4":
                    return "video/mp4";
                case "mov":
                    return "video/quicktime";
                case "ogm":
                case "ogv":
                    return "video/ogg";
                case "mpg":
                case "mpeg":
                    return "video/mpeg";
                case "flv":
                    return "video/x-flv";
                case "rm":
                    return "application/vnd.rn-realmedia";
            }
            if (SubtitleHelper.Extensions.ContainsKey(ext))
                return SubtitleHelper.Extensions[ext];
            return "application/octet-stream";
        }

        public FileServer(int port, int maxthreads=100)
        {
            _listener=new HttpListener();
            _listener.Prefixes.Add(String.Format(@"http://*:{0}/", port));
            _listener.Start();

        }

        private void Process(System.Net.HttpListenerContext obj)
        {
            Stream org = null;
                
            try
            {

                bool fname = false;
                string[] dta = obj.Request.RawUrl.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (dta.Length < 3)
                    return;
                string cmd = dta[0].ToLower();
                string user = dta[1];
                string arg = dta[2];
                string fullname;
                int userid = 0;
                int.TryParse(user, out userid);
                VideoLocal loc=null;
                if (cmd == "videolocal")
                {
                    int sid = 0;
                    int.TryParse(arg, out sid);
                    if (sid == 0)
                    {
                        obj.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        obj.Response.StatusDescription = "Stream Id missing.";
                        return;
                    }
                    VideoLocalRepository rep = new VideoLocalRepository();
                    loc = rep.GetByID(sid);
                    if (loc == null)
                    {
                        obj.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        obj.Response.StatusDescription = "Stream Id not found.";
                        return;

                    }
                    fullname = loc.FullServerPath;
                }
                else if (cmd == "file")
                {
                    fullname = Base64DecodeUrl(arg);

                }
                else
                {
                    obj.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    obj.Response.StatusDescription = "Not know command";
                    return;
                }

                bool range = false;

                try
                {
                    if (!File.Exists(fullname))
                    {
                        obj.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        obj.Response.StatusDescription = "File '" + fullname + "' not found.";
                        return;
                    }

                }
                catch (Exception)
                {
                    obj.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    obj.Response.StatusDescription = "Unable to access File '" + fullname + "'.";
                    return;
                }
                obj.Response.ContentType = GetMime(fullname);
                obj.Response.AddHeader("Accept-Ranges", "bytes");
                obj.Response.AddHeader("X-Plex-Protocol", "1.0");
                if (obj.Request.HttpMethod == "OPTIONS")
                {
                    obj.Response.AddHeader("Access-Control-Allow-Methods", "POST, GET, OPTIONS, DELETE, PUT, HEAD");
                    obj.Response.AddHeader("Access-Control-Max-Age", "1209600");
                    obj.Response.AddHeader("Access-Control-Allow-Headers",
                        "accept, x-plex-token, x-plex-client-identifier, x-plex-username, x-plex-product, x-plex-device, x-plex-platform, x-plex-platform-version, x-plex-version, x-plex-device-name");
                    obj.Response.AddHeader("Cache-Control", "no-cache");
                    obj.Response.ContentType = "text/plain";
                    return;
                }
                string rangevalue = null;
                if (obj.Request.Headers.AllKeys.Contains("Range"))
                    rangevalue = obj.Request.Headers["Range"].Replace("bytes=", string.Empty).Trim();
                if (obj.Request.Headers.AllKeys.Contains("range"))
                    rangevalue = obj.Request.Headers["range"].Replace("bytes=", string.Empty).Trim();

                if (obj.Request.HttpMethod != "HEAD")
                {
                    org = new FileStream(fullname, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    long totalsize = org.Length;
                    long start = 0;
                    long end = 0;
                    if (!string.IsNullOrEmpty(rangevalue))
                    {
                        range = true;
                        string[] split = rangevalue.Split('-');
                        if (split.Length == 2)
                        {
                            if (string.IsNullOrEmpty(split[0]) && !string.IsNullOrEmpty(split[1]))
                            {
                                long e = long.Parse(split[1]);
                                start = totalsize - e;
                                end = totalsize - 1;
                            }
                            else if (!string.IsNullOrEmpty(split[0]) && string.IsNullOrEmpty(split[1]))
                            {
                                start = long.Parse(split[0]);
                                end = totalsize - 1;
                            }
                            else if (!string.IsNullOrEmpty(split[0]) && !string.IsNullOrEmpty(split[1]))
                            {
                                start = long.Parse(split[0]);
                                end = long.Parse(split[1]);
                                if (start > totalsize - 1)
                                    start = totalsize - 1;
                                if (end > totalsize - 1)
                                    end = totalsize - 1;
                            }
                            else
                            {
                                start = 0;
                                end = totalsize - 1;
                            }
                        }
                    }
                    SubStream outstream;
                    if (range)
                    {
                        obj.Response.StatusCode = (int) HttpStatusCode.PartialContent;
                        obj.Response.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + totalsize);
                        outstream = new SubStream(org, start, end - start + 1);
                        obj.Response.ContentLength64 = end - start + 1;
                    }
                    else
                    {
                        outstream=new SubStream(org,0,totalsize);
                        obj.Response.ContentLength64 = totalsize;
                        obj.Response.StatusCode = (int) HttpStatusCode.OK;
                    }
                    if ((userid != 0) && (loc != null))
                    {
                        outstream.CrossPosition = (long) ((double) totalsize*WatchedThreshold);
                        outstream.CrossPositionCrossed += (a) =>
                        {
                            Task.Factory.StartNew(() =>
                            {
                                loc.ToggleWatchedStatus(true, userid);
                            }, new CancellationToken(), TaskCreationOptions.LongRunning, TaskScheduler.Default);
                        };
                    }
                    obj.Response.SendChunked = false;
                    outstream.CopyTo(obj.Response.OutputStream);
                    obj.Response.OutputStream.Close();
                    outstream.Close();
                }
                else
                {
                    obj.Response.SendChunked = false;
                    obj.Response.StatusCode = (int)HttpStatusCode.OK;
                    obj.Response.ContentLength64 = new FileInfo(fullname).Length;
                    obj.Response.KeepAlive = false;
                    obj.Response.OutputStream.Close();
                }
            }
            catch (HttpListenerException e)
            {
            }
            catch (Exception e)
            {
                logger.Error(e.ToString);
            }
            finally
            {
                if (org != null)
                    org.Close();
                if ((obj != null) && (obj.Response != null) && (obj.Response.OutputStream != null))
                    obj.Response.OutputStream.Close();                
            }
        }

        public void Start()
        {
            Run();
        }

        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}