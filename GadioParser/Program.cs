using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using HtmlAgilityPack;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Threading;

namespace GadioParser
{
    [DataContract]
    public class GadioItem
    {
        [DataMember(Order = 0)]
        public string Title;
        [DataMember(Order = 1)]
        public string Description;
        [DataMember(Order = 2)]
        public string Channel;
        [DataMember(Order = 3)]
        public DateTime PostDate;
        [DataMember(Order = 4)]
        public Uri Link;
        [DataMember(Order = 5)]
        public Uri FronCoverImg;
        [DataMember(Order = 6)]
        public Uri ChannelLink;
        [DataMember(Order = 7)]
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
            List<GadioItem> GadioItemList = new List<GadioItem>();

            if (File.Exists("GadioItems.json"))
            {
                Console.WriteLine("GadioItems.json exists, using local cached json.");
                DataContractJsonSerializer dcjs = new DataContractJsonSerializer(typeof(List<GadioItem>));
                using (FileStream fs = File.OpenRead("GadioItems.json"))
                {
                    GadioItemList = (List<GadioItem>)dcjs.ReadObject(fs);
                }
            }
            else
            {
                string HtmlPrefix = @"http://www.g-cores.com/categories/9/originals?page=";
                int maxPages = 25;
                for (int i = 1; i <= maxPages; i++)
                {
                    HtmlDocument GadioDoc = new HtmlDocument();
                    WebRequest request = WebRequest.CreateHttp(new Uri(HtmlPrefix + i));
                    request.Method = "GET";
                    using (var response = request.GetResponse())
                    {
                        using (StreamReader sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            GadioDoc.Load(sr);
                        }
                    }
                    //DO content handling

                    foreach (var node in GadioDoc.DocumentNode.Descendants("div").Where(d =>
                    d.Attributes.Contains("class") && d.Attributes["class"].Value.Contains("showcase-audio")))
                    {
                        Console.WriteLine("Gadio Hit!");
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
                        var description = childs[3].InnerText;
                        thisItem.Description = description;
                        {
                            if (thisItem.Audio != null)
                                continue;
                            HtmlDocument GadioDoc1 = new HtmlDocument();
                            WebRequest request1 = WebRequest.CreateHttp(new Uri(thisItem.Link.ToString()));
                            request.Method = "GET";
                            using (var response = request.GetResponse())
                            {
                                using (StreamReader sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                                {
                                    GadioDoc.Load(sr);
                                }
                            }

                            foreach (var node1 in GadioDoc.DocumentNode.Descendants("script"))
                            {
                                if (node1.Attributes["type"] != null && node1.Attributes["type"].Value == "text/javascript" && node1.InnerHtml.Contains("AudioPlayer"))
                                {
                                    Console.WriteLine("Audio Hit!");

                                    int start = node1.InnerHtml.IndexOf("AudioPlayer(") + "AudioPlayer(".Length;
                                    int length = node1.InnerHtml.LastIndexOf(')', node1.InnerHtml.LastIndexOf(')') - 1) - node1.InnerHtml.IndexOf("AudioPlayer(") - "AudioPlayer(".Length;

                                    var json = node1.InnerHtml.Substring(start, length);
                                    json = json.Replace("mediaSrc", "\"mediaSrc\"");
                                    json = json.Replace("timelines", "\"timelines\"");
                                    json = json.Replace(@"jplayerSwf: '/jplayer.swf',", "");
                                    json = json.Replace("audioId", "\"audioId\"");
                                    DataContractJsonSerializer dcjsAudioItem = new DataContractJsonSerializer(typeof(AudioItem));
                                    using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                                    {
                                        var item = (AudioItem)dcjsAudioItem.ReadObject(ms);
                                        thisItem.Audio = item;
                                    }
                                }
                            }
                        }
                        GadioItemList.Add(thisItem);

                    }
                }
                DataContractJsonSerializer dcjs = new DataContractJsonSerializer(typeof(List<GadioItem>));
                using (FileStream fs = File.OpenWrite("GadioItems.json"))
                {
                    dcjs.WriteObject(fs, GadioItemList);
                }
            }

            
            if (!Directory.Exists("Audio"))
            {
                Directory.CreateDirectory("Audio");
            }

            object ConsoleLock = new object();
            int counter = 0;

            List<GadioItem> missingContent = new List<GadioItem>();

            Parallel.ForEach(GadioItemList, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (G) =>
             {
                 int retrycount = 0;
                 retry:
                 if (File.Exists(Path.Combine("Audio", G.Channel + "." + G.Title + ".mp3"))||File.Exists(CleanFileName(Path.Combine("Audio", G.Channel + "." + G.Title + ".mp3"))))
                 {
                     lock (ConsoleLock)
                     {
                         counter++;
                         Console.WriteLine(Path.Combine("["+counter+"/"+GadioItemList.Count+"]"+"+Audio", G.Channel + "." + G.Title + ".mp3") + "already downloaded.");
                     }
                     return;
                 }
                 try
                 {
                     string url = G.Audio.mediaSrc.mp3[0];
                     HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(url);
                     webrequest.Timeout = 30000;
                     webrequest.ReadWriteTimeout = 30000;
                     webrequest.Proxy = null;
                     webrequest.KeepAlive = false;
                     var webresponse = (HttpWebResponse)webrequest.GetResponse();

                     using (Stream sr = webrequest.GetResponse().GetResponseStream())
                     using (FileStream sw = File.Create(CleanFileName(Path.Combine("Audio",G.Channel+"."+G.Title+".mp3"))))
                     {
                         sr.CopyTo(sw);
                     }

                     lock (ConsoleLock)
                     {
                         counter++;
                         Console.WriteLine(Path.Combine("[" + counter + "/" + GadioItemList.Count + "]"+"Audio", G.Channel + "." + G.Title + ".mp3") + " done.");
                     }
                 }

                 catch (Exception ee)
                 {
                     lock (ConsoleLock)
                     {
                         Console.WriteLine("Exception occured during processing: " + Path.Combine("Audio", G.Channel + "." + G.Title + ".mp3"));
                         Console.Write(FlattenException(ee));
                         Console.WriteLine();
                     }
                     if(File.Exists(Path.Combine("Audio", G.Channel + "." + G.Title + ".mp3")))
                     {
                         File.Delete(Path.Combine("Audio", G.Channel + "." + G.Title + ".mp3"));
                     }
                     retrycount++;
                     if (retrycount == 10)
                     {
                         lock (ConsoleLock)
                         {
                             Console.WriteLine("Max retry count reached: " + Path.Combine("Audio", G.Channel + "." + G.Title + ".mp3"));
                             missingContent.Add(G);
                         }
                         return;
                     }
                     Thread.Sleep(5000);

                     goto retry;
                 }
             });
            DataContractJsonSerializer dcjsmissing = new DataContractJsonSerializer(typeof(List<GadioItem>));
            using (FileStream fs = File.OpenWrite("MissingGadioItems.json"))
            {
                dcjsmissing.WriteObject(fs, missingContent);
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
        public static string CleanFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

    }

}
