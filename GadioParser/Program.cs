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
        [DataMember(Order =4)]
        public Uri Link;
        [DataMember(Order = 5)]
        public Uri FronCoverImg;
        [DataMember(Order = 6)]
        public Uri ChannelLink;
    }

    class Program
    {
        static void Main(string[] args)
        {
            List<GadioItem> GadioItemList = new List<GadioItem>();
            string HtmlPrefix = @"http://www.g-cores.com/categories/9/originals?page=";
            int maxPages = 19;
            for (int i = 1; i <= maxPages; i++)
            {
                HtmlDocument GadioDoc = new HtmlDocument();
                string content = "";
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
                    string link = titleNode.ChildNodes[1].Attributes[0].Value;
                    thisItem.Link = new Uri(link);

                    var childs = node.ChildNodes.Where(d => d.NodeType != HtmlNodeType.Text).ToList();//#
                    string channel = childs[0].ChildNodes[1].ChildNodes[1].InnerHtml.Trim().Replace("\n","").Replace(" ","");
                    thisItem.Channel = channel;
                    string channel_link = childs[0].ChildNodes[1].ChildNodes[1].Attributes[0].Value;
                    thisItem.ChannelLink = new Uri(channel_link);
                    DateTime post_date = DateTime.Parse(childs[0].ChildNodes[2].InnerText.Trim());
                    thisItem.PostDate = post_date;
                    var description = childs[3].InnerText;
                    thisItem.Description = description;

                    GadioItemList.Add(thisItem);
                }
            }
            DataContractJsonSerializer dcjs = new DataContractJsonSerializer(typeof(List<GadioItem>));
            using (FileStream fs = File.OpenWrite("GadioItems"))
            {
                dcjs.WriteObject(fs, GadioItemList);
            }
        }
    }
}
