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
    public class GadioHelper
    {
        static string cdnPrefix = @"http://alioss.g-cores.com/";
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
                WebRequest request = WebRequest.CreateHttp(new Uri(G));
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
                        WebRequest request1 = WebRequest.CreateHttp(new Uri(thisItem.Link.ToString()));
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
            HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(url);
            webrequest.Timeout = 30000;
            webrequest.ReadWriteTimeout = 30000;
            webrequest.Proxy = null;
            webrequest.KeepAlive = false;
            var webresponse = (HttpWebResponse)webrequest.GetResponse();

            using (Stream sr = webrequest.GetResponse().GetResponseStream())
            using (FileStream sw = File.Create(CleanFileName(path)))
            {
                sr.CopyTo(sw);
            }
        }
        public static void DownloadUri(Uri url, string path)
        {
            HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(url);
            webrequest.Timeout = 30000;
            webrequest.ReadWriteTimeout = 30000;
            webrequest.Proxy = null;
            webrequest.KeepAlive = false;
            var webresponse = (HttpWebResponse)webrequest.GetResponse();

            using (Stream sr = webrequest.GetResponse().GetResponseStream())
            using (FileStream sw = File.Create(CleanFileName(path)))
            {
                sr.CopyTo(sw);
            }
        }
        public static void DownloadItem(GadioItem G, string path)
        {
            string url = G.Audio.mediaSrc.mp3[0];
            DownloadUri(url, path);
            
            //download frontcover
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

    class Program
    {
        static void Main(string[] args)
        {
            JsonSerializer serializer = new JsonSerializer();
            bool needDownload = false;
            int DownloaderMaxDegreeOfParallelism = 4;
            List<GadioItem> GadioItemList = new List<GadioItem>();
            bool clearFlag = false;

            if(clearFlag)
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

            object ConsoleLock = new object();
            int counter = 0;

            List<GadioItem> missingContent = new List<GadioItem>();

            Parallel.ForEach(GadioItemList, new ParallelOptions { MaxDegreeOfParallelism = DownloaderMaxDegreeOfParallelism }, (G) =>
            {
                retry:
                int retryCount = 0;
                try
                {
                    GadioHelper.DownloadItem(G, G.Link.ToString().Split('/').Last() + G.Link.ToString().Substring(G.Link.ToString().LastIndexOf(".") + 1));
                    GadioHelper.DownloadUri(G.FronCoverImg, G.Link.ToString().Split('/').Last() + G.FronCoverImg.ToString().Substring(G.FronCoverImg.ToString().LastIndexOf(".") + 1));

                }
                catch(Exception e)
                {
                    if(retryCount!=10)
                    goto retry;
                }
            });
            using (StreamWriter sw = new StreamWriter("MissingGadioItems.json"))
            {
                using (JsonWriter jw = new JsonTextWriter(sw))
                {
                    serializer.Serialize(jw, missingContent);
                }
            }
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
