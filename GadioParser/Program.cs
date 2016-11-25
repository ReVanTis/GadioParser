using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using HtmlAgilityPack;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json;

namespace GadioParser
{
    public class DownloadJob
    {
        public string url;
        public string savePath;
        public DownloadJob() { }
        public DownloadJob(string a,string b)
        {
            url = a;
            savePath = b;
        }
    }
    public class GadioHelper
    {
        static string cdnPrefix = @"http://alioss.g-cores.com";
        public static List<GadioItem> GetItemList(int CrawlerMaxDegreeOfParallelism = 4)
        {
            object ListLock = new object();
            List<GadioItem> GadioItemList = new List<GadioItem>();
            int GadioCounter = 0;
            object GadioCounterLock = new object();

            string HtmlPrefix = @"http://www.g-cores.com/categories/9/originals?page=";
            int maxPages = 22;

            List<string> htmllist = new List<string>();

            for (int c = 1; c <= maxPages; c++)
            {
                htmllist.Add(HtmlPrefix + c);
            }
            Parallel.ForEach(htmllist, new ParallelOptions { MaxDegreeOfParallelism = CrawlerMaxDegreeOfParallelism }, (G) =>
            {

                HtmlDocument GadioDoc = new HtmlDocument();
                WebRequest request = WebRequest.CreateHttp(G);
                request.Method = "GET";
                using (var response = request.GetResponse())
                {
                    using (StreamReader sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        GadioDoc.Load(sr);
                    }
                }
                //DO content handling
                var a = GadioDoc.DocumentNode.Descendants("div").Where(d => d.Attributes.Contains("class") && d.Attributes["class"].Value.Contains("showcase-audio")).ToList();
                foreach (var node in a)
                {
                    lock (GadioCounterLock)
                    {
                        Console.WriteLine(GadioCounter + " Gadio Hit!");
                        GadioCounter++;
                    }
                    GadioItem thisItem = new GadioItem();

                    string ImgUrl = node.Descendants("img").ToList()[0].Attributes["src"].Value;
                    thisItem.FronCoverImg = new Uri(ImgUrl);

                    var titleNode = node.Descendants("h4").ToList()[0];//#

                    string title = titleNode.ChildNodes[1].InnerHtml.Trim();
                    thisItem.Title = title;
                    string link = titleNode.ChildNodes[1].Attributes[1].Value;
                    thisItem.Link = new Uri(link);

                    var childs = node.ChildNodes.Where(d => d.NodeType != HtmlNodeType.Text).ToList();//#
                    string channel = childs[0].ChildNodes[1].ChildNodes[1].InnerHtml.Trim().Replace("\n", "").Replace(" ", "");
                    thisItem.Channel = channel;
                    string channel_link = childs[0].ChildNodes[1].ChildNodes[1].Attributes[1].Value;
                    thisItem.ChannelLink = new Uri(channel_link);
                    DateTime post_date = DateTime.Parse(childs[0].ChildNodes[2].InnerText.Trim());
                    thisItem.PostDate = post_date;
                    var description = childs[2].InnerText;
                    thisItem.Description = description;
                    {
                        if (thisItem.Audio != null)
                            continue;
                        HtmlDocument GadioDoc1 = new HtmlDocument();
                        WebRequest request1 = WebRequest.CreateHttp(thisItem.Link.ToString());
                        request1.Method = "GET";
                        using (var response = request1.GetResponse())
                        {
                            using (StreamReader sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                            {
                                GadioDoc1.Load(sr);
                            }
                        }

                        foreach (var node1 in GadioDoc1.DocumentNode.Descendants("script"))
                        {
                            if (node1.Attributes["type"] != null && node1.Attributes["type"].Value == "text/javascript" && node1.InnerHtml.Contains("AudioPlayer"))
                            {
                                //Console.WriteLine("Audio Hit!");

                                int start = node1.InnerHtml.IndexOf("AudioPlayer(") + "AudioPlayer(".Length;
                                int length = node1.InnerHtml.LastIndexOf(')', node1.InnerHtml.LastIndexOf(')') - 1) - node1.InnerHtml.IndexOf("AudioPlayer(") - "AudioPlayer(".Length;

                                var json = node1.InnerHtml.Substring(start, length);
                                json = json.Replace("mediaSrc", "\"mediaSrc\"");
                                json = json.Replace("timelines", "\"timelines\"");
                                json = json.Replace(@"jplayerSwf: '/jplayer.swf',", "");
                                json = json.Replace("audioId", "\"audioId\"");
                                thisItem.Audio = JsonConvert.DeserializeObject<AudioItem>(json);

                            }
                        }
                    }
                    lock (ListLock)
                    {
                        GadioItemList.Add(thisItem);
                    }
                }
            });

            GadioItemList.Sort((a, b) =>
            {
                var chCom = a.ChannelLink.ToString().Split('/').Last().CompareTo(b.ChannelLink.ToString().Split('/').Last());
                if (chCom == 0)
                {
                    chCom = a.PostDate.CompareTo(b.PostDate);
                }

                if (chCom == 0)
                {
                    float a_ = -1;
                    var valid = float.TryParse(a.Channel.Split('.').Last(), out a_);
                    if (!valid)
                    {
                        a_ = float.Parse(a.Channel.Split('.').Last().Split('-')[0]) + float.Parse(a.Channel.Split('.').Last().Split('-')[1]) / 100f;
                    }

                    float b_ = -1;
                    valid = float.TryParse(b.Channel.Split('.').Last(), out a_);
                    if (!valid)
                    {
                        b_ = float.Parse(b.Channel.Split('.').Last().Split('-')[0]) + float.Parse(b.Channel.Split('.').Last().Split('-')[1]) / 100f;
                    }

                    chCom = a_.CompareTo(b_);
                }

                return chCom;
            });

            return GadioItemList;
        }
        public static string CleanFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        public static void DownloadUri(string url, string path)
        {
            Program.downloadJobs.Add(new DownloadJob() { url = url, savePath = path });
        }
        public static void DownloadUri(Uri url, string path)
        {
            Program.downloadJobs.Add(new DownloadJob() { url = url.ToString(), savePath = path });
        }

        public static List<DownloadJob> DownloadItem(GadioItem G, string path)
        {
            List<DownloadJob> rt = new List<DownloadJob>();
            if(Directory.Exists(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                throw new Exception("path is file.");
            }
            string rootPath = Path.Combine(path, G.Link.ToString().Split('/').Last());

            Directory.CreateDirectory(rootPath);
            

            string url = G.Audio.mediaSrc.mp3[0];
            string mp3FileName = G.Audio.mediaSrc.mp3[0];

            rt.Add(new DownloadJob(url, Path.Combine(rootPath, G.Link.ToString().Split('/').Last() + mp3FileName.ToString().Substring(mp3FileName.ToString().LastIndexOf(".")))));
            rt.Add(new DownloadJob(G.FronCoverImg.ToString(), Path.Combine(rootPath,G.Link.ToString().Split('/').Last() + G.FronCoverImg.ToString().Substring(G.FronCoverImg.ToString().LastIndexOf(".")))));

            foreach (var i in G.Audio.timelines)
            {
                if(!Directory.Exists(Path.Combine(rootPath, "timeline")))
                    Directory.CreateDirectory(Path.Combine(rootPath, "timeline"));
                string assetUrl = "";
                if (i.asset.url == null)
                    continue;
                if(i.asset.url.StartsWith("http"))
                {
                    assetUrl = i.asset.url;
                }
                else
                {
                    assetUrl = cdnPrefix + i.asset.url;
                }

                rt.Add(new DownloadJob(assetUrl, Path.Combine(rootPath,"timeline", assetUrl.Split('/').Last())));
            }
            return rt;
        }
    }
    
    public class GadioItem
    {
        public string Title;
        public string Description;
        public string Channel;
        public DateTime PostDate;
        public Uri Link;
        public Uri FronCoverImg;
        public Uri ChannelLink;
        public AudioItem Audio;
    }
#pragma warning disable IDE1006 // Naming Styles
    public class AudioItem
    {
        public Mediasrc mediaSrc { get; set; }
        public Timeline[] timelines { get; set; }
        public int audioId { get; set; }
    }

    public class Mediasrc
    {
        public string[] mp3 { get; set; }
    }

    public class Timeline
    {
        public int id { get; set; }

        public int media_id { get; set; }
        public int at { get; set; }
        public string title { get; set; }
        public Asset asset { get; set; }
        public string content { get; set; }
        public string quote_href { get; set; }
        public string created_at { get; set; }
        public string updated_at { get; set; }
    }
    public class Asset
    {
        public string url { get; set; }
    }
#pragma warning restore IDE1006 // Naming Styles
    class Program
    {
        public static List<DownloadJob> downloadJobs = new List<DownloadJob>();
        public static object ConsoleLock = new object();
        public static object logLock = new object();
        public static object ConfirmedListLock = new object();
        static void Main(string[] args)
        {
            JsonSerializer serializer = new JsonSerializer();
            bool needDownload = true;
            int DownloaderMaxDegreeOfParallelism = 4;
            List<GadioItem> GadioItemList = new List<GadioItem>();
            bool clearFlag = false;
            
            if (clearFlag)
            {
                if (File.Exists("GadioItems.json"))
                    File.Delete("GadioItems.json");
            }

            if (File.Exists("GadioItems.json"))
            {
                Console.WriteLine("GadioItems.json exists, using local cached json.");
                var contents = File.ReadAllText("GadioItems.json");
                GadioItemList = (List<GadioItem>)JsonConvert.DeserializeObject(contents,GadioItemList.GetType());
            }
            else
            {
                GadioItemList = GadioHelper.GetItemList();

                serializer.Formatting = Formatting.Indented;

                using (StreamWriter sw = new StreamWriter("GadioItems.json"))
                {
                    using (JsonWriter jw = new JsonTextWriter(sw))
                    {
                        serializer.Serialize(jw, GadioItemList);
                    }
                }
            }

            if(false)
            {
                var channels = from x in GadioItemList
                        group x by x.ChannelLink 
                        into g select g;

                foreach(var ch in channels)
                {
                    if(File.Exists(ch.Key.ToString().Split('/').Last() + ".json"))
                    {
                        File.Delete(ch.Key.ToString().Split('/').Last() + ".json");
                    }

                    using (StreamWriter sw = new StreamWriter(ch.Key.ToString().Split('/').Last() + ".json"))
                    {
                        using (JsonWriter jw = new JsonTextWriter(sw))
                        {
                            serializer.Serialize(jw, ch.ToList());
                        }
                    }
                }
            }

            if (!needDownload)
                return;

            if (!Directory.Exists("Audio"))
            {
                Directory.CreateDirectory("Audio");
            }

            
            int counter = 0;

            List<GadioItem> missingContent = new List<GadioItem>();
            List<DownloadJob> failedJob = new List<DownloadJob>();

            foreach(var G in GadioItemList)
            {
                var rt = GadioHelper.DownloadItem(G, "Audio");
                downloadJobs.AddRange(rt);
            }
            object lineLock = new object();
            bool[] lineOccupied = new bool[DownloaderMaxDegreeOfParallelism];
            if (!File.Exists("confirmed.txt"))
                File.Create("confirmed.txt");
            var list = File.ReadAllLines("confirmed.txt");

            Parallel.ForEach(downloadJobs, new ParallelOptions { MaxDegreeOfParallelism = DownloaderMaxDegreeOfParallelism }, (J) =>
            {
                int max_retry = 30;
                int retryCount = 0;

                retry:
                int occu_line = -1;
                lock (lineLock)
                {
                    for (int i = 0; i < DownloaderMaxDegreeOfParallelism; i++)
                    {
                        if (!lineOccupied[i])
                        {
                            lineOccupied[i] = true;
                            occu_line = i + 1;
                            break;
                        }
                    }
                }
                try
                {
                    //if (!File.Exists(J.savePath))
                    {
                        bool confirmedDone = false;
                        lock (ConfirmedListLock)
                        {
                            if (list.Contains(J.url) && (File.Exists(J.savePath)))
                            {
                                confirmedDone = true;
                                lock (ConsoleLock)
                                {
                                    if (occu_line != -1)
                                    {
                                        counter++;
                                        Console.SetCursorPosition(0, occu_line * 2);
                                        ClearCurrentConsoleLine();
                                        Console.WriteLine(retryCount + "/" + max_retry + " Downloaded: " + J.savePath + " already exist.");

                                        Console.SetCursorPosition(0, 0);
                                        ClearCurrentConsoleLine();
                                        Console.WriteLine(retryCount + "/" + max_retry + " Job downloaded: " + counter + " / " + downloadJobs.Count);
                                    }
                                }
                            }
                        }
                        if (!confirmedDone)
                        {
                            lock (ConsoleLock)
                            {
                                if (occu_line != -1)
                                {
                                    Console.SetCursorPosition(0, occu_line * 2);
                                    ClearCurrentConsoleLine();
                                    Console.WriteLine(retryCount + "/" + max_retry + " Connecting : " + J.url);
                                }
                            }

                            using (var client = new WebClient())
                            {
                                HttpWebRequest wr = (HttpWebRequest)WebRequest.CreateHttp(J.url);
                                //wr.Method = "GET";
                                wr.Timeout = 3000;
                                //wr.UserAgent = @"Mozilla / 5.0(Windows NT 10.0; WOW64; rv: 50.0) Gecko / 20100101 Firefox / 50.0";
                                //wr.Referer = new Uri(J.url).Host;
                                //wr.Credentials=CredentialCache.DefaultCredentials;

                                using (var response = wr.GetResponse())
                                {
                                    Int64 fileSize = response.ContentLength;
                                    bool noDownload = false;
                                    if (File.Exists(J.savePath))
                                    {
                                        FileInfo fi = new FileInfo(J.savePath);
                                        if (fi.Length < fileSize)
                                        {
                                            File.Delete(J.savePath);
                                        }
                                        else
                                        {
                                            noDownload = true;

                                            lock (ConsoleLock)
                                            {
                                                if (occu_line != -1)
                                                {
                                                    counter++;
                                                    Console.SetCursorPosition(0, occu_line * 2);
                                                    ClearCurrentConsoleLine();
                                                    Console.WriteLine(retryCount + "/" + max_retry + " Downloaded : " + J.savePath + " already exist.");

                                                    Console.SetCursorPosition(0, 0);
                                                    ClearCurrentConsoleLine();
                                                    Console.WriteLine(retryCount + "/" + max_retry + " Job downloaded: " + counter + " / " + downloadJobs.Count);
                                                }
                                            }
                                        }
                                    }
                                    lock (ConsoleLock)
                                    {
                                        if (occu_line != -1)
                                        {
                                            Console.SetCursorPosition(0, occu_line * 2);
                                            ClearCurrentConsoleLine();
                                            Console.WriteLine(retryCount + "/" + max_retry + " Downloading: " + J.url + " to " + J.savePath);
                                        }
                                    }
                                    if (!noDownload)
                                    {
                                        using (var responseStream = response.GetResponseStream())
                                        {
                                            using (var localStream = File.OpenWrite(J.savePath))
                                            {
                                                byte[] downloadBuffer = new byte[4096];

                                                int bytes = 0;
                                                while ((bytes = responseStream.Read(downloadBuffer, 0, downloadBuffer.Length)) > 0)
                                                {
                                                    localStream.Write(downloadBuffer, 0, bytes);
                                                    if (occu_line != -1)
                                                    {
                                                        double percentage = (double)localStream.Length / (double)fileSize;
                                                        StringBuilder sb = new StringBuilder();
                                                        sb.Append(string.Format("{0,3:000.00}", percentage * 100) + "% [");
                                                        for (float i = 0; i <= 1; i = i + 0.01f)
                                                        {
                                                            if (i <= percentage)
                                                                sb.Append("*");
                                                            else sb.Append(" ");
                                                        }
                                                        sb.Append("] ");

                                                        sb.Append(localStream.Length.ToString("N") + "/" + fileSize.ToString("N"));

                                                        lock (ConsoleLock)
                                                        {
                                                            Console.SetCursorPosition(0, occu_line * 2 + 1);
                                                            ClearCurrentConsoleLine();
                                                            Console.WriteLine(sb);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }

                                lock (ConsoleLock)
                                {
                                    if (occu_line != -1)
                                    {
                                        counter++;
                                        Console.SetCursorPosition(0, 0);
                                        ClearCurrentConsoleLine();
                                        Console.WriteLine("Job downloaded: " + counter + " / " + downloadJobs.Count);
                                    }
                                }
                                lock (ConfirmedListLock)
                                {
                                    File.AppendAllText("confirmed.txt", J.url + "\n");
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    var exMessage = FlattenException(e);
                    exMessage = "Exception occured during downloading: " + J.url + "\n" + exMessage;
                    lock (logLock)
                    {
                        File.AppendAllText("log.txt", DateTime.Now + "\n" + exMessage + "\n");
                    }
                    if (retryCount < max_retry)
                    {
                        retryCount++;
                        goto retry;
                    }
                    failedJob.Add(J);
                    lock (logLock)
                    {
                        File.AppendAllText("log.txt", "ERROR:" + DateTime.Now + ": " + J.url + " failed 30 times.\n");
                    }
                }
                finally
                {
                    lock (lineLock)
                    {
                        lineOccupied[occu_line-1] = false;
                        occu_line = -1;
                    }
                }
            });
            using (StreamWriter sw = new StreamWriter("FailedJob.json"))
            {
                using (JsonWriter jw = new JsonTextWriter(sw))
                {
                    serializer.Serialize(jw, failedJob);
                }
            }
        }
        public static void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }
        public static string FlattenException(Exception exception)
        {
            var stringBuilder = new StringBuilder();
            try
            {
                while (exception != null)
                {
                    stringBuilder.AppendLine(exception.Message);
                    stringBuilder.AppendLine(exception.StackTrace);
                    exception = exception.InnerException;
                }
            }
            catch (Exception e)
            {
                stringBuilder.AppendLine("Error in tracing exception:");
                stringBuilder.AppendLine(e.ToString());
            }
            return stringBuilder.ToString();
        }


    }

}
