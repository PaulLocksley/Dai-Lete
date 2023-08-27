using System.Data;
using Microsoft.AspNetCore.Mvc;
using System.ServiceModel.Syndication;
using System.Text;
using System.Xml;
using Dai_Lete.Models;
using Dai_Lete.Repositories;
using Dai_Lete.ScheduledTasks;
using Dapper;

namespace Dai_Lete.Controllers;
[ApiController]
[Route("[controller]")]
public class PodcastController : Controller
{
    [HttpPost("add")]
    public Guid addPodcast(Uri inUri)
    {
        var p = new Podcast(inUri);
        var sql = @"INSERT INTO Podcasts (inuri,outuri,id)  VALUES (@InUri,@OutUri,@Id)";
        Console.WriteLine($"obj: {p.InUri.ToString()} , {p.OutUri},{p.Id}");
        var rows = SqLite.Connection().Execute(sql, new {InUri = p.InUri.ToString(), OutUri = p.OutUri, Id = p.Id});
        if (rows != 1)
        {
            throw new DataException();
        }

        return p.Id;

    }
    
    [HttpGet("list-podcasts")]
    [Produces("application/json", "application/xml")]
    public IActionResult listPodcasts()
    {
        return Ok(PodcastServices.GetPodcasts().Select(x => x.ToString())); //todo: Work out how to setup default serialization because this is stupid.
    }

    [HttpGet("podcast-feed")]
    [Produces("application/xml")]
    public ContentResult getFeed(Guid id)
    {
        var RssFeed = new XmlDocument();
        var sql = @"SELECT * FROM Podcasts where id = @id";
        Podcast podcast = SqLite.Connection().QueryFirst<Podcast>(sql,new {id});

        sql = @"SELECT Id,FileSize FROM Episodes where PodcastId = @pid";
        var episodes = SqLite.Connection().Query(sql, new { pid = id }).ToDictionary(
                row=> (string)row.Id, 
                row => (int)row.FileSize
                );
        if (podcast is null)
        {
            throw new FileNotFoundException($"Failed to locate podcast with id {id}");
        }

        try
        {
            using (var reader = XmlReader.Create(podcast.InUri.ToString()))
            {
                RssFeed.Load(reader);
            }
        }
        catch
        {
            throw new Exception($"Failed to parse {podcast.InUri}");
        }
        Console.WriteLine();
        Console.WriteLine("Break");
        foreach (XmlElement node in RssFeed.DocumentElement.ChildNodes)
        {
            foreach (XmlElement n2 in node.ChildNodes)
            {
                if (n2.Name != "item")
                {
                    continue;
                }

                XmlNode? guid = null;
                XmlNode? enclosure = null;
                foreach (XmlElement n3 in n2.ChildNodes)
                {
                    if (n3.Name == "guid") { guid = n3;}
                    if (n3.Name == "enclosure") { enclosure = n3;}
                }

                if(episodes.ContainsKey(guid.InnerText))
                {
                    foreach (XmlAttribute atr in enclosure.Attributes)
                    {
                        switch (atr.Name)
                        {
                            case "url":
                                atr.Value = $"https://{HttpContext.Request.Host}/Podcasts/{id}/{guid.InnerText}.mp3";
                                break;
                            case "length":
                                atr.Value = "2312";
                                break;
                            case "type":
                                atr.Value = "audio/mpeg";
                                break;
                        }
                    }
                }
                //Console.WriteLine(n2.Name);
            }
        }
        return Content(RssFeed.OuterXml, "application/xml");
        //
        //return sb.ToString();
    }

    /*public string Add(string inUri)
    {
        return inUri;
    }*/
}